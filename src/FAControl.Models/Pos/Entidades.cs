// Portado de POS500.Models el 2026-07-30 al integrar el punto de venta a la
// suite. Los tipos de usuarios, roles, permisos, sesion y auditoria NO se
// portaron: esos viven en facontrol_db y son los de FAControl, compartidos por
// todos los modos.
namespace FAControl.Models.Pos;

/// <summary>Cliente del negocio. Cedula OPCIONAL (retail). Soft delete.</summary>
public class Cliente
{
    public long Id { get; set; }
    public string? Cedula { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Telefono { get; set; }
    public string? Direccion { get; set; }
    public string? Notas { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}

/// <summary>Datos editables de un cliente (formulario nuevo/editar).</summary>
public record ClienteDatos(string? Cedula, string Nombre, string? Telefono, string? Direccion, string? Notas);

/// <summary>Producto del inventario. Codigo (barras) opcional. Soft delete.</summary>
public class Producto
{
    public long Id { get; set; }
    public string? Codigo { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public decimal Precio { get; set; }
    public int Cantidad { get; set; }
    public string? Descripcion { get; set; }
    public DateOnly? FechaCaducidad { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}

/// <summary>Datos editables de un producto (formulario nuevo/editar).</summary>
public record ProductoDatos(string? Codigo, string Nombre, decimal Precio, int Cantidad,
    string? Descripcion, DateOnly? FechaCaducidad);

/// <summary>Totales del módulo Almacén (calculados en SQL, no en UI).</summary>
public record AlmacenTotales(int TotalProductos, long TotalUnidades, decimal ValorInventario);

/// <summary>
/// Configuración compartida del negocio (tabla configuracion_negocio, fila única).
/// Se carga al iniciar y se expone vía ConfiguracionNegocioService.
/// </summary>
public class ConfiguracionNegocio
{
    public string NombreNegocio { get; set; } = "Mi Negocio";
    public string? Rnc { get; set; }
    public string? Direccion { get; set; }
    public string? Telefono { get; set; }
    public string? Email { get; set; }
    public string? LogoRuta { get; set; }
    public bool ItbisActivo { get; set; } = true;   // OFF: sin ITBIS en venta ni ticket
    public decimal ItbisTasa { get; set; } = 18.00m;
    /// <summary>Tasa que realmente se aplica al vender (0 si el ITBIS está apagado).</summary>
    public decimal ItbisTasaEfectiva => ItbisActivo ? ItbisTasa : 0m;

    // ---------- Comisión del vendedor (037) ----------
    // Del NEGOCIO, no de la terminal. Aparece en Vender (junto al subtotal), en
    // el cuadre del día y en la exportación a Excel.
    public bool ComisionActiva { get; set; }
    public decimal ComisionPorcentaje { get; set; }

    /// <summary>
    /// Si la comisión se imprime en la factura del cliente (038).
    ///
    /// En 037 nunca salía —es un asunto entre el negocio y su empleado—; el
    /// dueño pidió poder mostrarla, así que dejó de ser regla y pasó a ser
    /// opción. Se guarda aparte de <see cref="ComisionActiva"/> a propósito:
    /// calcular la comisión y enseñársela al cliente son dos decisiones
    /// distintas, y quien quiera lo primero sin lo segundo no debería tener
    /// que elegir.
    /// </summary>
    public bool ComisionEnFactura { get; set; }

    /// <summary>
    /// Si el ticket debe imprimir la línea de comisión. Sin comisión activa no
    /// hay nada que mostrar, por más marcada que esté la casilla.
    /// </summary>
    public bool MuestraComisionEnFactura => ComisionActiva && ComisionEnFactura;

    /// <summary>Porcentaje que realmente se aplica (0 si la comisión está apagada).</summary>
    public decimal ComisionEfectiva => ComisionActiva ? ComisionPorcentaje : 0m;

    /// <summary>
    /// Comisión sobre un monto vendido. Se redondea al final, nunca por línea:
    /// acumular redondeos daría un número que no cuadra con el total.
    /// </summary>
    public decimal ComisionSobre(decimal monto) =>
        ComisionEfectiva <= 0m
            ? 0m
            : Math.Round(monto * ComisionEfectiva / 100m, 2, MidpointRounding.AwayFromZero);
    public ModoRedondeo Redondeo { get; set; } = ModoRedondeo.Centavo;
    public string MonedaSimbolo { get; set; } = "RD$";
    public string FormatoMiles { get; set; } = "coma";
    public string FacturaPrefijo { get; set; } = "F-";
    public long FacturaSiguiente { get; set; } = 1;
    public FormatoFactura FacturaFormato { get; set; } = FormatoFactura.Simple;
    public bool MostrarClienteEnVenta { get; set; } = true;   // regla Yuber 2026-07-11
}

/// <summary>Línea del carrito al momento de facturar.</summary>
public record VentaLinea(long ProductoId, string NombreProducto, int Cantidad, decimal PrecioUnitario)
{
    public decimal Subtotal => Cantidad * PrecioUnitario;
}

/// <summary>Solicitud de venta (lo que el cajero confirmó en pantalla).</summary>
public record VentaSolicitud(
    IReadOnlyList<VentaLinea> Lineas,
    long? ClienteId,                       // NULL = consumidor final (regla Yuber)
    MetodoPagoFactura MetodoPago,
    decimal? EfectivoRecibido);

/// <summary>Totales calculados de una venta (ITBIS sobre subtotal, spec §7).</summary>
public record VentaTotales(decimal Subtotal, decimal ItbisTasa, decimal Itbis, decimal Total);

/// <summary>Resultado de una venta registrada (para el ticket).</summary>
public record VentaResultado(
    long FacturaId,
    string NumeroFactura,
    DateTime FechaEmisionUtc,
    VentaTotales Totales,
    decimal? EfectivoRecibido,
    decimal? Cambio,
    IReadOnlyList<VentaLinea> Lineas,
    string? NombreCliente,
    MetodoPagoFactura MetodoPago);

/// <summary>Fila de la lista de comprobantes (sin sus líneas).</summary>
public record FacturaResumen(
    long Id,
    string NumeroFactura,
    DateTime FechaEmisionUtc,
    string? NombreCliente,
    string NombreCajero,
    long UsuarioId,
    decimal Total,
    MetodoPagoFactura MetodoPago,
    EstadoFactura Estado,
    string? AnuladaMotivo);

/// <summary>Línea de una factura ya emitida (leída de BD).</summary>
public record FacturaLinea(long ProductoId, string NombreProducto, int Cantidad,
    decimal PrecioUnitario, decimal Subtotal);

/// <summary>Factura completa para ver el detalle y reimprimir el ticket.</summary>
public record FacturaCompleta(FacturaResumen Resumen, VentaTotales Totales,
    decimal? EfectivoRecibido, decimal? Cambio, IReadOnlyList<FacturaLinea> Lineas);

/// <summary>Filtros de la búsqueda de comprobantes.</summary>
public record FiltroComprobantes(
    string? Texto,          // número de factura o nombre de cliente
    DateOnly? Desde,        // día de negocio (UTC-4)
    DateOnly? Hasta,
    long? UsuarioId,        // null = todos (requiere permiso comprobantes_todos)
    int Limite = 200);

/// <summary>Totales del cuadre de un cajero en un día de negocio.</summary>
public record CuadreResumen(
    long UsuarioId,
    string NombreCajero,
    DateOnly Fecha,
    int TotalFacturas,
    decimal TotalVendido,
    decimal TotalEfectivo,
    decimal TotalTarjeta,
    decimal TotalTransferencia,
    decimal TotalMixto,
    int FacturasAnuladas,
    decimal MontoAnulado,
    int TiempoActivoSegundos,
    bool YaCerrado)
{
    public string TiempoActivoTexto
    {
        get
        {
            var t = TimeSpan.FromSeconds(TiempoActivoSegundos);
            return t.TotalHours >= 1
                ? $"{(int)t.TotalHours}h {t.Minutes}min"
                : $"{t.Minutes}min";
        }
    }
}

// ---------------------------------------------------------------------
// Analítica (Fase 5)
// ---------------------------------------------------------------------

/// <summary>Ventas de un día de negocio (para el gráfico de tendencia).</summary>
public record VentaDiaria(DateOnly Fecha, decimal Monto, int Facturas);

/// <summary>Ranking de cajeros/vendedores por ventas.</summary>
public record VendedorRanking(string Nombre, int Facturas, decimal Total);

/// <summary>Ranking de productos más vendidos.</summary>
public record ProductoRanking(string Nombre, int Unidades, decimal Total);

/// <summary>Totales por método de pago (reutilizado en Dashboard y Reportes).</summary>
public record TotalesPorMetodo(decimal Efectivo, decimal Tarjeta, decimal Transferencia, decimal Mixto);

/// <summary>Datos del Panel: KPIs del día/mes, tendencia y rankings.</summary>
public record DashboardDatos(
    decimal VentasHoy,
    int FacturasHoy,
    decimal VentasMes,
    decimal VentasMesAnterior,
    decimal TicketPromedioMes,
    int ProductosPorCaducar,
    int ProductosStockBajo,
    IReadOnlyList<VentaDiaria> VentasPorDia,
    IReadOnlyList<VendedorRanking> TopVendedores,
    IReadOnlyList<ProductoRanking> TopProductos);

/// <summary>Reporte de ventas de un rango de días de negocio.</summary>
public record ReporteVentas(
    DateOnly Desde,
    DateOnly Hasta,
    decimal TotalVendido,
    int TotalFacturas,
    decimal TotalItbis,
    decimal TicketPromedio,
    TotalesPorMetodo PorMetodo,
    int FacturasAnuladas,
    decimal MontoAnulado,
    IReadOnlyList<VentaDiaria> VentasPorDia,
    IReadOnlyList<ProductoRanking> TopProductos,
    IReadOnlyList<VendedorRanking> PorCajero);

/// <summary>
/// Cuadre GENERAL del día: el desglose de cada cajero + los totales del negocio
/// (pedido Yuber 2026-07-12). Es la vista por defecto del módulo.
/// </summary>
public record CuadreGeneral(
    DateOnly Fecha,
    IReadOnlyList<CuadreResumen> PorCajero,
    int TotalFacturas,
    decimal TotalVendido,
    decimal TotalEfectivo,
    decimal TotalTarjeta,
    decimal TotalTransferencia,
    decimal TotalMixto,
    int FacturasAnuladas,
    decimal MontoAnulado)
{
    public bool TodosCerrados => PorCajero.Count > 0 && PorCajero.All(c => c.YaCerrado);
    public bool HayPendientes => PorCajero.Any(c => !c.YaCerrado);
}

/// <summary>Tamaño del papel para imprimir el cierre de caja.</summary>
public enum TamanoImpresion
{
    Ticket80mm,
    Carta
}

/// <summary>Entrada del log de auditoría (inmutable, nunca se borra).</summary>
public class Auditoria
{
    public long Id { get; set; }
    public long UsuarioId { get; set; }
    public string Entidad { get; set; } = string.Empty;
    public long? EntidadId { get; set; }
    public AccionAuditoria Accion { get; set; }
    public string? Descripcion { get; set; }
    public string? IpLocal { get; set; }
    public DateTime TimestampUtc { get; set; }
}
