using FAControl.Common;
using FAControl.Models;

namespace FAControl.Data;

/// <summary>
/// Consultas del panel principal de DealControl (pedido 2026-07-25).
/// Solo lectura y SOLO tablas del dealer (vehiculo, venta_vehiculo, alquiler):
/// nunca toca préstamos ni cuotas — el aislamiento con PrestControl es total.
/// El mes de negocio es local RD (UTC-4, sin DST).
/// </summary>
public class PanelDealRepository
{
    private const int OffsetRdHoras = 4;

    private readonly ConexionFactory _factory;

    public PanelDealRepository(ConexionFactory factory) => _factory = factory;

    public async Task<ResumenPanelDeal> ObtenerResumenAsync(CancellationToken ct = default)
    {
        // Inicio del mes de negocio (local RD) expresado en UTC
        var hoyLocal = FechaNegocio.Hoy;
        var inicioMesUtc = new DateTime(hoyLocal.Year, hoyLocal.Month, 1).AddHours(OffsetRdHoras);

        using var conexion = await _factory.AbrirAsync(ct);

        int disponibles = 0, alquilados = 0, ventasMes = 0, alquileresActivos = 0;
        decimal capitalInvertido = 0m, montoVentasMes = 0m, gananciaVentasMes = 0m, ingresosAlquilerMes = 0m;

        using (var cmd = conexion.CreateCommand())
        {
            cmd.CommandText = $"""
                SELECT
                  (SELECT COUNT(*) FROM {DbNames.Vehiculo}
                   WHERE deleted_at IS NULL AND estado = 'disponible') AS disponibles,
                  (SELECT COUNT(*) FROM {DbNames.Vehiculo}
                   WHERE deleted_at IS NULL AND estado = 'alquilado') AS alquilados,
                  (SELECT COALESCE(SUM(costo_adquisicion + gastos_importacion), 0) FROM {DbNames.Vehiculo}
                   WHERE deleted_at IS NULL AND estado IN ('disponible', 'alquilado')) AS invertido,
                  (SELECT COUNT(*) FROM {DbNames.VentaVehiculo}
                   WHERE fecha_venta >= @inicioMes) AS ventas_mes,
                  (SELECT COALESCE(SUM(precio), 0) FROM {DbNames.VentaVehiculo}
                   WHERE fecha_venta >= @inicioMes) AS monto_ventas_mes,
                  (SELECT COALESCE(SUM(v.precio - (ve.costo_adquisicion + ve.gastos_importacion)), 0)
                   FROM {DbNames.VentaVehiculo} v
                   JOIN {DbNames.Vehiculo} ve ON ve.id = v.vehiculo_id
                   WHERE v.fecha_venta >= @inicioMes) AS ganancia_ventas_mes,
                  (SELECT COUNT(*) FROM {DbNames.Alquiler}
                   WHERE estado = 'activo') AS alquileres_activos,
                  (SELECT COALESCE(SUM(monto_total), 0) FROM {DbNames.Alquiler}
                   WHERE estado <> 'cancelado' AND created_at >= @inicioMes) AS ingresos_alquiler_mes;
                """;
            cmd.Parameters.AddWithValue("@inicioMes", inicioMesUtc);

            using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                disponibles = Convert.ToInt32(reader["disponibles"]);
                alquilados = Convert.ToInt32(reader["alquilados"]);
                capitalInvertido = reader.GetDecimal("invertido");
                ventasMes = Convert.ToInt32(reader["ventas_mes"]);
                montoVentasMes = reader.GetDecimal("monto_ventas_mes");
                gananciaVentasMes = reader.GetDecimal("ganancia_ventas_mes");
                alquileresActivos = Convert.ToInt32(reader["alquileres_activos"]);
                ingresosAlquilerMes = reader.GetDecimal("ingresos_alquiler_mes");
            }
        }

        var movimientos = new List<MovimientoDeal>();
        using (var cmd = conexion.CreateCommand())
        {
            cmd.CommandText = $"""
                SELECT * FROM (
                  SELECT 'Venta' AS tipo, v.codigo, v.fecha_venta AS fecha,
                         TRIM(CONCAT(c.nombre, ' ', COALESCE(c.apellido, ''))) AS cliente,
                         TRIM(CONCAT(ve.marca, ' ', ve.modelo, ' ', COALESCE(ve.anio, ''))) AS vehiculo,
                         v.precio AS monto
                  FROM {DbNames.VentaVehiculo} v
                  JOIN {DbNames.Cliente} c ON c.id = v.cliente_id
                  JOIN {DbNames.Vehiculo} ve ON ve.id = v.vehiculo_id
                  UNION ALL
                  SELECT 'Alquiler' AS tipo, a.codigo, a.created_at AS fecha,
                         TRIM(CONCAT(c.nombre, ' ', COALESCE(c.apellido, ''))) AS cliente,
                         TRIM(CONCAT(ve.marca, ' ', ve.modelo, ' ', COALESCE(ve.anio, ''))) AS vehiculo,
                         a.monto_total AS monto
                  FROM {DbNames.Alquiler} a
                  JOIN {DbNames.Cliente} c ON c.id = a.cliente_id
                  JOIN {DbNames.Vehiculo} ve ON ve.id = a.vehiculo_id
                  WHERE a.estado <> 'cancelado'
                ) m
                ORDER BY m.fecha DESC
                LIMIT 10;
                """;
            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                movimientos.Add(new MovimientoDeal(
                    reader.GetString("tipo"),
                    reader.GetString("codigo"),
                    DateTime.SpecifyKind(reader.GetDateTime("fecha"), DateTimeKind.Utc),
                    reader.GetString("cliente"),
                    reader.GetString("vehiculo"),
                    reader.GetDecimal("monto")));
            }
        }

        return new ResumenPanelDeal(
            disponibles, alquilados, capitalInvertido,
            ventasMes, montoVentasMes, gananciaVentasMes,
            alquileresActivos, ingresosAlquilerMes,
            movimientos);
    }
}
