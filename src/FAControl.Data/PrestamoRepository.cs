using MySqlConnector;
using FAControl.Common;
using FAControl.Models;

namespace FAControl.Data;

/// <summary>
/// Acceso a prestamo y cuota. Las escrituras exponen variantes transaccionales:
/// crear un préstamo o registrar un pago son operaciones multi-paso que el
/// Service orquesta dentro de UNA MySqlTransaction.
/// </summary>
public class PrestamoRepository
{
    private readonly ConexionFactory _factory;

    public PrestamoRepository(ConexionFactory factory) => _factory = factory;

    // ============================================================
    // Escrituras (siempre dentro de una transacción del Service)
    // ============================================================

    public async Task<long> InsertarAsync(Prestamo prestamo, MySqlConnection conexion,
        MySqlTransaction transaccion, CancellationToken ct = default)
    {
        using var cmd = conexion.CreateCommand();
        cmd.Transaction = transaccion;
        cmd.CommandText = $"""
            INSERT INTO {DbNames.Prestamo}
              (codigo, cliente_id, vehiculo_id, monto_capital, moneda, tasa_interes, plazo_cuotas,
               modalidad, metodo_amortizacion, fecha_inicio, garantia, estado, notas)
            VALUES
              (@codigo, @clienteId, @vehiculoId, @montoCapital, @moneda, @tasaInteres, @plazoCuotas,
               @modalidad, @metodo, @fechaInicio, @garantia, @estado, @notas);
            SELECT LAST_INSERT_ID();
            """;
        cmd.Parameters.AddWithValue("@codigo", prestamo.Codigo);
        cmd.Parameters.AddWithValue("@clienteId", prestamo.ClienteId);
        cmd.Parameters.AddWithValue("@vehiculoId", (object?)prestamo.VehiculoId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@montoCapital", prestamo.MontoCapital);
        cmd.Parameters.AddWithValue("@moneda", prestamo.Moneda);
        cmd.Parameters.AddWithValue("@tasaInteres", prestamo.TasaInteres);
        cmd.Parameters.AddWithValue("@plazoCuotas", prestamo.PlazoCuotas);
        cmd.Parameters.AddWithValue("@modalidad", EnumMap.ADb(prestamo.Modalidad));
        cmd.Parameters.AddWithValue("@metodo", EnumMap.ADb(prestamo.MetodoAmortizacion));
        cmd.Parameters.AddWithValue("@fechaInicio", prestamo.FechaInicio.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@garantia", (object?)prestamo.Garantia ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@estado", EnumMap.ADb(prestamo.Estado));
        cmd.Parameters.AddWithValue("@notas", (object?)prestamo.Notas ?? DBNull.Value);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task InsertarCuotasAsync(long prestamoId, IReadOnlyList<CuotaCalculada> tabla,
        MySqlConnection conexion, MySqlTransaction transaccion, CancellationToken ct = default)
    {
        foreach (var cuota in tabla)
        {
            using var cmd = conexion.CreateCommand();
            cmd.Transaction = transaccion;
            cmd.CommandText = $"""
                INSERT INTO {DbNames.Cuota}
                  (prestamo_id, numero_cuota, fecha_vencimiento, capital, interes, monto_total, saldo_despues)
                VALUES
                  (@prestamoId, @numero, @vencimiento, @capital, @interes, @montoTotal, @saldoDespues);
                """;
            cmd.Parameters.AddWithValue("@prestamoId", prestamoId);
            cmd.Parameters.AddWithValue("@numero", cuota.NumeroCuota);
            cmd.Parameters.AddWithValue("@vencimiento", cuota.FechaVencimiento.ToDateTime(TimeOnly.MinValue));
            cmd.Parameters.AddWithValue("@capital", cuota.Capital);
            cmd.Parameters.AddWithValue("@interes", cuota.Interes);
            cmd.Parameters.AddWithValue("@montoTotal", cuota.MontoTotal);
            cmd.Parameters.AddWithValue("@saldoDespues", cuota.SaldoDespues);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task ActualizarEstadoAsync(long prestamoId, EstadoPrestamo estado,
        MySqlConnection conexion, MySqlTransaction transaccion, CancellationToken ct = default)
    {
        using var cmd = conexion.CreateCommand();
        cmd.Transaction = transaccion;
        cmd.CommandText = $"""
            UPDATE {DbNames.Prestamo}
            SET estado = @estado, updated_at = UTC_TIMESTAMP()
            WHERE id = @id;
            """;
        cmd.Parameters.AddWithValue("@estado", EnumMap.ADb(estado));
        cmd.Parameters.AddWithValue("@id", prestamoId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Cancela un préstamo: las cuotas aún no pagadas quedan 'cancelada'
    /// (NUNCA se borran — regla §8.4 del CLAUDE.md del proyecto).
    /// </summary>
    public async Task CancelarCuotasImpagasAsync(long prestamoId, MySqlConnection conexion,
        MySqlTransaction transaccion, CancellationToken ct = default)
    {
        using var cmd = conexion.CreateCommand();
        cmd.Transaction = transaccion;
        cmd.CommandText = $"""
            UPDATE {DbNames.Cuota}
            SET estado = 'cancelada', updated_at = UTC_TIMESTAMP()
            WHERE prestamo_id = @prestamoId
              AND estado IN ('pendiente', 'vencida', 'en_mora');
            """;
        cmd.Parameters.AddWithValue("@prestamoId", prestamoId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Aplica el resultado de un abono sobre la cuota (acumulado + estado).</summary>
    public async Task ActualizarCuotaTrasPagoAsync(long cuotaId, decimal nuevoMontoPagado,
        EstadoCuota nuevoEstado, MySqlConnection conexion, MySqlTransaction transaccion,
        CancellationToken ct = default)
    {
        using var cmd = conexion.CreateCommand();
        cmd.Transaction = transaccion;
        cmd.CommandText = $"""
            UPDATE {DbNames.Cuota}
            SET monto_pagado = @montoPagado, estado = @estado, updated_at = UTC_TIMESTAMP()
            WHERE id = @id;
            """;
        cmd.Parameters.AddWithValue("@montoPagado", nuevoMontoPagado);
        cmd.Parameters.AddWithValue("@estado", EnumMap.ADb(nuevoEstado));
        cmd.Parameters.AddWithValue("@id", cuotaId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Cuotas cobrables de un préstamo, bloqueadas con FOR UPDATE: nadie más
    /// puede modificarlas hasta que la transacción del pago termine.
    /// </summary>
    public async Task<IReadOnlyList<Cuota>> ObtenerCuotasImpagasParaPagoAsync(long prestamoId,
        MySqlConnection conexion, MySqlTransaction transaccion, CancellationToken ct = default)
    {
        using var cmd = conexion.CreateCommand();
        cmd.Transaction = transaccion;
        cmd.CommandText = $"""
            SELECT id, prestamo_id, numero_cuota, fecha_vencimiento, capital, interes,
                   monto_total, saldo_despues, monto_pagado, estado, created_at, updated_at
            FROM {DbNames.Cuota}
            WHERE prestamo_id = @prestamoId
              AND estado IN ('pendiente', 'vencida', 'en_mora')
            ORDER BY numero_cuota
            FOR UPDATE;
            """;
        cmd.Parameters.AddWithValue("@prestamoId", prestamoId);

        var cuotas = new List<Cuota>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            cuotas.Add(MapearCuota(reader));
        return cuotas;
    }

    // ============================================================
    // Lecturas
    // ============================================================

    /// <summary>
    /// Lista completa para la pantalla Préstamos (una sola consulta con agregados).
    /// <paramref name="soloVehiculares"/>: null = todos; true = solo créditos con
    /// vehículo (AutoControl); false = solo préstamos personales (PrestControl).
    /// </summary>
    public async Task<IReadOnlyList<PrestamoResumen>> ObtenerResumenesAsync(
        bool? soloVehiculares = null, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT p.id, p.codigo, p.cliente_id, CONCAT(c.nombre, ' ', c.apellido) AS cliente_nombre,
                   p.monto_capital, p.tasa_interes, p.plazo_cuotas, p.modalidad,
                   p.metodo_amortizacion, p.fecha_inicio, p.estado,
                   COALESCE(SUM(q.monto_total), 0)  AS total_a_pagar,
                   COALESCE(SUM(q.monto_pagado), 0) AS total_pagado,
                   COALESCE(SUM(q.estado = 'pagada'), 0) AS cuotas_pagadas,
                   MIN(CASE WHEN q.estado IN ('pendiente', 'vencida', 'en_mora')
                            THEN q.fecha_vencimiento END) AS proximo_vencimiento
            FROM {DbNames.Prestamo} p
            JOIN {DbNames.Cliente} c ON c.id = p.cliente_id
            LEFT JOIN {DbNames.Cuota} q ON q.prestamo_id = p.id
            WHERE (@soloVehiculares IS NULL
                   OR (@soloVehiculares = 1 AND p.vehiculo_id IS NOT NULL)
                   OR (@soloVehiculares = 0 AND p.vehiculo_id IS NULL))
            GROUP BY p.id, p.codigo, p.cliente_id, cliente_nombre, p.monto_capital, p.tasa_interes,
                     p.plazo_cuotas, p.modalidad, p.metodo_amortizacion, p.fecha_inicio, p.estado
            ORDER BY p.id DESC;
            """;
        cmd.Parameters.AddWithValue("@soloVehiculares",
            soloVehiculares is null ? DBNull.Value : (soloVehiculares.Value ? 1 : 0));

        var resumenes = new List<PrestamoResumen>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            resumenes.Add(new PrestamoResumen(
                reader.GetInt64("id"),
                reader.GetString("codigo"),
                reader.GetInt64("cliente_id"),
                reader.GetString("cliente_nombre"),
                reader.GetDecimal("monto_capital"),
                reader.GetDecimal("tasa_interes"),
                reader.GetInt32("plazo_cuotas"),
                EnumMap.ModalidadDeDb(reader.GetString("modalidad")),
                EnumMap.MetodoDeDb(reader.GetString("metodo_amortizacion")),
                DateOnly.FromDateTime(reader.GetDateTime("fecha_inicio")),
                EnumMap.EstadoPrestamoDeDb(reader.GetString("estado")),
                reader.GetDecimal("total_a_pagar"),
                reader.GetDecimal("total_pagado"),
                reader.GetInt32("cuotas_pagadas"),
                reader.IsDBNull(reader.GetOrdinal("proximo_vencimiento"))
                    ? null
                    : DateOnly.FromDateTime(reader.GetDateTime("proximo_vencimiento"))));
        }
        return resumenes;
    }

    public async Task<Prestamo?> ObtenerPorIdAsync(long id, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT id, codigo, cliente_id, vehiculo_id, monto_capital, moneda, tasa_interes, plazo_cuotas,
                   modalidad, metodo_amortizacion, fecha_inicio, garantia, estado, notas,
                   created_at, updated_at
            FROM {DbNames.Prestamo}
            WHERE id = @id;
            """;
        cmd.Parameters.AddWithValue("@id", id);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        return new Prestamo
        {
            Id = reader.GetInt64("id"),
            Codigo = reader.GetString("codigo"),
            ClienteId = reader.GetInt64("cliente_id"),
            VehiculoId = reader.IsDBNull(reader.GetOrdinal("vehiculo_id")) ? null : reader.GetInt64("vehiculo_id"),
            MontoCapital = reader.GetDecimal("monto_capital"),
            Moneda = reader.GetString("moneda"),
            TasaInteres = reader.GetDecimal("tasa_interes"),
            PlazoCuotas = reader.GetInt32("plazo_cuotas"),
            Modalidad = EnumMap.ModalidadDeDb(reader.GetString("modalidad")),
            MetodoAmortizacion = EnumMap.MetodoDeDb(reader.GetString("metodo_amortizacion")),
            FechaInicio = DateOnly.FromDateTime(reader.GetDateTime("fecha_inicio")),
            Garantia = reader.IsDBNull(reader.GetOrdinal("garantia")) ? null : reader.GetString("garantia"),
            Estado = EnumMap.EstadoPrestamoDeDb(reader.GetString("estado")),
            Notas = reader.IsDBNull(reader.GetOrdinal("notas")) ? null : reader.GetString("notas"),
            CreatedAtUtc = DateTime.SpecifyKind(reader.GetDateTime("created_at"), DateTimeKind.Utc),
            UpdatedAtUtc = reader.IsDBNull(reader.GetOrdinal("updated_at"))
                ? null
                : DateTime.SpecifyKind(reader.GetDateTime("updated_at"), DateTimeKind.Utc)
        };
    }

    public async Task<IReadOnlyList<Cuota>> ObtenerCuotasAsync(long prestamoId, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT id, prestamo_id, numero_cuota, fecha_vencimiento, capital, interes,
                   monto_total, saldo_despues, monto_pagado, estado, created_at, updated_at
            FROM {DbNames.Cuota}
            WHERE prestamo_id = @prestamoId
            ORDER BY numero_cuota;
            """;
        cmd.Parameters.AddWithValue("@prestamoId", prestamoId);

        var cuotas = new List<Cuota>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            cuotas.Add(MapearCuota(reader));
        return cuotas;
    }

    private static Cuota MapearCuota(MySqlDataReader reader) => new()
    {
        Id = reader.GetInt64("id"),
        PrestamoId = reader.GetInt64("prestamo_id"),
        NumeroCuota = reader.GetInt32("numero_cuota"),
        FechaVencimiento = DateOnly.FromDateTime(reader.GetDateTime("fecha_vencimiento")),
        Capital = reader.GetDecimal("capital"),
        Interes = reader.GetDecimal("interes"),
        MontoTotal = reader.GetDecimal("monto_total"),
        SaldoDespues = reader.GetDecimal("saldo_despues"),
        MontoPagado = reader.GetDecimal("monto_pagado"),
        Estado = EnumMap.EstadoCuotaDeDb(reader.GetString("estado")),
        CreatedAtUtc = DateTime.SpecifyKind(reader.GetDateTime("created_at"), DateTimeKind.Utc),
        UpdatedAtUtc = reader.IsDBNull(reader.GetOrdinal("updated_at"))
            ? null
            : DateTime.SpecifyKind(reader.GetDateTime("updated_at"), DateTimeKind.Utc)
    };
}
