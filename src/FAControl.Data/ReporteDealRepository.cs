using FAControl.Common;
using FAControl.Models;

namespace FAControl.Data;

/// <summary>
/// Consultas del expediente de contratos y del reporte propio de DealControl
/// (pedido 2026-07-25). Solo lectura y SOLO tablas del dealer: nunca toca
/// prestamo/cuota/pago — el aislamiento con PrestControl es total.
/// El día de negocio se obtiene restando 4 horas al UTC (RD, sin DST).
/// </summary>
public class ReporteDealRepository
{
    private const int OffsetRdHoras = 4;

    private readonly ConexionFactory _factory;

    public ReporteDealRepository(ConexionFactory factory) => _factory = factory;

    /// <summary>
    /// Expediente de contratos: una fila por venta con su cliente, vendedor,
    /// matrícula y estado de los plazos.
    /// </summary>
    public async Task<IReadOnlyList<ContratoDealFila>> ObtenerContratosAsync(DateOnly hoy,
        CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT vv.id, vv.codigo, vv.fecha_venta, vv.precio, vv.tipo_venta, vv.inicial,
                   TRIM(CONCAT(c.nombre, ' ', COALESCE(c.apellido, ''))) AS cliente,
                   COALESCE(u.nombre, '—') AS vendedor,
                   TRIM(CONCAT(v.marca, ' ', v.modelo, COALESCE(CONCAT(' ', v.anio), ''))) AS vehiculo,
                   v.matricula, v.placa,
                   (SELECT COUNT(*) FROM {DbNames.VentaPlazo} z
                    WHERE z.venta_id = vv.id AND z.estado <> 'cancelado') AS plazos_totales,
                   (SELECT COUNT(*) FROM {DbNames.VentaPlazo} z
                    WHERE z.venta_id = vv.id AND z.estado = 'pagado') AS plazos_pagados,
                   (SELECT COUNT(*) FROM {DbNames.VentaPlazo} z
                    WHERE z.venta_id = vv.id AND z.estado = 'pendiente'
                      AND z.fecha_vencimiento < @hoy
                      AND z.monto_pagado < z.monto) AS plazos_atrasados,
                   (SELECT COALESCE(SUM(z.monto - z.monto_pagado), 0) FROM {DbNames.VentaPlazo} z
                    WHERE z.venta_id = vv.id AND z.estado <> 'cancelado') AS pendiente
            FROM {DbNames.VentaVehiculo} vv
            JOIN {DbNames.Cliente} c ON c.id = vv.cliente_id
            JOIN {DbNames.Vehiculo} v ON v.id = vv.vehiculo_id
            LEFT JOIN {DbNames.Usuario} u ON u.id = vv.created_by
            ORDER BY vv.fecha_venta DESC;
            """;
        cmd.Parameters.AddWithValue("@hoy", hoy.ToDateTime(TimeOnly.MinValue));

        var lista = new List<ContratoDealFila>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var tipo = EnumMap.TipoVentaDeDb(reader.GetString("tipo_venta"));
            // Una separación no tiene plazos: lo pendiente es precio − adelanto
            var pendiente = tipo == TipoVenta.Separacion
                ? reader.GetDecimal("precio") - reader.GetDecimal("inicial")
                : reader.GetDecimal("pendiente");

            lista.Add(new ContratoDealFila(
                reader.GetInt64("id"),
                reader.GetString("codigo"),
                DateTime.SpecifyKind(reader.GetDateTime("fecha_venta"), DateTimeKind.Utc),
                reader.GetString("cliente"),
                reader.GetString("vendedor"),
                reader.GetString("vehiculo"),
                reader.IsDBNull(reader.GetOrdinal("matricula")) ? null : reader.GetString("matricula"),
                reader.IsDBNull(reader.GetOrdinal("placa")) ? null : reader.GetString("placa"),
                reader.GetDecimal("precio"),
                tipo,
                Convert.ToInt32(reader["plazos_totales"]),
                Convert.ToInt32(reader["plazos_pagados"]),
                Convert.ToInt32(reader["plazos_atrasados"]),
                pendiente));
        }
        return lista;
    }

    /// <summary>
    /// Reporte del dealer en un rango. <paramref name="porcentajeComision"/> lo
    /// define el negocio en Configuración (la app no inventa la tasa).
    /// </summary>
    public async Task<ReporteDeal> ObtenerReporteAsync(DateOnly desde, DateOnly hasta,
        decimal porcentajeComision, CancellationToken ct = default)
    {
        // Rango local [desde, hasta] → instantes UTC [inicio, fin)
        var inicioUtc = desde.ToDateTime(TimeOnly.MinValue).AddHours(OffsetRdHoras);
        var finUtc = hasta.AddDays(1).ToDateTime(TimeOnly.MinValue).AddHours(OffsetRdHoras);

        using var conexion = await _factory.AbrirAsync(ct);

        int ventas = 0, alquileres = 0, disponibles = 0;
        decimal montoVendido = 0m, ganancia = 0m, ingresosAlquiler = 0m,
                capitalInvertido = 0m, pendienteDeCobro = 0m;

        using (var cmd = conexion.CreateCommand())
        {
            cmd.CommandText = $"""
                SELECT
                  (SELECT COUNT(*) FROM {DbNames.VentaVehiculo}
                   WHERE fecha_venta >= @inicio AND fecha_venta < @fin) AS ventas,
                  (SELECT COALESCE(SUM(precio), 0) FROM {DbNames.VentaVehiculo}
                   WHERE fecha_venta >= @inicio AND fecha_venta < @fin) AS monto_vendido,
                  (SELECT COALESCE(SUM(vv.precio - (v.costo_adquisicion + v.gastos_importacion)), 0)
                   FROM {DbNames.VentaVehiculo} vv
                   JOIN {DbNames.Vehiculo} v ON v.id = vv.vehiculo_id
                   WHERE vv.fecha_venta >= @inicio AND vv.fecha_venta < @fin) AS ganancia,
                  (SELECT COUNT(*) FROM {DbNames.Alquiler}
                   WHERE estado <> 'cancelado' AND created_at >= @inicio AND created_at < @fin) AS alquileres,
                  (SELECT COALESCE(SUM(monto_total), 0) FROM {DbNames.Alquiler}
                   WHERE estado <> 'cancelado' AND created_at >= @inicio AND created_at < @fin) AS ingresos_alquiler,
                  (SELECT COUNT(*) FROM {DbNames.Vehiculo}
                   WHERE deleted_at IS NULL AND estado = 'disponible') AS disponibles,
                  (SELECT COALESCE(SUM(costo_adquisicion + gastos_importacion), 0) FROM {DbNames.Vehiculo}
                   WHERE deleted_at IS NULL AND estado IN ('disponible','reservado','alquilado')) AS capital_invertido,
                  (SELECT COALESCE(SUM(z.monto - z.monto_pagado), 0) FROM {DbNames.VentaPlazo} z
                   WHERE z.estado = 'pendiente') AS pendiente_cobro;
                """;
            cmd.Parameters.AddWithValue("@inicio", inicioUtc);
            cmd.Parameters.AddWithValue("@fin", finUtc);

            using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                ventas = Convert.ToInt32(reader["ventas"]);
                montoVendido = reader.GetDecimal("monto_vendido");
                ganancia = reader.GetDecimal("ganancia");
                alquileres = Convert.ToInt32(reader["alquileres"]);
                ingresosAlquiler = reader.GetDecimal("ingresos_alquiler");
                disponibles = Convert.ToInt32(reader["disponibles"]);
                capitalInvertido = reader.GetDecimal("capital_invertido");
                pendienteDeCobro = reader.GetDecimal("pendiente_cobro");
            }
        }

        var porVendedor = new List<ComisionVendedor>();
        using (var cmd = conexion.CreateCommand())
        {
            cmd.CommandText = $"""
                SELECT COALESCE(u.nombre, 'Sin vendedor') AS vendedor,
                       COUNT(*) AS cantidad,
                       COALESCE(SUM(vv.precio), 0) AS monto
                FROM {DbNames.VentaVehiculo} vv
                LEFT JOIN {DbNames.Usuario} u ON u.id = vv.created_by
                WHERE vv.fecha_venta >= @inicio AND vv.fecha_venta < @fin
                GROUP BY vendedor
                ORDER BY monto DESC;
                """;
            cmd.Parameters.AddWithValue("@inicio", inicioUtc);
            cmd.Parameters.AddWithValue("@fin", finUtc);

            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var monto = reader.GetDecimal("monto");
                porVendedor.Add(new ComisionVendedor(
                    reader.GetString("vendedor"),
                    Convert.ToInt32(reader["cantidad"]),
                    monto,
                    // La comisión se redondea al final del cálculo, no antes
                    Math.Round(monto * porcentajeComision / 100m, 2, MidpointRounding.AwayFromZero)));
            }
        }

        return new ReporteDeal(
            desde, hasta,
            ventas, montoVendido, ganancia,
            alquileres, ingresosAlquiler,
            disponibles, capitalInvertido,
            pendienteDeCobro,
            porVendedor);
    }
}
