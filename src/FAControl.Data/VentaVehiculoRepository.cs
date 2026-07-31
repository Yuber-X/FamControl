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
              (codigo, vehiculo_id, cliente_id, precio, tipo_venta, inicial, fecha_limite,
               metodo_pago, notas, created_by)
            VALUES
              (@codigo, @vehiculoId, @clienteId, @precio, @tipoVenta, @inicial, @fechaLimite,
               @metodoPago, @notas, @createdBy);
            SELECT LAST_INSERT_ID();
            """;
        cmd.Parameters.AddWithValue("@codigo", venta.Codigo);
        cmd.Parameters.AddWithValue("@vehiculoId", venta.VehiculoId);
        cmd.Parameters.AddWithValue("@clienteId", venta.ClienteId);
        cmd.Parameters.AddWithValue("@precio", venta.Precio);
        cmd.Parameters.AddWithValue("@tipoVenta", EnumMap.ADb(venta.TipoVenta));
        cmd.Parameters.AddWithValue("@inicial", venta.Inicial);
        cmd.Parameters.AddWithValue("@fechaLimite",
            (object?)venta.FechaLimite?.ToDateTime(TimeOnly.MinValue) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@metodoPago", EnumMap.ADb(venta.MetodoPago));
        cmd.Parameters.AddWithValue("@notas", (object?)venta.Notas ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@createdBy", SesionActual.HaySesionActiva ? SesionActual.Id : (object)DBNull.Value);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
    }

    /// <summary>La venta con sus datos de financiamiento (016). Null si no existe.</summary>
    public async Task<VentaVehiculo?> ObtenerPorIdAsync(long id, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT id, codigo, vehiculo_id, cliente_id, fecha_venta, precio,
                   tipo_venta, inicial, fecha_limite, metodo_pago, notas, created_at
            FROM {DbNames.VentaVehiculo}
            WHERE id = @id;
            """;
        cmd.Parameters.AddWithValue("@id", id);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;
        return new VentaVehiculo
        {
            Id = reader.GetInt64("id"),
            Codigo = reader.GetString("codigo"),
            VehiculoId = reader.GetInt64("vehiculo_id"),
            ClienteId = reader.GetInt64("cliente_id"),
            FechaVentaUtc = DateTime.SpecifyKind(reader.GetDateTime("fecha_venta"), DateTimeKind.Utc),
            Precio = reader.GetDecimal("precio"),
            TipoVenta = EnumMap.TipoVentaDeDb(reader.GetString("tipo_venta")),
            Inicial = reader.GetDecimal("inicial"),
            FechaLimite = reader.IsDBNull(reader.GetOrdinal("fecha_limite"))
                ? null
                : DateOnly.FromDateTime(reader.GetDateTime("fecha_limite")),
            MetodoPago = EnumMap.MetodoPagoDeDb(reader.GetString("metodo_pago")),
            Notas = reader.IsDBNull(reader.GetOrdinal("notas")) ? null : reader.GetString("notas"),
            CreatedAtUtc = DateTime.SpecifyKind(reader.GetDateTime("created_at"), DateTimeKind.Utc)
        };
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
                   vv.fecha_venta, vv.precio, vv.metodo_pago, vv.tipo_venta,
                   COALESCE(TRIM(CONCAT(u.nombre, ' ', COALESCE(u.apellido, ''))), '—') AS vendedor
            FROM {DbNames.VentaVehiculo} vv
            JOIN {DbNames.Vehiculo} v ON v.id = vv.vehiculo_id
            JOIN {DbNames.Cliente} c ON c.id = vv.cliente_id
            -- LEFT: la venta no se pierde del listado si se borró el usuario
            LEFT JOIN {DbNames.Usuario} u ON u.id = vv.created_by
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
                EnumMap.MetodoPagoDeDb(reader.GetString("metodo_pago")),
                EnumMap.TipoVentaDeDb(reader.GetString("tipo_venta")),
                reader.GetString("vendedor")));
        }
        return lista;
    }

    /// <summary>
    /// Datos completos de una venta para su FACTURA (pedido 2026-07-25):
    /// venta + cliente + vehículo + quién vendió. Null si la venta no existe.
    /// </summary>
    public async Task<FacturaVentaDatos?> ObtenerFacturaAsync(long ventaId, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT vv.codigo, vv.fecha_venta, vv.precio, vv.metodo_pago, vv.notas,
                   TRIM(CONCAT(c.nombre, ' ', COALESCE(c.apellido, ''))) AS cliente_nombre,
                   c.cedula AS cliente_cedula, c.telefono AS cliente_telefono,
                   c.direccion AS cliente_direccion,
                   CONCAT(v.marca, ' ', v.modelo,
                          COALESCE(CONCAT(' ', v.anio), '')) AS vehiculo_desc,
                   v.vin, v.placa, v.matricula, v.color, v.anio,
                   COALESCE(u.nombre, '—') AS vendedor
            FROM {DbNames.VentaVehiculo} vv
            JOIN {DbNames.Cliente} c ON c.id = vv.cliente_id
            JOIN {DbNames.Vehiculo} v ON v.id = vv.vehiculo_id
            LEFT JOIN {DbNames.Usuario} u ON u.id = vv.created_by
            WHERE vv.id = @id;
            """;
        cmd.Parameters.AddWithValue("@id", ventaId);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        string? Texto(string columna) =>
            reader.IsDBNull(reader.GetOrdinal(columna)) ? null : reader.GetString(columna);

        return new FacturaVentaDatos(
            reader.GetString("codigo"),
            DateTime.SpecifyKind(reader.GetDateTime("fecha_venta"), DateTimeKind.Utc),
            reader.GetDecimal("precio"),
            EnumMap.MetodoPagoDeDb(reader.GetString("metodo_pago")),
            Texto("notas"),
            reader.GetString("cliente_nombre"),
            Texto("cliente_cedula"),
            Texto("cliente_telefono"),
            Texto("cliente_direccion"),
            reader.GetString("vehiculo_desc"),
            Texto("vin"),
            Texto("placa"),
            Texto("matricula"),
            Texto("color"),
            reader.IsDBNull(reader.GetOrdinal("anio")) ? null : reader.GetInt32(reader.GetOrdinal("anio")),
            reader.GetString("vendedor"));
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
