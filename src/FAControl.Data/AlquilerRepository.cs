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
            SELECT a.id, a.codigo, a.vehiculo_id, a.cliente_id, a.fecha_inicio, a.fecha_fin,
                   a.fecha_devolucion, a.tarifa_dia, a.dias, a.dias_reales, a.monto_total,
                   a.monto_final, a.estado, a.cerrado_motivo, a.cerrado_at, a.notas, a.created_at,
                   TRIM(CONCAT(u.nombre, ' ', COALESCE(u.apellido, ''))) AS cerrado_por_nombre
            FROM {DbNames.Alquiler} a
            -- LEFT: el alquiler no desaparece si se borro el usuario que lo cerro
            LEFT JOIN {DbNames.Usuario} u ON u.id = a.cerrado_por
            WHERE a.id = @id;
            """;
        cmd.Parameters.AddWithValue("@id", id);
        using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Mapear(reader) : null;
    }

    /// <summary>
    /// Cierra un alquiler (finalizado/cancelado) dentro de la transaccion del
    /// Service, guardando el motivo, quien lo cerro y los dias/monto REALES (031).
    ///
    /// El WHERE exige que siga activo: si otro usuario lo cerro entre que se
    /// leyo y se escribio, esto afecta 0 filas y el Service lo convierte en
    /// error, en vez de pisar un cierre ajeno.
    /// </summary>
    public async Task<int> CerrarAsync(long id, EstadoAlquiler estado, DateOnly? fechaDevolucion,
        string motivo, int? diasReales, decimal? montoFinal,
        MySqlConnection conexion, MySqlTransaction transaccion, CancellationToken ct = default)
    {
        using var cmd = conexion.CreateCommand();
        cmd.Transaction = transaccion;
        cmd.CommandText = $"""
            UPDATE {DbNames.Alquiler}
            SET estado = @estado, fecha_devolucion = @fechaDevolucion,
                cerrado_motivo = @motivo, cerrado_at = UTC_TIMESTAMP(), cerrado_por = @cerradoPor,
                dias_reales = @diasReales, monto_final = @montoFinal
            WHERE id = @id AND estado = 'activo';
            """;
        cmd.Parameters.AddWithValue("@estado", EnumMap.ADb(estado));
        cmd.Parameters.AddWithValue("@fechaDevolucion",
            fechaDevolucion is { } f ? f.ToDateTime(TimeOnly.MinValue) : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@motivo", motivo);
        cmd.Parameters.AddWithValue("@cerradoPor",
            SesionActual.HaySesionActiva ? SesionActual.Id : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@diasReales", (object?)diasReales ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@montoFinal", (object?)montoFinal ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", id);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Corrige los datos de un alquiler (031). El codigo NO se toca: ya se
    /// emitio. El vehiculo y el cliente tampoco: cambiarlos no es corregir un
    /// tipeo, es otro alquiler.
    /// </summary>
    public async Task ActualizarDatosAsync(Alquiler alquiler, MySqlConnection conexion,
        MySqlTransaction transaccion, CancellationToken ct = default)
    {
        using var cmd = conexion.CreateCommand();
        cmd.Transaction = transaccion;
        cmd.CommandText = $"""
            UPDATE {DbNames.Alquiler}
            SET fecha_inicio = @inicio, fecha_fin = @fin, tarifa_dia = @tarifa,
                dias = @dias, monto_total = @total, notas = @notas
            WHERE id = @id;
            """;
        cmd.Parameters.AddWithValue("@inicio", alquiler.FechaInicio.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@fin", alquiler.FechaFin.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@tarifa", alquiler.TarifaDia);
        cmd.Parameters.AddWithValue("@dias", alquiler.Dias);
        cmd.Parameters.AddWithValue("@total", alquiler.MontoTotal);
        cmd.Parameters.AddWithValue("@notas", (object?)alquiler.Notas ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", alquiler.Id);
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
                   a.fecha_inicio, a.fecha_fin, a.dias, a.monto_total, a.estado,
                   COALESCE(TRIM(CONCAT(u.nombre, ' ', COALESCE(u.apellido, ''))), '—') AS registro
            FROM {DbNames.Alquiler} a
            JOIN {DbNames.Vehiculo} v ON v.id = a.vehiculo_id
            JOIN {DbNames.Cliente} c ON c.id = a.cliente_id
            -- LEFT: el alquiler no desaparece del listado si se borró el usuario
            LEFT JOIN {DbNames.Usuario} u ON u.id = a.created_by
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
                EnumMap.EstadoAlquilerDeDb(reader.GetString("estado")),
                reader.GetString("registro")));
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
        DiasReales = reader.IsDBNull(reader.GetOrdinal("dias_reales")) ? null : reader.GetInt32("dias_reales"),
        MontoTotal = reader.GetDecimal("monto_total"),
        MontoFinal = reader.IsDBNull(reader.GetOrdinal("monto_final")) ? null : reader.GetDecimal("monto_final"),
        Estado = EnumMap.EstadoAlquilerDeDb(reader.GetString("estado")),
        CerradoMotivo = reader.IsDBNull(reader.GetOrdinal("cerrado_motivo")) ? null : reader.GetString("cerrado_motivo"),
        CerradoAtUtc = reader.IsDBNull(reader.GetOrdinal("cerrado_at"))
            ? null : DateTime.SpecifyKind(reader.GetDateTime("cerrado_at"), DateTimeKind.Utc),
        CerradoPorNombre = reader.IsDBNull(reader.GetOrdinal("cerrado_por_nombre"))
            ? null : reader.GetString("cerrado_por_nombre"),
        Notas = reader.IsDBNull(reader.GetOrdinal("notas")) ? null : reader.GetString("notas"),
        CreatedAtUtc = DateTime.SpecifyKind(reader.GetDateTime("created_at"), DateTimeKind.Utc)
    };
}
