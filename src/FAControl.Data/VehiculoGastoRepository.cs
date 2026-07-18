using MySqlConnector;
using FAControl.Common;
using FAControl.Models;

namespace FAControl.Data;

/// <summary>
/// Acceso a vehiculo_gasto (gestión de importación). El alta/baja va en la
/// transacción del Service, que luego recalcula gastos_importacion del vehículo.
/// </summary>
public class VehiculoGastoRepository
{
    private readonly ConexionFactory _factory;

    public VehiculoGastoRepository(ConexionFactory factory) => _factory = factory;

    public async Task<long> InsertarAsync(VehiculoGasto gasto, MySqlConnection conexion,
        MySqlTransaction transaccion, CancellationToken ct = default)
    {
        using var cmd = conexion.CreateCommand();
        cmd.Transaction = transaccion;
        cmd.CommandText = $"""
            INSERT INTO {DbNames.VehiculoGasto} (vehiculo_id, concepto, monto, fecha, created_by)
            VALUES (@vehiculoId, @concepto, @monto, @fecha, @createdBy);
            SELECT LAST_INSERT_ID();
            """;
        cmd.Parameters.AddWithValue("@vehiculoId", gasto.VehiculoId);
        cmd.Parameters.AddWithValue("@concepto", gasto.Concepto);
        cmd.Parameters.AddWithValue("@monto", gasto.Monto);
        cmd.Parameters.AddWithValue("@fecha", gasto.Fecha.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@createdBy", SesionActual.HaySesionActiva ? SesionActual.Id : (object)DBNull.Value);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task EliminarAsync(long id, MySqlConnection conexion,
        MySqlTransaction transaccion, CancellationToken ct = default)
    {
        using var cmd = conexion.CreateCommand();
        cmd.Transaction = transaccion;
        cmd.CommandText = $"DELETE FROM {DbNames.VehiculoGasto} WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Suma de los gastos del vehículo (para reflejar en gastos_importacion), dentro de la transacción.</summary>
    public async Task<decimal> SumarAsync(long vehiculoId, MySqlConnection conexion,
        MySqlTransaction transaccion, CancellationToken ct = default)
    {
        using var cmd = conexion.CreateCommand();
        cmd.Transaction = transaccion;
        cmd.CommandText = $"SELECT COALESCE(SUM(monto), 0) FROM {DbNames.VehiculoGasto} WHERE vehiculo_id = @vehiculoId;";
        cmd.Parameters.AddWithValue("@vehiculoId", vehiculoId);
        return Convert.ToDecimal(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task<long?> ObtenerVehiculoIdAsync(long gastoId, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"SELECT vehiculo_id FROM {DbNames.VehiculoGasto} WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", gastoId);
        var r = await cmd.ExecuteScalarAsync(ct);
        return r is null or DBNull ? null : Convert.ToInt64(r);
    }

    public async Task<IReadOnlyList<VehiculoGasto>> ObtenerPorVehiculoAsync(long vehiculoId, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT id, vehiculo_id, concepto, monto, fecha, created_at
            FROM {DbNames.VehiculoGasto}
            WHERE vehiculo_id = @vehiculoId
            ORDER BY fecha, id;
            """;
        cmd.Parameters.AddWithValue("@vehiculoId", vehiculoId);

        var lista = new List<VehiculoGasto>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            lista.Add(new VehiculoGasto
            {
                Id = reader.GetInt64("id"),
                VehiculoId = reader.GetInt64("vehiculo_id"),
                Concepto = reader.GetString("concepto"),
                Monto = reader.GetDecimal("monto"),
                Fecha = DateOnly.FromDateTime(reader.GetDateTime("fecha")),
                CreatedAtUtc = DateTime.SpecifyKind(reader.GetDateTime("created_at"), DateTimeKind.Utc)
            });
        }
        return lista;
    }
}
