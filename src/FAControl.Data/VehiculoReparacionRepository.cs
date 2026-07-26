using FAControl.Common;
using FAControl.Models;

namespace FAControl.Data;

/// <summary>
/// Historial de reparaciones/mantenimientos del vehículo (015 — 2026-07-25).
/// Soft delete; las lecturas filtran deleted_at IS NULL.
/// </summary>
public class VehiculoReparacionRepository
{
    private readonly ConexionFactory _factory;

    public VehiculoReparacionRepository(ConexionFactory factory) => _factory = factory;

    public async Task<long> InsertarAsync(VehiculoReparacion reparacion, long usuarioId,
        CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {DbNames.VehiculoReparacion}
              (vehiculo_id, fecha, detalle, costo, created_by)
            VALUES
              (@vehiculoId, @fecha, @detalle, @costo, @usuarioId);
            SELECT LAST_INSERT_ID();
            """;
        cmd.Parameters.AddWithValue("@vehiculoId", reparacion.VehiculoId);
        cmd.Parameters.AddWithValue("@fecha", reparacion.Fecha.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@detalle", reparacion.Detalle);
        cmd.Parameters.AddWithValue("@costo", reparacion.Costo);
        cmd.Parameters.AddWithValue("@usuarioId", usuarioId);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task EliminarAsync(long id, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {DbNames.VehiculoReparacion}
            SET deleted_at = UTC_TIMESTAMP()
            WHERE id = @id AND deleted_at IS NULL;
            """;
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Reparaciones del vehículo, la más reciente primero.</summary>
    public async Task<IReadOnlyList<VehiculoReparacion>> ObtenerDeVehiculoAsync(long vehiculoId,
        CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT r.id, r.vehiculo_id, r.fecha, r.detalle, r.costo, u.nombre AS registrada_por
            FROM {DbNames.VehiculoReparacion} r
            LEFT JOIN {DbNames.Usuario} u ON u.id = r.created_by
            WHERE r.vehiculo_id = @vehiculoId AND r.deleted_at IS NULL
            ORDER BY r.fecha DESC, r.id DESC;
            """;
        cmd.Parameters.AddWithValue("@vehiculoId", vehiculoId);

        var lista = new List<VehiculoReparacion>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            lista.Add(new VehiculoReparacion
            {
                Id = reader.GetInt64("id"),
                VehiculoId = reader.GetInt64("vehiculo_id"),
                Fecha = DateOnly.FromDateTime(reader.GetDateTime("fecha")),
                Detalle = reader.GetString("detalle"),
                Costo = reader.GetDecimal("costo"),
                RegistradaPor = reader.IsDBNull(reader.GetOrdinal("registrada_por"))
                    ? null : reader.GetString("registrada_por")
            });
        return lista;
    }
}
