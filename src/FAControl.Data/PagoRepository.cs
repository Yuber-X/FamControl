using MySqlConnector;
using FAControl.Common;
using FAControl.Models;

namespace FAControl.Data;

/// <summary>
/// Acceso a la tabla pago. Un pago registrado NUNCA se modifica ni se borra:
/// los errores se corrigen con un pago compensatorio negativo (regla contable).
/// Por eso aquí solo hay INSERT (transaccional) y SELECT.
/// </summary>
public class PagoRepository
{
    private readonly ConexionFactory _factory;

    public PagoRepository(ConexionFactory factory) => _factory = factory;

    /// <summary>Inserta un abono dentro de la transacción del cobro.</summary>
    public async Task<long> InsertarAsync(Pago pago, MySqlConnection conexion,
        MySqlTransaction transaccion, CancellationToken ct = default)
    {
        using var cmd = conexion.CreateCommand();
        cmd.Transaction = transaccion;
        cmd.CommandText = $"""
            INSERT INTO {DbNames.Pago}
              (cuota_id, numero_recibo, fecha_pago, monto_pagado, monto_interes, monto_capital, metodo_pago, notas, created_by)
            VALUES
              (@cuotaId, @numeroRecibo, @fechaPago, @montoPagado, @montoInteres, @montoCapital, @metodoPago, @notas, @createdBy);
            SELECT LAST_INSERT_ID();
            """;
        cmd.Parameters.AddWithValue("@cuotaId", pago.CuotaId);
        cmd.Parameters.AddWithValue("@numeroRecibo", pago.NumeroRecibo);
        cmd.Parameters.AddWithValue("@fechaPago", pago.FechaPagoUtc);
        cmd.Parameters.AddWithValue("@montoPagado", pago.MontoPagado);
        cmd.Parameters.AddWithValue("@montoInteres", pago.MontoInteres);
        cmd.Parameters.AddWithValue("@montoCapital", pago.MontoCapital);
        cmd.Parameters.AddWithValue("@metodoPago", EnumMap.ADb(pago.MetodoPago));
        cmd.Parameters.AddWithValue("@notas", (object?)pago.Notas ?? DBNull.Value);
        // Quién cobró: la sesión activa. NULL si (por alguna razón) no hay sesión.
        cmd.Parameters.AddWithValue("@createdBy",
            SesionActual.HaySesionActiva ? SesionActual.Id : DBNull.Value);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
    }

    /// <summary>Historial de pagos recientes para la pantalla de Cobros.</summary>
    public async Task<IReadOnlyList<PagoResumen>> ObtenerRecientesAsync(int limite = 20, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT g.id, g.numero_recibo, g.fecha_pago, g.monto_pagado, g.metodo_pago,
                   q.numero_cuota, p.codigo AS prestamo_codigo,
                   CONCAT(c.nombre, ' ', c.apellido) AS cliente_nombre
            FROM {DbNames.Pago} g
            JOIN {DbNames.Cuota} q ON q.id = g.cuota_id
            JOIN {DbNames.Prestamo} p ON p.id = q.prestamo_id
            JOIN {DbNames.Cliente} c ON c.id = p.cliente_id
            WHERE g.deleted_at IS NULL
            ORDER BY g.id DESC
            LIMIT @limite;
            """;
        cmd.Parameters.AddWithValue("@limite", limite);

        var pagos = new List<PagoResumen>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            pagos.Add(new PagoResumen(
                reader.GetInt64("id"),
                reader.GetString("numero_recibo"),
                DateTime.SpecifyKind(reader.GetDateTime("fecha_pago"), DateTimeKind.Utc),
                reader.GetString("cliente_nombre"),
                reader.GetString("prestamo_codigo"),
                reader.GetInt32("numero_cuota"),
                reader.GetDecimal("monto_pagado"),
                EnumMap.MetodoPagoDeDb(reader.GetString("metodo_pago"))));
        }
        return pagos;
    }
}
