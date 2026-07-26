using MySqlConnector;
using FAControl.Common;
using FAControl.Models;

namespace FAControl.Data;

/// <summary>
/// Calendario de plazos de las ventas financiadas del dealer (016).
/// Las escrituras que forman parte de una operación multi-paso (crear la venta
/// con su plan, cobrar un plazo) reciben la conexión/transacción del Service.
/// </summary>
public class VentaPlazoRepository
{
    private readonly ConexionFactory _factory;

    public VentaPlazoRepository(ConexionFactory factory) => _factory = factory;

    /// <summary>Inserta el calendario completo dentro de la transacción de la venta.</summary>
    public async Task InsertarPlazosAsync(long ventaId, IReadOnlyList<VentaPlazo> plazos,
        MySqlConnection conexion, MySqlTransaction transaccion, CancellationToken ct = default)
    {
        foreach (var plazo in plazos)
        {
            using var cmd = conexion.CreateCommand();
            cmd.Transaction = transaccion;
            cmd.CommandText = $"""
                INSERT INTO {DbNames.VentaPlazo}
                  (venta_id, numero, fecha_vencimiento, monto)
                VALUES
                  (@ventaId, @numero, @vencimiento, @monto);
                """;
            cmd.Parameters.AddWithValue("@ventaId", ventaId);
            cmd.Parameters.AddWithValue("@numero", plazo.Numero);
            cmd.Parameters.AddWithValue("@vencimiento", plazo.FechaVencimiento.ToDateTime(TimeOnly.MinValue));
            cmd.Parameters.AddWithValue("@monto", plazo.Monto);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    /// <summary>Plazos de una venta, ordenados por número.</summary>
    public async Task<IReadOnlyList<VentaPlazo>> ObtenerDeVentaAsync(long ventaId,
        CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT id, venta_id, numero, fecha_vencimiento, monto, monto_pagado, estado
            FROM {DbNames.VentaPlazo}
            WHERE venta_id = @ventaId
            ORDER BY numero;
            """;
        cmd.Parameters.AddWithValue("@ventaId", ventaId);

        var lista = new List<VentaPlazo>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            lista.Add(Mapear(reader));
        return lista;
    }

    /// <summary>
    /// Plazo bloqueado FOR UPDATE dentro de la transacción del cobro: sin esto,
    /// dos cobros simultáneos podrían sobrepasar el monto del plazo.
    /// </summary>
    public async Task<VentaPlazo?> ObtenerParaCobroAsync(long plazoId, MySqlConnection conexion,
        MySqlTransaction transaccion, CancellationToken ct = default)
    {
        using var cmd = conexion.CreateCommand();
        cmd.Transaction = transaccion;
        cmd.CommandText = $"""
            SELECT id, venta_id, numero, fecha_vencimiento, monto, monto_pagado, estado
            FROM {DbNames.VentaPlazo}
            WHERE id = @id
            FOR UPDATE;
            """;
        cmd.Parameters.AddWithValue("@id", plazoId);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Mapear(reader) : null;
    }

    /// <summary>Actualiza lo acumulado y el estado del plazo tras un abono.</summary>
    public async Task ActualizarTrasPagoAsync(long plazoId, decimal montoPagado, EstadoPlazo estado,
        MySqlConnection conexion, MySqlTransaction transaccion, CancellationToken ct = default)
    {
        using var cmd = conexion.CreateCommand();
        cmd.Transaction = transaccion;
        cmd.CommandText = $"""
            UPDATE {DbNames.VentaPlazo}
            SET monto_pagado = @montoPagado, estado = @estado, updated_at = UTC_TIMESTAMP()
            WHERE id = @id;
            """;
        cmd.Parameters.AddWithValue("@montoPagado", montoPagado);
        cmd.Parameters.AddWithValue("@estado", EnumMap.ADb(estado));
        cmd.Parameters.AddWithValue("@id", plazoId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Registra el abono con su recibo dentro de la transacción del cobro.</summary>
    public async Task<long> InsertarPagoAsync(VentaPlazoPago pago, MySqlConnection conexion,
        MySqlTransaction transaccion, CancellationToken ct = default)
    {
        using var cmd = conexion.CreateCommand();
        cmd.Transaction = transaccion;
        cmd.CommandText = $"""
            INSERT INTO {DbNames.VentaPlazoPago}
              (plazo_id, numero_recibo, fecha_pago, monto, metodo_pago, notas, created_by)
            VALUES
              (@plazoId, @recibo, @fecha, @monto, @metodoPago, @notas, @createdBy);
            SELECT LAST_INSERT_ID();
            """;
        cmd.Parameters.AddWithValue("@plazoId", pago.PlazoId);
        cmd.Parameters.AddWithValue("@recibo", pago.NumeroRecibo);
        cmd.Parameters.AddWithValue("@fecha", pago.FechaPagoUtc);
        cmd.Parameters.AddWithValue("@monto", pago.Monto);
        cmd.Parameters.AddWithValue("@metodoPago", EnumMap.ADb(pago.MetodoPago));
        cmd.Parameters.AddWithValue("@notas", (object?)pago.Notas ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@createdBy",
            SesionActual.HaySesionActiva ? SesionActual.Id : (object)DBNull.Value);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
    }

    /// <summary>Abonos registrados a los plazos de una venta (historial).</summary>
    public async Task<IReadOnlyList<VentaPlazoPago>> ObtenerPagosDeVentaAsync(long ventaId,
        CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT p.id, p.plazo_id, p.numero_recibo, p.fecha_pago, p.monto, p.metodo_pago, p.notas
            FROM {DbNames.VentaPlazoPago} p
            JOIN {DbNames.VentaPlazo} z ON z.id = p.plazo_id
            WHERE z.venta_id = @ventaId AND p.deleted_at IS NULL
            ORDER BY p.fecha_pago DESC, p.id DESC;
            """;
        cmd.Parameters.AddWithValue("@ventaId", ventaId);

        var lista = new List<VentaPlazoPago>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            lista.Add(new VentaPlazoPago
            {
                Id = reader.GetInt64("id"),
                PlazoId = reader.GetInt64("plazo_id"),
                NumeroRecibo = reader.GetString("numero_recibo"),
                FechaPagoUtc = DateTime.SpecifyKind(reader.GetDateTime("fecha_pago"), DateTimeKind.Utc),
                Monto = reader.GetDecimal("monto"),
                MetodoPago = EnumMap.MetodoPagoDeDb(reader.GetString("metodo_pago")),
                Notas = reader.IsDBNull(reader.GetOrdinal("notas")) ? null : reader.GetString("notas")
            });
        return lista;
    }

    private static VentaPlazo Mapear(MySqlDataReader reader) => new()
    {
        Id = reader.GetInt64("id"),
        VentaId = reader.GetInt64("venta_id"),
        Numero = reader.GetInt32("numero"),
        FechaVencimiento = DateOnly.FromDateTime(reader.GetDateTime("fecha_vencimiento")),
        Monto = reader.GetDecimal("monto"),
        MontoPagado = reader.GetDecimal("monto_pagado"),
        Estado = EnumMap.EstadoPlazoDeDb(reader.GetString("estado"))
    };
}
