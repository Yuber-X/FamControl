using MySqlConnector;
using FAControl.Common;
using FAControl.Models;

namespace FAControl.Data;

/// <summary>
/// Secuencia de comprobantes fiscales (012_ncf.sql). La reserva del siguiente
/// NCF es atómica (SELECT ... FOR UPDATE dentro de la transacción del préstamo):
/// dos préstamos simultáneos jamás reciben el mismo comprobante, y un rollback
/// no consume el número.
///
/// UNA SECUENCIA POR MODO (030). Cada estancia lleva su propio rango: un negocio
/// de varios rubros puede tener una autorización de la DGII por cada uno, o hasta
/// otro RNC. Compartir el rango entregaría comprobantes que la DGII espera de un
/// único libro de ventas. El modo llega por parámetro y no se lee de SesionActual
/// aquí: así los tests pueden probar dos estancias sin simular logins.
/// </summary>
public class NcfRepository
{
    private readonly ConexionFactory _factory;

    public NcfRepository(ConexionFactory factory) => _factory = factory;

    /// <summary>La secuencia activa DE ESE MODO, o null si esa estancia no configuró ninguna.</summary>
    public async Task<NcfSecuencia?> ObtenerActivaAsync(ModoApp modo, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT id, prefijo, largo, proxima, fin_rango, vencimiento, activo
            FROM {DbNames.NcfSecuencia}
            WHERE modo = @modo AND activo = 1
            ORDER BY id
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("@modo", modo.ClaveDb());
        using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;
        return Mapear(reader);
    }

