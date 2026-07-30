// Portado de POS-500 el 2026-07-30 al integrar el punto de venta a la suite.
// Cambios respecto del original: usa ConexionPos500 (base pos500_db, aparte de
// facontrol_db) y el SesionActual / la auditoria compartidos de FAControl.
using MySqlConnector;
using FAControl.Common;
using FAControl.Models.Pos;

namespace FAControl.Data.Pos;

/// <summary>
/// Escritura de facturas. Los métodos transaccionales reciben la conexión y
/// transacción del VentaService: la emisión es UN solo commit (spec §9.2/§9.4).
/// Las facturas JAMÁS se borran (solo estado anulada, Fase 4).
/// </summary>
public class FacturaRepository
{
    private readonly ConexionPos500 _factory;

    public FacturaRepository(ConexionPos500 factory) => _factory = factory;

    public async Task<MySqlConnection> AbrirConexionAsync(CancellationToken ct = default) =>
        await _factory.AbrirAsync(ct);

    /// <summary>
    /// Base de la suite (facontrol_db). La necesitan los services del POS para
    /// escribir la auditoría —que es compartida y vive allá— dentro de la MISMA
    /// transacción de la venta.
    /// </summary>
    public string EsquemaSuite => _factory.EsquemaSuite;

    // ------------------------------------------------------------------
    // Lectura (Buscar comprobante)
    // ------------------------------------------------------------------

    // Columnas y FROM separados: el detalle añade columnas extra al SELECT y
    // pegarlas después del FROM haría que MySQL las lea como tablas
    private const string ColumnasResumen = """
        f.id, f.numero_factura, f.fecha_emision, f.total, f.metodo_pago, f.estado,
        f.anulada_motivo, f.usuario_id,
        c.nombre AS cliente_nombre,
        TRIM(CONCAT(u.nombre, ' ', COALESCE(u.apellido, ''))) AS cajero_nombre
        """;

    /// <summary>
    /// Propiedad y no constante: `usuario` vive en la base de la SUITE, y su
    /// nombre sale de la cadena de conexión. Las facturas y los clientes del
    /// mostrador sí están en la base del POS, por eso van sin calificar.
    /// </summary>
    private string FromResumen => $"""
        FROM factura f
        LEFT JOIN cliente c ON c.id = f.cliente_id
        JOIN {_factory.EsquemaSuite}.usuario u ON u.id = f.usuario_id
        """;

