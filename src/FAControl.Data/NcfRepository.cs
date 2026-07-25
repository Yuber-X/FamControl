using MySqlConnector;
using FAControl.Common;
using FAControl.Models;

namespace FAControl.Data;

/// <summary>
/// Secuencia de comprobantes fiscales (012_ncf.sql). La reserva del siguiente
/// NCF es atómica (SELECT ... FOR UPDATE dentro de la transacción del préstamo):
/// dos préstamos simultáneos jamás reciben el mismo comprobante, y un rollback
/// no consume el número.
/// </summary>
public class NcfRepository
{
    private readonly ConexionFactory _factory;

    public NcfRepository(ConexionFactory factory) => _factory = factory;

    /// <summary>La secuencia activa (hoy se maneja una sola), o null si no hay.</summary>
    public async Task<NcfSecuencia?> ObtenerActivaAsync(CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT id, prefijo, largo, proxima, fin_rango, vencimiento, activo
            FROM {DbNames.NcfSecuencia}
            WHERE activo = 1
            ORDER BY id
            LIMIT 1;
            """;
        using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;
        return Mapear(reader);
    }

    /// <summary>Crea o actualiza la configuración de la secuencia (upsert por prefijo).</summary>
    public async Task GuardarAsync(NcfSecuencia secuencia, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {DbNames.NcfSecuencia}
              (prefijo, largo, proxima, fin_rango, vencimiento, activo)
            VALUES
              (@prefijo, @largo, @proxima, @fin, @vencimiento, @activo)
            ON DUPLICATE KEY UPDATE
              largo = @largo, proxima = @proxima, fin_rango = @fin,
              vencimiento = @vencimiento, activo = @activo, updated_at = UTC_TIMESTAMP();
            """;
        cmd.Parameters.AddWithValue("@prefijo", secuencia.Prefijo);
        cmd.Parameters.AddWithValue("@largo", secuencia.Largo);
        cmd.Parameters.AddWithValue("@proxima", secuencia.Proxima);
        cmd.Parameters.AddWithValue("@fin", (object?)secuencia.FinRango ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@vencimiento",
            (object?)secuencia.Vencimiento?.ToDateTime(TimeOnly.MinValue) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@activo", secuencia.Activo);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Desactiva todas las secuencias (el negocio dejó de usar NCF local).</summary>
    public async Task DesactivarTodasAsync(CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"UPDATE {DbNames.NcfSecuencia} SET activo = 0, updated_at = UTC_TIMESTAMP();";
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Reserva el siguiente NCF de la secuencia activa DENTRO de la transacción
    /// dada. Tira si no hay secuencia activa, está vencida o agotada.
    /// </summary>
    public async Task<string> ReservarSiguienteAsync(MySqlConnection conexion,
        MySqlTransaction transaccion, DateOnly hoy, CancellationToken ct = default)
    {
        NcfSecuencia secuencia;
        using (var select = conexion.CreateCommand())
        {
            select.Transaction = transaccion;
            select.CommandText = $"""
                SELECT id, prefijo, largo, proxima, fin_rango, vencimiento, activo
                FROM {DbNames.NcfSecuencia}
                WHERE activo = 1
                ORDER BY id
                LIMIT 1
                FOR UPDATE;
                """;
            using var reader = await select.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                throw new InvalidOperationException(
                    "No hay una secuencia de comprobantes fiscales configurada. " +
                    "Configurala en Configuración → Comprobante fiscal.");
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
