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
/// acá: así los tests pueden probar dos estancias sin simular logins.
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
                    "Cada estancia lleva la suya: la de otra estancia no sirve acá.");
            secuencia = Mapear(reader);
        }

        if (secuencia.EstaVencida(hoy))
            throw new InvalidOperationException(
                $"La secuencia de comprobantes {secuencia.Prefijo} venció el {secuencia.Vencimiento:dd/MM/yyyy}. " +
                "Solicitá una nueva a la DGII y actualizá la configuración.");
        if (secuencia.EstaAgotada)
            throw new InvalidOperationException(
                $"La secuencia de comprobantes {secuencia.Prefijo} se agotó (fin del rango autorizado). " +
                "Solicitá una nueva a la DGII y actualizá la configuración.");

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
