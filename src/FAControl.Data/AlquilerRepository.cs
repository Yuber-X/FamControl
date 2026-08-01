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

    // ---------- Cobros del alquiler (034) ----------

    /// <summary>
    /// Inserta el cobro DENTRO de la transaccion del Service, junto con la
    /// reserva del numero de recibo. Asi un rollback no quema un numero.
    /// </summary>
    public async Task<long> InsertarPagoAsync(AlquilerPago pago, MySqlConnection conexion,
        MySqlTransaction transaccion, CancellationToken ct = default)
    {
        using var cmd = conexion.CreateCommand();
        cmd.Transaction = transaccion;
        cmd.CommandText = $"""
            INSERT INTO {DbNames.AlquilerPago}
              (alquiler_id, numero_recibo, monto, metodo_pago, notas, created_by)
            VALUES (@alquiler, @recibo, @monto, @metodo, @notas, @usuario);
            SELECT LAST_INSERT_ID();
            """;
        cmd.Parameters.AddWithValue("@alquiler", pago.AlquilerId);
        cmd.Parameters.AddWithValue("@recibo", pago.NumeroRecibo);
        cmd.Parameters.AddWithValue("@monto", pago.Monto);
        cmd.Parameters.AddWithValue("@metodo", EnumMap.ADb(pago.MetodoPago));
        cmd.Parameters.AddWithValue("@notas", (object?)pago.Notas ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@usuario",
            SesionActual.HaySesionActiva ? SesionActual.Id : (object)DBNull.Value);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
    }

    /// <summary>
    /// Total cobrado, LEIDO DENTRO de la transaccion y con FOR UPDATE sobre el
    /// alquiler: sin ese bloqueo, dos cajeros cobrando a la vez podrian pasarse
    /// juntos del monto del contrato, cada uno viendo el saldo de antes.
    /// </summary>
    public async Task<decimal> ObtenerCobradoParaActualizarAsync(long alquilerId,
        MySqlConnection conexion, MySqlTransaction transaccion, CancellationToken ct = default)
    {
        using (var bloqueo = conexion.CreateCommand())
        {
            bloqueo.Transaction = transaccion;
            bloqueo.CommandText = $"SELECT id FROM {DbNames.Alquiler} WHERE id = @id FOR UPDATE;";
            bloqueo.Parameters.AddWithValue("@id", alquilerId);
            await bloqueo.ExecuteScalarAsync(ct);
        }

        using var cmd = conexion.CreateCommand();
        cmd.Transaction = transaccion;
        cmd.CommandText = $"""
            SELECT COALESCE(SUM(monto), 0) FROM {DbNames.AlquilerPago}
            WHERE alquiler_id = @id AND deleted_at IS NULL;
            """;
        cmd.Parameters.AddWithValue("@id", alquilerId);
        return Convert.ToDecimal(await cmd.ExecuteScalarAsync(ct));
    }

    /// <summary>Cobros del alquiler, del mas reciente al mas viejo.</summary>
    public async Task<IReadOnlyList<AlquilerPago>> ObtenerPagosAsync(long alquilerId,
        CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT p.id, p.alquiler_id, p.numero_recibo, p.fecha_pago, p.monto,
                   p.metodo_pago, p.notas,
                   TRIM(CONCAT(u.nombre, ' ', COALESCE(u.apellido, ''))) AS cobrado_por
            FROM {DbNames.AlquilerPago} p
            -- LEFT: el cobro no desaparece si se borro el usuario que lo tomo
            LEFT JOIN {DbNames.Usuario} u ON u.id = p.created_by
            WHERE p.alquiler_id = @id AND p.deleted_at IS NULL
            ORDER BY p.fecha_pago DESC, p.id DESC;
            """;
        cmd.Parameters.AddWithValue("@id", alquilerId);

        var lista = new List<AlquilerPago>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            lista.Add(new AlquilerPago
            {
                Id = reader.GetInt64("id"),
                AlquilerId = reader.GetInt64("alquiler_id"),
                NumeroRecibo = reader.GetString("numero_recibo"),
                FechaPagoUtc = DateTime.SpecifyKind(reader.GetDateTime("fecha_pago"), DateTimeKind.Utc),
                Monto = reader.GetDecimal("monto"),
                MetodoPago = EnumMap.MetodoPagoDeDb(reader.GetString("metodo_pago")),
                Notas = reader.IsDBNull(reader.GetOrdinal("notas")) ? null : reader.GetString("notas"),
                CobradoPor = reader.IsDBNull(reader.GetOrdinal("cobrado_por"))
                    ? null : reader.GetString("cobrado_por")
            });
        }
        return lista;
    }

    // ---------- Renovaciones del alquiler (039) ----------

    /// <summary>
    /// Inserta el tramo nuevo DENTRO de la transaccion del Service: la
    /// renovacion y la actualizacion del contrato son una sola cosa.
    /// </summary>
    public async Task<long> InsertarRenovacionAsync(AlquilerRenovacion renovacion,
        MySqlConnection conexion, MySqlTransaction transaccion, CancellationToken ct = default)
    {
        using var cmd = conexion.CreateCommand();
        cmd.Transaction = transaccion;
        cmd.CommandText = $"""
            INSERT INTO {DbNames.AlquilerRenovacion}
              (alquiler_id, fecha_fin_anterior, fecha_fin_nueva, tarifa_dia, dias, monto,
               notas, created_by)
            VALUES (@alquiler, @anterior, @nueva, @tarifa, @dias, @monto, @notas, @usuario);
            SELECT LAST_INSERT_ID();
            """;
        cmd.Parameters.AddWithValue("@alquiler", renovacion.AlquilerId);
        cmd.Parameters.AddWithValue("@anterior", renovacion.FechaFinAnterior.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@nueva", renovacion.FechaFinNueva.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@tarifa", renovacion.TarifaDia);
        cmd.Parameters.AddWithValue("@dias", renovacion.Dias);
        cmd.Parameters.AddWithValue("@monto", renovacion.Monto);
        cmd.Parameters.AddWithValue("@notas", (object?)renovacion.Notas ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@usuario",
            SesionActual.HaySesionActiva ? SesionActual.Id : (object)DBNull.Value);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
    }

    /// <summary>
    /// Renovaciones del alquiler EN ORDEN CRONOLOGICO. El orden importa: con el
    /// los tramos se reconstruyen uno detras del otro para saber a que tarifa
    /// corresponde cada dia.
    /// </summary>
    public async Task<IReadOnlyList<AlquilerRenovacion>> ObtenerRenovacionesAsync(long alquilerId,
        CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        return await LeerRenovacionesAsync(alquilerId, conexion, null, ct);
    }

    /// <summary>
    /// Los tramos leidos DENTRO de una transaccion: el cierre y la renovacion
    /// los necesitan sin que otra caja pueda agregar uno en el medio.
    /// </summary>
    public async Task<IReadOnlyList<AlquilerRenovacion>> LeerRenovacionesAsync(long alquilerId,
        MySqlConnection conexion, MySqlTransaction? transaccion, CancellationToken ct = default)
    {
        using var cmd = conexion.CreateCommand();
        cmd.Transaction = transaccion;
        cmd.CommandText = $"""
            SELECT r.id, r.alquiler_id, r.fecha_fin_anterior, r.fecha_fin_nueva, r.tarifa_dia,
                   r.dias, r.monto, r.notas, r.created_at, u.nombre AS creado_por
            FROM {DbNames.AlquilerRenovacion} r
            LEFT JOIN {DbNames.Usuario} u ON u.id = r.created_by
            WHERE r.alquiler_id = @id
            ORDER BY r.id;
            """;
        cmd.Parameters.AddWithValue("@id", alquilerId);

        var lista = new List<AlquilerRenovacion>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            lista.Add(new AlquilerRenovacion
            {
                Id = reader.GetInt64("id"),
                AlquilerId = reader.GetInt64("alquiler_id"),
                FechaFinAnterior = DateOnly.FromDateTime(reader.GetDateTime("fecha_fin_anterior")),
                FechaFinNueva = DateOnly.FromDateTime(reader.GetDateTime("fecha_fin_nueva")),
                TarifaDia = reader.GetDecimal("tarifa_dia"),
                Dias = reader.GetInt32("dias"),
                Monto = reader.GetDecimal("monto"),
                Notas = reader.IsDBNull(reader.GetOrdinal("notas")) ? null : reader.GetString("notas"),
                CreatedAtUtc = DateTime.SpecifyKind(reader.GetDateTime("created_at"), DateTimeKind.Utc),
                CreadoPorNombre = reader.IsDBNull(reader.GetOrdinal("creado_por"))
                    ? null : reader.GetString("creado_por")
            });
        }
        return lista;
    }

    /// <summary>
    /// Corre la fecha de devolucion y actualiza el total pactado. NO toca
    /// tarifa_dia: esa queda con la ORIGINAL, la del primer tramo; la vigente
    /// es la de la ultima renovacion.
    /// </summary>
    public async Task<int> RenovarAsync(long alquilerId, DateOnly fechaFinNueva, int diasTotales,
        decimal montoTotal, MySqlConnection conexion, MySqlTransaction transaccion,
        CancellationToken ct = default)
    {
        using var cmd = conexion.CreateCommand();
        cmd.Transaction = transaccion;
        cmd.CommandText = $"""
            UPDATE {DbNames.Alquiler}
            SET fecha_fin = @fin, dias = @dias, monto_total = @total
            WHERE id = @id AND estado = 'activo';
            """;
        cmd.Parameters.AddWithValue("@fin", fechaFinNueva.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@dias", diasTotales);
        cmd.Parameters.AddWithValue("@total", montoTotal);
        cmd.Parameters.AddWithValue("@id", alquilerId);
        return await cmd.ExecuteNonQueryAsync(ct);
    }
}
