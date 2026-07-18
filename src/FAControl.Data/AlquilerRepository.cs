using MySqlConnector;
using FAControl.Common;
using FAControl.Models;

namespace FAControl.Data;

/// <summary>
/// Acceso a alquiler (rent a car). El alta va en la transacción del Service
/// (código + alquiler + marcar el vehículo 'alquilado' + auditoría). La
/// devolución/cancelación cambia el estado y libera el vehículo.
/// </summary>
public class AlquilerRepository
{
    private readonly ConexionFactory _factory;

    public AlquilerRepository(ConexionFactory factory) => _factory = factory;

    public async Task<long> InsertarAsync(Alquiler alquiler, MySqlConnection conexion,
        MySqlTransaction transaccion, CancellationToken ct = default)
    {
        using var cmd = conexion.CreateCommand();
        cmd.Transaction = transaccion;
        cmd.CommandText = $"""
            INSERT INTO {DbNames.Alquiler}
              (codigo, vehiculo_id, cliente_id, fecha_inicio, fecha_fin,
               tarifa_dia, dias, monto_total, estado, notas, created_by)
            VALUES
              (@codigo, @vehiculoId, @clienteId, @fechaInicio, @fechaFin,
               @tarifaDia, @dias, @montoTotal, @estado, @notas, @createdBy);
            SELECT LAST_INSERT_ID();
            """;
        cmd.Parameters.AddWithValue("@codigo", alquiler.Codigo);
        cmd.Parameters.AddWithValue("@vehiculoId", alquiler.VehiculoId);
        cmd.Parameters.AddWithValue("@clienteId", alquiler.ClienteId);
        cmd.Parameters.AddWithValue("@fechaInicio", alquiler.FechaInicio.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@fechaFin", alquiler.FechaFin.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@tarifaDia", alquiler.TarifaDia);
        cmd.Parameters.AddWithValue("@dias", alquiler.Dias);
        cmd.Parameters.AddWithValue("@montoTotal", alquiler.MontoTotal);
        cmd.Parameters.AddWithValue("@estado", EnumMap.ADb(alquiler.Estado));
        cmd.Parameters.AddWithValue("@notas", (object?)alquiler.Notas ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@createdBy", SesionActual.HaySesionActiva ? SesionActual.Id : (object)DBNull.Value);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task<Alquiler?> ObtenerPorIdAsync(long id, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT id, codigo, vehiculo_id, cliente_id, fecha_inicio, fecha_fin, fecha_devolucion,
                   tarifa_dia, dias, monto_total, estado, notas, created_at
            FROM {DbNames.Alquiler}
            WHERE id = @id;
            """;
        cmd.Parameters.AddWithValue("@id", id);
        using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Mapear(reader) : null;
    }

    /// <summary>Cierra un alquiler (finalizado/cancelado) dentro de la transacción del Service.</summary>
    public async Task CerrarAsync(long id, EstadoAlquiler estado, DateOnly? fechaDevolucion,
        MySqlConnection conexion, MySqlTransaction transaccion, CancellationToken ct = default)
    {
        using var cmd = conexion.CreateCommand();
        cmd.Transaction = transaccion;
        cmd.CommandText = $"""
            UPDATE {DbNames.Alquiler}
            SET estado = @estado, fecha_devolucion = @fechaDevolucion
            WHERE id = @id;
            """;
        cmd.Parameters.AddWithValue("@estado", EnumMap.ADb(estado));
        cmd.Parameters.AddWithValue("@fechaDevolucion",
            fechaDevolucion is { } f ? f.ToDateTime(TimeOnly.MinValue) : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Lista de alquileres con datos del vehículo y cliente.</summary>
    public async Task<IReadOnlyList<AlquilerResumen>> ObtenerResumenesAsync(CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT a.id, a.codigo,
                   CONCAT(v.marca, ' ', v.modelo, COALESCE(CONCAT(' ', v.anio), '')) AS vehiculo_desc,
                   TRIM(CONCAT(c.nombre, ' ', c.apellido)) AS cliente_nombre,
                   a.fecha_inicio, a.fecha_fin, a.dias, a.monto_total, a.estado
            FROM {DbNames.Alquiler} a
            JOIN {DbNames.Vehiculo} v ON v.id = a.vehiculo_id
            JOIN {DbNames.Cliente} c ON c.id = a.cliente_id
            ORDER BY a.estado = 'activo' DESC, a.fecha_inicio DESC;
            """;

        var lista = new List<AlquilerResumen>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            lista.Add(new AlquilerResumen(
                reader.GetInt64("id"),
                reader.GetString("codigo"),
                reader.GetString("vehiculo_desc"),
                reader.GetString("cliente_nombre"),
                DateOnly.FromDateTime(reader.GetDateTime("fecha_inicio")),
                DateOnly.FromDateTime(reader.GetDateTime("fecha_fin")),
                reader.GetInt32("dias"),
                reader.GetDecimal("monto_total"),
                EnumMap.EstadoAlquilerDeDb(reader.GetString("estado"))));
        }
        return lista;
    }

    private static Alquiler Mapear(MySqlDataReader reader) => new()
    {
        Id = reader.GetInt64("id"),
        Codigo = reader.GetString("codigo"),
        VehiculoId = reader.GetInt64("vehiculo_id"),
        ClienteId = reader.GetInt64("cliente_id"),
        FechaInicio = DateOnly.FromDateTime(reader.GetDateTime("fecha_inicio")),
        FechaFin = DateOnly.FromDateTime(reader.GetDateTime("fecha_fin")),
        FechaDevolucion = reader.IsDBNull(reader.GetOrdinal("fecha_devolucion"))
            ? null : DateOnly.FromDateTime(reader.GetDateTime("fecha_devolucion")),
        TarifaDia = reader.GetDecimal("tarifa_dia"),
        Dias = reader.GetInt32("dias"),
        MontoTotal = reader.GetDecimal("monto_total"),
        Estado = EnumMap.EstadoAlquilerDeDb(reader.GetString("estado")),
        Notas = reader.IsDBNull(reader.GetOrdinal("notas")) ? null : reader.GetString("notas"),
        CreatedAtUtc = DateTime.SpecifyKind(reader.GetDateTime("created_at"), DateTimeKind.Utc)
    };
}