    /// <summary>Crea o actualiza la secuencia de ese modo (upsert por modo + prefijo).</summary>
    public async Task GuardarAsync(ModoApp modo, NcfSecuencia secuencia, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {DbNames.NcfSecuencia}
              (modo, prefijo, largo, proxima, fin_rango, vencimiento, activo)
            VALUES
              (@modo, @prefijo, @largo, @proxima, @fin, @vencimiento, @activo)
            ON DUPLICATE KEY UPDATE
              largo = @largo, proxima = @proxima, fin_rango = @fin,
              vencimiento = @vencimiento, activo = @activo, updated_at = UTC_TIMESTAMP();
            """;
        cmd.Parameters.AddWithValue("@modo", modo.ClaveDb());
        cmd.Parameters.AddWithValue("@prefijo", secuencia.Prefijo);
        cmd.Parameters.AddWithValue("@largo", secuencia.Largo);
        cmd.Parameters.AddWithValue("@proxima", secuencia.Proxima);
        cmd.Parameters.AddWithValue("@fin", (object?)secuencia.FinRango ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@vencimiento",
            (object?)secuencia.Vencimiento?.ToDateTime(TimeOnly.MinValue) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@activo", secuencia.Activo);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Desactiva las secuencias DE ESE MODO (esa estancia dejó de usar NCF
    /// local). Las de las demás no se tocan: apagar el comprobante del punto de
    /// venta no puede dejar sin numeración a los préstamos.
    /// </summary>
    public async Task DesactivarTodasAsync(ModoApp modo, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText =
            $"UPDATE {DbNames.NcfSecuencia} SET activo = 0, updated_at = UTC_TIMESTAMP() WHERE modo = @modo;";
        cmd.Parameters.AddWithValue("@modo", modo.ClaveDb());
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Adopta como PREDETERMINADO el comprobante que se acaba de usar a mano,
    /// para que la secuencia siga a partir de él (pedido del cliente
    /// 2026-09-03: "si se digita un NCF y la operación sale bien, ese mismo NCF
    /// se toma como el predeterminado y se agrega en Configuración para
    /// continuar la secuencia").
    ///
    /// Devuelve true si movió algo. No hace nada —y no falla— cuando:
    ///  * el texto no tiene forma de comprobante de la DGII, o
    ///  * es de la MISMA serie y su número ya quedó atrás.
    ///
    /// Ese segundo caso es deliberado y es la única parte donde no se sigue el
    /// pedido al pie de la letra: retroceder la secuencia dentro de la misma
    /// serie volvería a entregar números ya consumidos, que la DGII prohíbe
    /// reusar y que las claves únicas (uq_prestamo_ncf, uq_pago_ncf) rechazarían
    /// más tarde con un error de base de datos en la cara del cajero. Si la
    /// serie CAMBIA sí se adopta tal cual: un talonario nuevo trae su propia
    /// numeración y no pisa nada.
    ///
    /// El rango y el vencimiento se conservan si la serie es la misma; si es
    /// otra quedan sin tope hasta que el Admin cargue la autorización.
    /// </summary>
    public async Task<bool> AdoptarComoPredeterminadaAsync(ModoApp modo, string? ncfUsado,
        CancellationToken ct = default)
    {
        if (NcfSecuencia.Descomponer(ncfUsado) is not { } partes)
            return false;
        var (prefijo, numero, largo) = partes;
        var siguiente = numero + 1;

        using var conexion = await _factory.AbrirAsync(ct);
        using var transaccion = await conexion.BeginTransactionAsync(ct);
        try
        {
            NcfSecuencia? activa = null;
            using (var select = conexion.CreateCommand())
            {
                select.Transaction = transaccion;
                select.CommandText = $"""
                    SELECT id, prefijo, largo, proxima, fin_rango, vencimiento, activo
                    FROM {DbNames.NcfSecuencia}
                    WHERE modo = @modo AND activo = 1
                    ORDER BY id
                    LIMIT 1
                    FOR UPDATE;
                    """;
                select.Parameters.AddWithValue("@modo", modo.ClaveDb());
                using var reader = await select.ExecuteReaderAsync(ct);
                if (await reader.ReadAsync(ct))
                    activa = Mapear(reader);
            }

            if (activa is not null && activa.Prefijo == prefijo && siguiente <= activa.Proxima)
                return false;

            // Serie distinta: la anterior se apaga para que ObtenerActivaAsync no
            // siga devolviéndola (toma la PRIMERA activa por id, no la última).
            if (activa is null || activa.Prefijo != prefijo)
            {
                using var apagar = conexion.CreateCommand();
                apagar.Transaction = transaccion;
                apagar.CommandText = $"UPDATE {DbNames.NcfSecuencia} " +
                    "SET activo = 0, updated_at = UTC_TIMESTAMP() WHERE modo = @modo AND activo = 1;";
                apagar.Parameters.AddWithValue("@modo", modo.ClaveDb());
                await apagar.ExecuteNonQueryAsync(ct);
            }

            using (var guardar = conexion.CreateCommand())
            {
                guardar.Transaction = transaccion;
                // El rango y el vencimiento solo se conservan si la serie es la
                // misma; una serie nueva queda sin tope hasta que el Admin cargue
                // la autorización de la DGII en Configuración.
                guardar.CommandText = $"""
                    INSERT INTO {DbNames.NcfSecuencia}
                      (modo, prefijo, largo, proxima, fin_rango, vencimiento, activo)
                    VALUES
                      (@modo, @prefijo, @largo, @proxima, NULL, NULL, 1)
                    ON DUPLICATE KEY UPDATE
                      largo = @largo, proxima = @proxima, activo = 1,
                      updated_at = UTC_TIMESTAMP();
                    """;
                guardar.Parameters.AddWithValue("@modo", modo.ClaveDb());
                guardar.Parameters.AddWithValue("@prefijo", prefijo);
                guardar.Parameters.AddWithValue("@largo", largo);
                guardar.Parameters.AddWithValue("@proxima", siguiente);
                await guardar.ExecuteNonQueryAsync(ct);
            }

            await transaccion.CommitAsync(ct);
            return true;
        }
        catch
        {
            await transaccion.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    /// <summary>
    /// Reserva el siguiente NCF de la secuencia activa DENTRO de la transacción
    /// dada. Tira si no hay secuencia activa, está vencida o agotada.
    /// </summary>
    public async Task<string> ReservarSiguienteAsync(ModoApp modo, MySqlConnection conexion,
        MySqlTransaction transaccion, DateOnly hoy, CancellationToken ct = default)
    {
        NcfSecuencia secuencia;
        using (var select = conexion.CreateCommand())
        {
            select.Transaction = transaccion;
            select.CommandText = $"""
                SELECT id, prefijo, largo, proxima, fin_rango, vencimiento, activo
                FROM {DbNames.NcfSecuencia}
                WHERE modo = @modo AND activo = 1
                ORDER BY id
                LIMIT 1
                FOR UPDATE;
                """;
            select.Parameters.AddWithValue("@modo", modo.ClaveDb());
            using var reader = await select.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                throw new InvalidOperationException(
                    $"{IdentidadModo.De(modo).Nombre} no tiene una secuencia de comprobantes fiscales " +
                    "configurada. Configurala en Configuración → Comprobante fiscal. " +
                    "Cada estancia lleva la suya: la de otra estancia no sirve aquí.");
            secuencia = Mapear(reader);
        }

        if (secuencia.EstaVencida(hoy))
            throw new InvalidOperationException(
                $"La secuencia de comprobantes {secuencia.Prefijo} venció el {secuencia.Vencimiento:dd/MM/yyyy}. " +
                "Solicita una nueva a la DGII y actualiza la configuración.");
        if (secuencia.EstaAgotada)
            throw new InvalidOperationException(
                $"La secuencia de comprobantes {secuencia.Prefijo} se agotó (fin del rango autorizado). " +
                "Solicita una nueva a la DGII y actualiza la configuración.");

        var ncf = secuencia.Formatear(secuencia.Proxima);

        using (var update = conexion.CreateCommand())
        {
            update.Transaction = transaccion;
            update.CommandText = $"""
                UPDATE {DbNames.NcfSecuencia}
                SET proxima = proxima + 1, updated_at = UTC_TIMESTAMP()
                WHERE id = @id;
                """;
            update.Parameters.AddWithValue("@id", secuencia.Id);
            await update.ExecuteNonQueryAsync(ct);
        }

        return ncf;
    }

    private static NcfSecuencia Mapear(MySqlDataReader reader) => new()
    {
        Id = reader.GetInt32("id"),
        Prefijo = reader.GetString("prefijo"),
        Largo = reader.GetInt32("largo"),
        Proxima = reader.GetInt64("proxima"),
        FinRango = reader.IsDBNull(reader.GetOrdinal("fin_rango")) ? null : reader.GetInt64("fin_rango"),
        Vencimiento = reader.IsDBNull(reader.GetOrdinal("vencimiento"))
            ? null
            : DateOnly.FromDateTime(reader.GetDateTime("vencimiento")),
        Activo = reader.GetBoolean("activo")
    };
}