    /// <summary>
    /// Búsqueda de comprobantes. El rango de fechas se evalúa por DÍA DE NEGOCIO
    /// (RD = UTC-4): una venta de las 10pm del lunes pertenece al lunes, no al martes.
    /// </summary>
    public async Task<List<FacturaResumen>> BuscarAsync(FiltroComprobantes filtro, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT {ColumnasResumen}
            {FromResumen}
            WHERE (@texto IS NULL
                   OR f.numero_factura LIKE CONCAT('%', @texto, '%')
                   OR c.nombre LIKE CONCAT('%', @texto, '%'))
              AND (@desde IS NULL OR DATE(DATE_SUB(f.fecha_emision, INTERVAL 4 HOUR)) >= @desde)
              AND (@hasta IS NULL OR DATE(DATE_SUB(f.fecha_emision, INTERVAL 4 HOUR)) <= @hasta)
              AND (@usuarioId IS NULL OR f.usuario_id = @usuarioId)
            ORDER BY f.fecha_emision DESC
            LIMIT @limite;
            """;
        cmd.Parameters.AddWithValue("@texto",
            string.IsNullOrWhiteSpace(filtro.Texto) ? DBNull.Value : filtro.Texto.Trim());
        cmd.Parameters.AddWithValue("@desde",
            filtro.Desde is { } d ? d.ToDateTime(TimeOnly.MinValue) : DBNull.Value);
        cmd.Parameters.AddWithValue("@hasta",
            filtro.Hasta is { } h ? h.ToDateTime(TimeOnly.MinValue) : DBNull.Value);
        cmd.Parameters.AddWithValue("@usuarioId", (object?)filtro.UsuarioId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@limite", filtro.Limite);

        var lista = new List<FacturaResumen>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            lista.Add(MapearResumen(reader));
        return lista;
    }

    /// <summary>Factura completa con sus líneas (detalle + reimpresión del ticket).</summary>
    public async Task<FacturaCompleta?> ObtenerCompletaAsync(long facturaId, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);

        FacturaResumen resumen;
        VentaTotales totales;
        decimal? efectivo, cambio;

        using (var cmd = conexion.CreateCommand())
        {
            cmd.CommandText = $"""
                SELECT {ColumnasResumen},
                       f.subtotal, f.itbis_tasa, f.itbis, f.efectivo_recibido, f.cambio
                {FromResumen}
                WHERE f.id = @id;
                """;
            cmd.Parameters.AddWithValue("@id", facturaId);

            using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                return null;

            resumen = MapearResumen(reader);
            totales = new VentaTotales(
                reader.GetDecimal("subtotal"),
                reader.GetDecimal("itbis_tasa"),
                reader.GetDecimal("itbis"),
                reader.GetDecimal("total"));
            efectivo = reader.IsDBNull(reader.GetOrdinal("efectivo_recibido"))
                ? null : reader.GetDecimal("efectivo_recibido");
            cambio = reader.IsDBNull(reader.GetOrdinal("cambio"))
                ? null : reader.GetDecimal("cambio");
        }

        var lineas = new List<FacturaLinea>();
        using (var cmd = conexion.CreateCommand())
        {
            cmd.CommandText = $"""
                SELECT d.producto_id, p.nombre, d.cantidad, d.precio_unitario, d.subtotal
                FROM {DbNamesPos.Detalle} d
                JOIN {DbNamesPos.Producto} p ON p.id = d.producto_id
                WHERE d.factura_id = @id
                ORDER BY d.id;
                """;
            cmd.Parameters.AddWithValue("@id", facturaId);

            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                lineas.Add(new FacturaLinea(
                    reader.GetInt64("producto_id"),
                    reader.GetString("nombre"),
                    reader.GetInt32("cantidad"),
                    reader.GetDecimal("precio_unitario"),
                    reader.GetDecimal("subtotal")));
        }

        return new FacturaCompleta(resumen, totales, efectivo, cambio, lineas);
    }

    // ------------------------------------------------------------------
    // Anulación (nunca se borra una factura — spec §9.3)
    // ------------------------------------------------------------------

    /// <summary>
    /// Marca la factura como anulada. Devuelve false si ya lo estaba (el
    /// WHERE exige estado='emitida'), lo que evita anular dos veces y
    /// devolver el stock por duplicado.
    /// </summary>
    public static async Task<bool> MarcarAnuladaAsync(
        MySqlConnection conexion, MySqlTransaction transaccion,
        long facturaId, string motivo, DateTime ahoraUtc, CancellationToken ct = default)
    {
        using var cmd = conexion.CreateCommand();
        cmd.Transaction = transaccion;
        cmd.CommandText = $"""
            UPDATE {DbNamesPos.Factura}
            SET estado = 'anulada', anulada_at = @ahora, anulada_motivo = @motivo
            WHERE id = @id AND estado = 'emitida';
            """;
        cmd.Parameters.AddWithValue("@ahora", ahoraUtc);
        cmd.Parameters.AddWithValue("@motivo", motivo);
        cmd.Parameters.AddWithValue("@id", facturaId);
        return await cmd.ExecuteNonQueryAsync(ct) == 1;
    }

    /// <summary>Devuelve al inventario lo vendido en la factura (al anular).</summary>
    public static async Task DevolverStockAsync(
        MySqlConnection conexion, MySqlTransaction transaccion,
        long facturaId, CancellationToken ct = default)
    {
        using var cmd = conexion.CreateCommand();
        cmd.Transaction = transaccion;
        cmd.CommandText = $"""
            UPDATE {DbNamesPos.Producto} p
            JOIN {DbNamesPos.Detalle} d ON d.producto_id = p.id
            SET p.cantidad = p.cantidad + d.cantidad, p.updated_at = UTC_TIMESTAMP()
            WHERE d.factura_id = @facturaId;
            """;
        cmd.Parameters.AddWithValue("@facturaId", facturaId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static FacturaResumen MapearResumen(MySqlDataReader reader) => new(
        reader.GetInt64("id"),
        reader.GetString("numero_factura"),
        DateTime.SpecifyKind(reader.GetDateTime("fecha_emision"), DateTimeKind.Utc),
        reader.IsDBNull(reader.GetOrdinal("cliente_nombre")) ? null : reader.GetString("cliente_nombre"),
        reader.GetString("cajero_nombre"),
        reader.GetInt64("usuario_id"),
        reader.GetDecimal("total"),
        EnumMap.MetodoPagoDeDb(reader.GetString("metodo_pago")),
        EnumMap.EstadoFacturaDeDb(reader.GetString("estado")),
        reader.IsDBNull(reader.GetOrdinal("anulada_motivo")) ? null : reader.GetString("anulada_motivo"));

    /// <summary>
    /// Descuenta stock validando disponibilidad EN LA MISMA sentencia
    /// (cantidad >= @cantidad): si otro cajero vendió el mismo producto a la
    /// vez, afecta 0 filas y la venta completa se revierte (spec §9.5).
    /// </summary>
    public static async Task<bool> DescontarStockAsync(
        MySqlConnection conexion, MySqlTransaction transaccion,
        long productoId, int cantidad, CancellationToken ct = default)
    {
        using var cmd = conexion.CreateCommand();
        cmd.Transaction = transaccion;
        cmd.CommandText = $"""
            UPDATE {DbNamesPos.Producto}
            SET cantidad = cantidad - @cantidad, updated_at = UTC_TIMESTAMP()
            WHERE id = @id AND deleted_at IS NULL AND cantidad >= @cantidad;
            """;
        cmd.Parameters.AddWithValue("@cantidad", cantidad);
        cmd.Parameters.AddWithValue("@id", productoId);
        return await cmd.ExecuteNonQueryAsync(ct) == 1;
    }

    public static async Task<long> InsertarFacturaAsync(
        MySqlConnection conexion, MySqlTransaction transaccion,
        string numeroFactura, long? clienteId, long usuarioId, DateTime fechaEmisionUtc,
        VentaTotales totales, MetodoPagoFactura metodoPago,
        decimal? efectivoRecibido, decimal? cambio, CancellationToken ct = default)
    {
        using var cmd = conexion.CreateCommand();
        cmd.Transaction = transaccion;
        cmd.CommandText = $"""
            INSERT INTO {DbNamesPos.Factura}
                (numero_factura, cliente_id, usuario_id, fecha_emision,
                 subtotal, itbis_tasa, itbis, total, metodo_pago,
                 efectivo_recibido, cambio, estado)
            VALUES
                (@numero, @clienteId, @usuarioId, @fecha,
                 @subtotal, @itbisTasa, @itbis, @total, @metodoPago,
                 @efectivo, @cambio, 'emitida');
            SELECT LAST_INSERT_ID();
            """;
        cmd.Parameters.AddWithValue("@numero", numeroFactura);
        cmd.Parameters.AddWithValue("@clienteId", (object?)clienteId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@usuarioId", usuarioId);
        cmd.Parameters.AddWithValue("@fecha", fechaEmisionUtc);
        cmd.Parameters.AddWithValue("@subtotal", totales.Subtotal);
        cmd.Parameters.AddWithValue("@itbisTasa", totales.ItbisTasa);
        cmd.Parameters.AddWithValue("@itbis", totales.Itbis);
        cmd.Parameters.AddWithValue("@total", totales.Total);
        cmd.Parameters.AddWithValue("@metodoPago", EnumMap.ADb(metodoPago));
        cmd.Parameters.AddWithValue("@efectivo", (object?)efectivoRecibido ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cambio", (object?)cambio ?? DBNull.Value);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
    }

    public static async Task InsertarDetalleAsync(
        MySqlConnection conexion, MySqlTransaction transaccion,
        long facturaId, VentaLinea linea, CancellationToken ct = default)
    {
        using var cmd = conexion.CreateCommand();
        cmd.Transaction = transaccion;
        cmd.CommandText = $"""
            INSERT INTO {DbNamesPos.Detalle}
                (factura_id, producto_id, cantidad, precio_unitario, subtotal)
            VALUES (@facturaId, @productoId, @cantidad, @precio, @subtotal);
            """;
        cmd.Parameters.AddWithValue("@facturaId", facturaId);
        cmd.Parameters.AddWithValue("@productoId", linea.ProductoId);
        cmd.Parameters.AddWithValue("@cantidad", linea.Cantidad);
        cmd.Parameters.AddWithValue("@precio", linea.PrecioUnitario);
        cmd.Parameters.AddWithValue("@subtotal", linea.Subtotal);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
