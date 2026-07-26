using MySqlConnector;
using FAControl.Common;
using FAControl.Models;

namespace FAControl.Data;

/// <summary>
/// Acceso a venta_vehiculo (venta al contado del dealer). La inserción va
/// dentro de la transacción del Service (reserva de código + venta + marcar
/// el vehículo 'vendido' + auditoría).
/// </summary>
public class VentaVehiculoRepository
{
    private readonly ConexionFactory _factory;

    public VentaVehiculoRepository(ConexionFactory factory) => _factory = factory;

    public async Task<long> InsertarAsync(VentaVehiculo venta, MySqlConnection conexion,
        MySqlTransaction transaccion, CancellationToken ct = default)
    {
        using var cmd = conexion.CreateCommand();
        cmd.Transaction = transaccion;
        cmd.CommandText = $"""
            INSERT INTO {DbNames.VentaVehiculo}
              (codigo, vehiculo_id, cliente_id, precio, metodo_pago, notas, created_by)
            VALUES
              (@codigo, @vehiculoId, @clienteId, @precio, @metodoPago, @notas, @createdBy);
            SELECT LAST_INSERT_ID();
            """;
        cmd.Parameters.AddWithValue("@codigo", venta.Codigo);
        cmd.Parameters.AddWithValue("@vehiculoId", venta.VehiculoId);
        cmd.Parameters.AddWithValue("@clienteId", venta.ClienteId);
        cmd.Parameters.AddWithValue("@precio", venta.Precio);
        cmd.Parameters.AddWithValue("@metodoPago", EnumMap.ADb(venta.MetodoPago));
        cmd.Parameters.AddWithValue("@notas", (object?)venta.Notas ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@createdBy", SesionActual.HaySesionActiva ? SesionActual.Id : (object)DBNull.Value);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
    }

    /// <summary>Lista de ventas al contado con datos del vehículo y cliente.</summary>
    public async Task<IReadOnlyList<VentaResumen>> ObtenerResumenesAsync(CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT vv.id, vv.codigo,
                   CONCAT(v.marca, ' ', v.modelo,
                          COALESCE(CONCAT(' ', v.anio), '')) AS vehiculo_desc,
                   TRIM(CONCAT(c.nombre, ' ', c.apellido)) AS cliente_nombre,
                   vv.fecha_venta, vv.precio, vv.metodo_pago
            FROM {DbNames.VentaVehiculo} vv
            JOIN {DbNames.Vehiculo} v ON v.id = vv.vehiculo_id
            JOIN {DbNames.Cliente} c ON c.id = vv.cliente_id
            ORDER BY vv.fecha_venta DESC;
            """;

        var lista = new List<VentaResumen>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            lista.Add(new VentaResumen(
                reader.GetInt64("id"),
                reader.GetString("codigo"),
                reader.GetString("vehiculo_desc"),
                reader.GetString("cliente_nombre"),
                DateTime.SpecifyKind(reader.GetDateTime("fecha_venta"), DateTimeKind.Utc),
                reader.GetDecimal("precio"),
                EnumMap.MetodoPagoDeDb(reader.GetString("metodo_pago"))));
        }
        return lista;
    }

    /// <summary>Última venta de un vehículo (para la ficha: cliente que lo compró). Null si nunca se vendió.</summary>
    public async Task<VentaResumen?> ObtenerDeVehiculoAsync(long vehiculoId, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT vv.id, vv.codigo,
                   CONCAT(v.marca, ' ', v.modelo,
                          COALESCE(CONCAT(' ', v.anio), '')) AS vehiculo_desc,
                   TRIM(CONCAT(c.nombre, ' ', c.apellido)) AS cliente_nombre,
                   vv.fecha_venta, vv.precio, vv.metodo_pago
            FROM {DbNames.VentaVehiculo} vv
            JOIN {DbNames.Vehiculo} v ON v.id = vv.vehiculo_id
            JOIN {DbNames.Cliente} c ON c.id = vv.cliente_id
            WHERE vv.vehiculo_id = @vehiculoId
            ORDER BY vv.fecha_venta DESC
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("@vehiculoId", vehiculoId);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;
        return new VentaResumen(
            reader.GetInt64("id"),
            reader.GetString("codigo"),
            reader.GetString("vehiculo_desc"),
            reader.GetString("cliente_nombre"),
            DateTime.SpecifyKind(reader.GetDateTime("fecha_venta"), DateTimeKind.Utc),
            reader.GetDecimal("precio"),
            EnumMap.MetodoPagoDeDb(reader.GetString("metodo_pago")));
    }
}
