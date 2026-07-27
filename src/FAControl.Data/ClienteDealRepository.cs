using FAControl.Common;
using FAControl.Models;

namespace FAControl.Data;

/// <summary>
/// Ficha del cliente DEL DEALER (pedido 2026-07-27). Solo lectura y SOLO
/// tablas del dealer (venta_vehiculo, venta_plazo, alquiler, vehiculo): no
/// toca préstamos ni cuotas, igual que el resto de DealControl.
/// </summary>
public class ClienteDealRepository
{
    private readonly ConexionFactory _factory;

    public ClienteDealRepository(ConexionFactory factory) => _factory = factory;

    /// <summary>
    /// Métricas del cliente. Qué cuenta como COBRADO:
    ///  * venta al contado → el precio completo;
    ///  * venta a plazos   → la inicial + los abonos a plazos (recibos vivos);
    ///  * separación       → solo la inicial (todavía no compró);
    ///  * alquiler         → el monto total cuando ya se devolvió (finalizado).
    /// </summary>
    public async Task<MetricasClienteDeal> ObtenerMetricasAsync(long clienteId, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT
              (SELECT COALESCE(SUM(precio), 0) FROM {DbNames.VentaVehiculo}
               WHERE cliente_id = @id) AS total_ventas,
              (SELECT COALESCE(SUM(CASE WHEN tipo_venta = 'contado' THEN precio ELSE inicial END), 0)
               FROM {DbNames.VentaVehiculo} WHERE cliente_id = @id) AS cobrado_ventas,
              (SELECT COALESCE(SUM(pp.monto), 0)
               FROM {DbNames.VentaPlazoPago} pp
               JOIN {DbNames.VentaPlazo} pl ON pl.id = pp.plazo_id
               JOIN {DbNames.VentaVehiculo} v ON v.id = pl.venta_id
               WHERE v.cliente_id = @id AND pp.deleted_at IS NULL) AS cobrado_plazos,
              (SELECT COALESCE(SUM(monto_total), 0) FROM {DbNames.Alquiler}
               WHERE cliente_id = @id AND estado <> 'cancelado') AS total_alquiler,
              (SELECT COALESCE(SUM(monto_total), 0) FROM {DbNames.Alquiler}
               WHERE cliente_id = @id AND estado = 'finalizado') AS cobrado_alquiler,
              (SELECT COUNT(*) FROM {DbNames.VentaVehiculo}
               WHERE cliente_id = @id) AS comprados,
              (SELECT COUNT(*) FROM {DbNames.Alquiler}
               WHERE cliente_id = @id AND estado <> 'cancelado') AS alquilados,
              (SELECT COUNT(*) FROM {DbNames.VentaPlazo} pl
               JOIN {DbNames.VentaVehiculo} v ON v.id = pl.venta_id
               WHERE v.cliente_id = @id AND pl.estado = 'pendiente'
                 AND pl.fecha_vencimiento < @hoy) AS plazos_atrasados;
            """;
        cmd.Parameters.AddWithValue("@id", clienteId);
        cmd.Parameters.AddWithValue("@hoy", FechaNegocio.Hoy.ToDateTime(TimeOnly.MinValue));

        using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return new MetricasClienteDeal(0, 0, 0, 0, 0, 0);

        var transferido = reader.GetDecimal("total_ventas") + reader.GetDecimal("total_alquiler");
        var cobrado = reader.GetDecimal("cobrado_ventas") + reader.GetDecimal("cobrado_plazos")
                    + reader.GetDecimal("cobrado_alquiler");

        return new MetricasClienteDeal(
            transferido,
            cobrado,
            Math.Max(0m, transferido - cobrado),
            Convert.ToInt32(reader["comprados"]),
            Convert.ToInt32(reader["alquilados"]),
            Convert.ToInt32(reader["plazos_atrasados"]));
    }

    /// <summary>
    /// Vehículos que el cliente compró y/o alquiló, del más reciente al más
    /// viejo. Lleva el vehiculo_id para que el botón "Ver ficha" abra la ficha
    /// completa del vehículo desde la ficha del cliente.
    /// </summary>
    public async Task<IReadOnlyList<VehiculoDeCliente>> ObtenerVehiculosAsync(long clienteId,
        CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT * FROM (
              SELECT 'Compra' AS tipo, v.codigo, v.fecha_venta AS fecha, ve.id AS vehiculo_id,
                     TRIM(CONCAT(ve.marca, ' ', ve.modelo, ' ', COALESCE(ve.anio, ''))) AS descripcion,
                     ve.matricula, ve.vin, ve.color,
                     CASE v.tipo_venta WHEN 'contado' THEN 'Pagado'
                                       WHEN 'separacion' THEN 'Separado'
                                       ELSE 'En plazos' END AS estado,
                     v.precio AS monto,
                     GREATEST(v.precio - v.inicial - COALESCE((
                        SELECT SUM(pp.monto) FROM {DbNames.VentaPlazoPago} pp
                        JOIN {DbNames.VentaPlazo} pl ON pl.id = pp.plazo_id
                        WHERE pl.venta_id = v.id AND pp.deleted_at IS NULL), 0), 0) AS pendiente
              FROM {DbNames.VentaVehiculo} v
              JOIN {DbNames.Vehiculo} ve ON ve.id = v.vehiculo_id
              WHERE v.cliente_id = @id
              UNION ALL
              SELECT 'Alquiler' AS tipo, a.codigo, a.created_at AS fecha, ve.id AS vehiculo_id,
                     TRIM(CONCAT(ve.marca, ' ', ve.modelo, ' ', COALESCE(ve.anio, ''))) AS descripcion,
                     ve.matricula, ve.vin, ve.color,
                     CASE a.estado WHEN 'activo' THEN 'Alquilado'
                                   WHEN 'finalizado' THEN 'Devuelto'
                                   ELSE 'Cancelado' END AS estado,
                     a.monto_total AS monto,
                     CASE WHEN a.estado = 'activo' THEN a.monto_total ELSE 0 END AS pendiente
              FROM {DbNames.Alquiler} a
              JOIN {DbNames.Vehiculo} ve ON ve.id = a.vehiculo_id
              WHERE a.cliente_id = @id AND a.estado <> 'cancelado'
            ) f
            ORDER BY f.fecha DESC;
            """;
        cmd.Parameters.AddWithValue("@id", clienteId);

        var lista = new List<VehiculoDeCliente>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            lista.Add(new VehiculoDeCliente(
                reader.GetInt64("vehiculo_id"),
                reader.GetString("tipo"),
                reader.GetString("codigo"),
                DateTime.SpecifyKind(reader.GetDateTime("fecha"), DateTimeKind.Utc),
                reader.GetString("descripcion"),
                reader.IsDBNull(reader.GetOrdinal("matricula")) ? null : reader.GetString("matricula"),
                reader.IsDBNull(reader.GetOrdinal("vin")) ? null : reader.GetString("vin"),
                reader.IsDBNull(reader.GetOrdinal("color")) ? null : reader.GetString("color"),
                reader.GetString("estado"),
                reader.GetDecimal("monto"),
                reader.GetDecimal("pendiente")));
        }
        return lista;
    }
}
