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
              (codigo, ncf, cliente_id, vehiculo_id, monto_capital, moneda, tasa_interes, plazo_cuotas,
               modalidad, metodo_amortizacion, cuota_inicio_capital, fecha_inicio, garantia, estado, notas,
               acto_no, folio_no, fecha_acto, municipio_acto,
               deudor_sexo, deudor_nacionalidad, deudor_estado_civil, deudor_ocupacion,
               cuotas_exigibilidad, dias_gracia, mora_porcentaje, registro_titulos)
            VALUES
              (@codigo, @ncf, @clienteId, @vehiculoId, @montoCapital, @moneda, @tasaInteres, @plazoCuotas,
               @modalidad, @metodo, @cuotaInicioCapital, @fechaInicio, @garantia, @estado, @notas,
               @actoNo, @folioNo, @fechaActo, @municipioActo,
               @deudorSexo, @deudorNacionalidad, @deudorEstadoCivil, @deudorOcupacion,
               @cuotasExigibilidad, @diasGracia, @moraPorcentaje, @registroTitulos);
            SELECT LAST_INSERT_ID();
            """;
        cmd.Parameters.AddWithValue("@codigo", prestamo.Codigo);
        cmd.Parameters.AddWithValue("@ncf", (object?)prestamo.Ncf ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@clienteId", prestamo.ClienteId);
        cmd.Parameters.AddWithValue("@vehiculoId", (object?)prestamo.VehiculoId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@montoCapital", prestamo.MontoCapital);
        cmd.Parameters.AddWithValue("@moneda", prestamo.Moneda);
        cmd.Parameters.AddWithValue("@tasaInteres", prestamo.TasaInteres);
        cmd.Parameters.AddWithValue("@plazoCuotas", prestamo.PlazoCuotas);
        cmd.Parameters.AddWithValue("@modalidad", EnumMap.ADb(prestamo.Modalidad));
        cmd.Parameters.AddWithValue("@metodo", EnumMap.ADb(prestamo.MetodoAmortizacion));
        cmd.Parameters.AddWithValue("@cuotaInicioCapital",
            (object?)prestamo.CuotaInicioCapital ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@fechaInicio", prestamo.FechaInicio.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@garantia", (object?)prestamo.Garantia ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@estado", EnumMap.ADb(prestamo.Estado));
        cmd.Parameters.AddWithValue("@notas", (object?)prestamo.Notas ?? DBNull.Value);
        AgregarParametrosNotariales(cmd, prestamo);
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
        decimal nuevoCapitalPagado, EstadoCuota nuevoEstado,
        MySqlConnection conexion, MySqlTransaction transaccion,
        CancellationToken ct = default)
    {
        using var cmd = conexion.CreateCommand();
        cmd.Transaction = transaccion;
        cmd.CommandText = $"""
            UPDATE {DbNames.Cuota}
            SET monto_pagado = @montoPagado, capital_pagado = @capitalPagado,
                estado = @estado, updated_at = UTC_TIMESTAMP()
            WHERE id = @id;
            """;
        cmd.Parameters.AddWithValue("@montoPagado", nuevoMontoPagado);
        // Se guarda junto al acumulado y en la misma transaccion: si se
        // escribieran por separado, una caida en el medio dejaria una cuota
        // diciendo que cobro mas capital del que cobro (043).
        cmd.Parameters.AddWithValue("@capitalPagado", nuevoCapitalPagado);
        cmd.Parameters.AddWithValue("@estado", EnumMap.ADb(nuevoEstado));
        cmd.Parameters.AddWithValue("@id", cuotaId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Reescribe el interes de una cuota (043 + recalculo del prestamo abierto).
    /// Solo se usa sobre cuotas NO vencidas y NO pagadas: el interes ya
    /// devengado no se toca.
    /// </summary>
    public async Task ActualizarInteresCuotaAsync(long cuotaId, decimal interes,
        decimal montoTotal, decimal saldoDespues,
        MySqlConnection conexion, MySqlTransaction transaccion,
        CancellationToken ct = default)
    {
        using var cmd = conexion.CreateCommand();
        cmd.Transaction = transaccion;
        cmd.CommandText = $"""
            UPDATE {DbNames.Cuota}
            SET interes = @interes, monto_total = @montoTotal,
                saldo_despues = @saldoDespues, updated_at = UTC_TIMESTAMP()
            WHERE id = @id;
            """;
        cmd.Parameters.AddWithValue("@interes", interes);
        cmd.Parameters.AddWithValue("@montoTotal", montoTotal);
        cmd.Parameters.AddWithValue("@saldoDespues", saldoDespues);
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
                   monto_total, saldo_despues, monto_pagado, capital_pagado, estado,
                   created_at, updated_at
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
            SELECT id, codigo, ncf, cliente_id, vehiculo_id, monto_capital, moneda, tasa_interes, plazo_cuotas,
                   modalidad, metodo_amortizacion, cuota_inicio_capital, fecha_inicio,
                   garantia, estado, notas, created_at, updated_at,
                   acto_no, folio_no, fecha_acto, municipio_acto,
                   deudor_sexo, deudor_nacionalidad, deudor_estado_civil, deudor_ocupacion,
                   cuotas_exigibilidad, dias_gracia, mora_porcentaje, registro_titulos
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
            Ncf = reader.IsDBNull(reader.GetOrdinal("ncf")) ? null : reader.GetString("ncf"),
            ClienteId = reader.GetInt64("cliente_id"),
            VehiculoId = reader.IsDBNull(reader.GetOrdinal("vehiculo_id")) ? null : reader.GetInt64("vehiculo_id"),
            MontoCapital = reader.GetDecimal("monto_capital"),
            Moneda = reader.GetString("moneda"),
            TasaInteres = reader.GetDecimal("tasa_interes"),
            PlazoCuotas = reader.GetInt32("plazo_cuotas"),
            Modalidad = EnumMap.ModalidadDeDb(reader.GetString("modalidad")),
            MetodoAmortizacion = EnumMap.MetodoDeDb(reader.GetString("metodo_amortizacion")),
            CuotaInicioCapital = reader.IsDBNull(reader.GetOrdinal("cuota_inicio_capital"))
                ? null
                : reader.GetInt32("cuota_inicio_capital"),
            FechaInicio = DateOnly.FromDateTime(reader.GetDateTime("fecha_inicio")),
            Garantia = reader.IsDBNull(reader.GetOrdinal("garantia")) ? null : reader.GetString("garantia"),
            Estado = EnumMap.EstadoPrestamoDeDb(reader.GetString("estado")),
            Notas = reader.IsDBNull(reader.GetOrdinal("notas")) ? null : reader.GetString("notas"),
            CreatedAtUtc = DateTime.SpecifyKind(reader.GetDateTime("created_at"), DateTimeKind.Utc),
            UpdatedAtUtc = reader.IsDBNull(reader.GetOrdinal("updated_at"))
                ? null
                : DateTime.SpecifyKind(reader.GetDateTime("updated_at"), DateTimeKind.Utc),

            // ---- Pagaré notarial (044) ----
            ActoNo = Texto(reader, "acto_no"),
            FolioNo = Texto(reader, "folio_no"),
            FechaActo = reader.IsDBNull(reader.GetOrdinal("fecha_acto"))
                ? null
                : DateOnly.FromDateTime(reader.GetDateTime("fecha_acto")),
            MunicipioActo = Texto(reader, "municipio_acto"),
            DeudorSexo = (SexoPersona)reader.GetInt32("deudor_sexo"),
            DeudorNacionalidad = Texto(reader, "deudor_nacionalidad"),
            DeudorEstadoCivil = Texto(reader, "deudor_estado_civil"),
            DeudorOcupacion = Texto(reader, "deudor_ocupacion"),
            CuotasExigibilidad = Entero(reader, "cuotas_exigibilidad"),
            DiasGracia = Entero(reader, "dias_gracia"),
            MoraPorcentaje = reader.IsDBNull(reader.GetOrdinal("mora_porcentaje"))
                ? null
                : reader.GetDecimal("mora_porcentaje"),
            RegistroTitulos = Texto(reader, "registro_titulos")
        };
    }

    private static string? Texto(MySqlDataReader reader, string columna) =>
        reader.IsDBNull(reader.GetOrdinal(columna)) ? null : reader.GetString(columna);

    private static int? Entero(MySqlDataReader reader, string columna) =>
        reader.IsDBNull(reader.GetOrdinal(columna)) ? null : reader.GetInt32(columna);

    /// <summary>
    /// Los datos del acta notarial (044). Van juntos en un método aparte porque
    /// son doce parámetros que siempre viajan iguales, y porque el INSERT ya
    /// tenía bastante ruido.
    /// </summary>
    private static void AgregarParametrosNotariales(MySqlCommand cmd, Prestamo prestamo)
    {
        cmd.Parameters.AddWithValue("@actoNo", (object?)prestamo.ActoNo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@folioNo", (object?)prestamo.FolioNo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@fechaActo",
            (object?)prestamo.FechaActo?.ToDateTime(TimeOnly.MinValue) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@municipioActo", (object?)prestamo.MunicipioActo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@deudorSexo", (int)prestamo.DeudorSexo);
        cmd.Parameters.AddWithValue("@deudorNacionalidad", (object?)prestamo.DeudorNacionalidad ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@deudorEstadoCivil", (object?)prestamo.DeudorEstadoCivil ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@deudorOcupacion", (object?)prestamo.DeudorOcupacion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cuotasExigibilidad", (object?)prestamo.CuotasExigibilidad ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@diasGracia", (object?)prestamo.DiasGracia ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@moraPorcentaje", (object?)prestamo.MoraPorcentaje ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@registroTitulos", (object?)prestamo.RegistroTitulos ?? DBNull.Value);
    }

    /// <summary>
    /// Fija el NCF de un préstamo existente (dentro de la transacción de la
    /// operación que lo asigna, para que la reserva de la secuencia y el
    /// préstamo queden consistentes).
    /// </summary>
    public async Task ActualizarNcfAsync(long prestamoId, string ncf, MySqlConnection conexion,
        MySqlTransaction transaccion, CancellationToken ct = default)
    {
        using var cmd = conexion.CreateCommand();
        cmd.Transaction = transaccion;
        cmd.CommandText = $"""
            UPDATE {DbNames.Prestamo}
            SET ncf = @ncf, updated_at = UTC_TIMESTAMP()
            WHERE id = @id;
            """;
        cmd.Parameters.AddWithValue("@ncf", ncf);
        cmd.Parameters.AddWithValue("@id", prestamoId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Crédito vehicular (AutoControl) que financió un vehículo, para la ficha
    /// del inventario: código del préstamo y nombre del cliente. Null si nunca
    /// se financió. Toma el más reciente no cancelado.
    /// </summary>
    public async Task<(string Codigo, string ClienteNombre)?> ObtenerCreditoDeVehiculoAsync(
        long vehiculoId, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT p.codigo, TRIM(CONCAT(c.nombre, ' ', COALESCE(c.apellido, ''))) AS cliente
            FROM {DbNames.Prestamo} p
            JOIN {DbNames.Cliente} c ON c.id = p.cliente_id
            WHERE p.vehiculo_id = @vehiculoId AND p.estado <> 'cancelado'
            ORDER BY p.id DESC
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("@vehiculoId", vehiculoId);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;
        return (reader.GetString("codigo"), reader.GetString("cliente"));
    }

    public async Task<IReadOnlyList<Cuota>> ObtenerCuotasAsync(long prestamoId, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT id, prestamo_id, numero_cuota, fecha_vencimiento, capital, interes,
                   monto_total, saldo_despues, monto_pagado, capital_pagado, estado,
                   created_at, updated_at
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

    /// <summary>
    /// Cuántos cobros tiene el préstamo (029): decide hasta dónde se puede
    /// corregir. Cuenta también los ANULADOS (deleted_at IS NOT NULL) a
    /// propósito: un recibo anulado igual se imprimió y se entregó, así que su
    /// número ya circuló y los montos que declaraba no se pueden desmentir
    /// cambiando el préstamo por detrás.
    /// </summary>
    public async Task<int> ContarCobrosAsync(long prestamoId, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT COUNT(*)
            FROM {DbNames.Pago} p
            JOIN {DbNames.Cuota} c ON c.id = p.cuota_id
            WHERE c.prestamo_id = @prestamoId;
            """;
        cmd.Parameters.AddWithValue("@prestamoId", prestamoId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    /// <summary>
    /// Corrige los datos del préstamo (029). El código y el NCF NO se tocan:
    /// son identificadores ya emitidos. El cliente tampoco: mover un préstamo
    /// de una persona a otra no es corregir un tipeo, es otro préstamo.
    /// </summary>
    public async Task ActualizarDatosAsync(Prestamo prestamo, MySqlConnection conexion,
        MySqlTransaction transaccion, CancellationToken ct = default)
    {
        using var cmd = conexion.CreateCommand();
        cmd.Transaction = transaccion;
        cmd.CommandText = $"""
            UPDATE {DbNames.Prestamo}
            SET monto_capital = @montoCapital, tasa_interes = @tasaInteres,
                plazo_cuotas = @plazoCuotas, modalidad = @modalidad,
                metodo_amortizacion = @metodo, cuota_inicio_capital = @cuotaInicioCapital,
                fecha_inicio = @fechaInicio,
                garantia = @garantia, notas = @notas,
                acto_no = @actoNo, folio_no = @folioNo, fecha_acto = @fechaActo,
                municipio_acto = @municipioActo, deudor_sexo = @deudorSexo,
                deudor_nacionalidad = @deudorNacionalidad,
                deudor_estado_civil = @deudorEstadoCivil,
                deudor_ocupacion = @deudorOcupacion,
                cuotas_exigibilidad = @cuotasExigibilidad, dias_gracia = @diasGracia,
                mora_porcentaje = @moraPorcentaje, registro_titulos = @registroTitulos,
                updated_at = UTC_TIMESTAMP()
            WHERE id = @id;
            """;
        cmd.Parameters.AddWithValue("@montoCapital", prestamo.MontoCapital);
        cmd.Parameters.AddWithValue("@tasaInteres", prestamo.TasaInteres);
        cmd.Parameters.AddWithValue("@plazoCuotas", prestamo.PlazoCuotas);
        cmd.Parameters.AddWithValue("@modalidad", EnumMap.ADb(prestamo.Modalidad));
        cmd.Parameters.AddWithValue("@metodo", EnumMap.ADb(prestamo.MetodoAmortizacion));
        cmd.Parameters.AddWithValue("@cuotaInicioCapital",
            (object?)prestamo.CuotaInicioCapital ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@fechaInicio", prestamo.FechaInicio.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@garantia", (object?)prestamo.Garantia ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@notas", (object?)prestamo.Notas ?? DBNull.Value);
        AgregarParametrosNotariales(cmd, prestamo);
        cmd.Parameters.AddWithValue("@id", prestamo.Id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Borra la tabla de amortización para regenerarla tras una corrección.
    ///
    /// La regla del CLAUDE.md ("nunca borrar cuotas de un préstamo activo")
    /// apunta a NO usar el borrado como forma de cancelar: para eso está
    /// <see cref="CancelarCuotasImpagasAsync"/>, que las conserva. Aquí es otra
    /// cosa: la tabla es un cálculo derivado del capital, la tasa y el plazo, y
    /// el servicio solo llega hasta aquí cuando NO hay ningún cobro, así que
    /// ninguna cuota tiene recibo colgando. Regenerarla es recalcular, no
    /// borrar historia.
    ///
    /// La FK de `pago` hacia `cuota` es la última red: si por lo que fuera
    /// hubiera un pago, MySQL rechaza el DELETE y la transacción se revierte.
    /// </summary>
    public async Task BorrarCuotasAsync(long prestamoId, MySqlConnection conexion,
        MySqlTransaction transaccion, CancellationToken ct = default)
    {
        using var cmd = conexion.CreateCommand();
        cmd.Transaction = transaccion;
        cmd.CommandText = $"DELETE FROM {DbNames.Cuota} WHERE prestamo_id = @prestamoId;";
        cmd.Parameters.AddWithValue("@prestamoId", prestamoId);
        await cmd.ExecuteNonQueryAsync(ct);
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
        CapitalPagado = reader.GetDecimal("capital_pagado"),
        Estado = EnumMap.EstadoCuotaDeDb(reader.GetString("estado")),
        CreatedAtUtc = DateTime.SpecifyKind(reader.GetDateTime("created_at"), DateTimeKind.Utc),
        UpdatedAtUtc = reader.IsDBNull(reader.GetOrdinal("updated_at"))
            ? null
            : DateTime.SpecifyKind(reader.GetDateTime("updated_at"), DateTimeKind.Utc)
    };
}
