using MySqlConnector;
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

        // ---------- Gráficos (pedido 2026-07-27) ----------
        // Lo primero que quiere ver un dealer al abrir: cómo viene el mes contra
        // los anteriores, y cuánto del inventario está parado.
        var meses = await ObtenerUltimosMesesAsync(conexion, hoyLocal, 6, ct);
        var inventario = await ObtenerInventarioAsync(conexion, ct);

        return new ResumenPanelDeal(
            disponibles, alquilados, capitalInvertido,
            ventasMes, montoVentasMes, gananciaVentasMes,
            alquileresActivos, ingresosAlquilerMes,
            movimientos, meses, inventario);
    }

    /// <summary>
    /// Ventas y alquileres de los últimos N meses, del más viejo al más nuevo.
    /// Los meses SIN movimiento igual aparecen (en cero): si no, el gráfico
    /// miente sobre la continuidad del negocio.
    /// </summary>
    private static async Task<IReadOnlyList<MesDeal>> ObtenerUltimosMesesAsync(
        MySqlConnection conexion, DateOnly hoyLocal, int cantidadMeses,
        CancellationToken ct)
    {
        var primerMes = new DateTime(hoyLocal.Year, hoyLocal.Month, 1).AddMonths(-(cantidadMeses - 1));
        var desdeUtc = primerMes.AddHours(OffsetRdHoras);

        var ventas = new Dictionary<(int, int), decimal>();
        var alquileres = new Dictionary<(int, int), decimal>();

        using (var cmd = conexion.CreateCommand())
        {
            cmd.CommandText = $"""
                SELECT origen, YEAR(fecha) AS anio, MONTH(fecha) AS mes, SUM(monto) AS monto
                FROM (
                  SELECT 'venta' AS origen, fecha_venta AS fecha, precio AS monto
                  FROM {DbNames.VentaVehiculo} WHERE fecha_venta >= @desde
                  UNION ALL
                  SELECT 'alquiler' AS origen, created_at AS fecha, monto_total AS monto
                  FROM {DbNames.Alquiler} WHERE estado <> 'cancelado' AND created_at >= @desde
                ) m
                GROUP BY origen, anio, mes;
                """;
            cmd.Parameters.AddWithValue("@desde", desdeUtc);

            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var clave = (Convert.ToInt32(reader["anio"]), Convert.ToInt32(reader["mes"]));
                var monto = reader.GetDecimal("monto");
                if (reader.GetString("origen") == "venta")
                    ventas[clave] = monto;
                else
                    alquileres[clave] = monto;
            }
        }

        var lista = new List<MesDeal>(cantidadMeses);
        for (var i = 0; i < cantidadMeses; i++)
        {
            var mes = primerMes.AddMonths(i);
            var clave = (mes.Year, mes.Month);
            lista.Add(new MesDeal(mes.Year, mes.Month,
                ventas.GetValueOrDefault(clave, 0m),
                alquileres.GetValueOrDefault(clave, 0m)));
        }
        return lista;
    }

    /// <summary>Composición del inventario vivo por estado (los vendidos no cuentan: ya no son inventario).</summary>
    private static async Task<IReadOnlyList<ConteoInventario>> ObtenerInventarioAsync(
        MySqlConnection conexion, CancellationToken ct)
    {
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT estado, COUNT(*) AS cantidad
            FROM {DbNames.Vehiculo}
            WHERE deleted_at IS NULL AND estado <> 'vendido'
            GROUP BY estado
            ORDER BY cantidad DESC;
            """;

        var lista = new List<ConteoInventario>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var estado = reader.GetString("estado");
            lista.Add(new ConteoInventario(
                estado switch
                {
                    "disponible" => "Disponibles",
                    "alquilado" => "Alquilados",
                    "reservado" => "Reservados",
                    "baja" => "Dados de baja",
                    _ => estado
                },
                Convert.ToInt32(reader["cantidad"])));
        }
        return lista;
    }
}
