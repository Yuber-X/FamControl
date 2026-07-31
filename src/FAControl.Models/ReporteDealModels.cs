namespace FAControl.Models;

/// <summary>
/// Fila del expediente de contratos del dealer (pedido 2026-07-25): cliente,
/// quién vendió, cantidad de documentos, matrícula del auto y estado de pago.
/// </summary>
public record ContratoDealFila(
    long VentaId,
    string Codigo,
    DateTime FechaUtc,
    string ClienteNombre,
    string VendedorNombre,
    string VehiculoDescripcion,
    string? Matricula,
    string? Placa,
    decimal Precio,
    TipoVenta TipoVenta,
    int PlazosTotales,
    int PlazosPagados,
    int PlazosAtrasados,
    decimal Pendiente,
    /// <summary>Archivos guardados en el expediente digital de esta venta (018).</summary>
    int DocumentosAdjuntos = 0,
    /// <summary>
    /// True si la fila es un ALQUILER y no una venta (032 — "tambien debe
    /// mostrarse en contratos"). Los dos son contratos del dealer y se listan
    /// juntos, pero sus datos viven en tablas distintas y no se mezclan: por eso
    /// la fila lleva de que es, en vez de meter 'alquiler' dentro de TipoVenta.
    /// </summary>
    bool EsAlquiler = false,
    /// <summary>Alquiler: como esta el contrato ('Activo', 'Finalizado', 'Cancelado').</summary>
    string EstadoAlquilerTexto = "")
{
    /// <summary>
    /// Documentos del expediente: los que EMITE la app (factura y, según cómo
    /// se pactó, carta de compromiso o recibo de separación) más los que
    /// SUBIÓ el usuario al expediente digital (018).
    /// </summary>
    public int CantidadDocumentos => EsAlquiler
        // Un alquiler no emite factura ni carta: sus papeles son los que se
        // suben (contrato firmado, licencia, fotos del auto).
        ? DocumentosAdjuntos
        : DocumentosAdjuntos + TipoVenta switch
        {
            TipoVenta.Plazos => 2,        // factura + carta de compromiso
            TipoVenta.Separacion => 2,    // factura + recibo de separación
            _ => 1                        // factura
        };

    public bool TienePlan => !EsAlquiler && TipoVenta != TipoVenta.Contado;
}

/// <summary>
/// Ventas de un vendedor en el período, con su comisión (pedido 2026-07-25:
/// "contratos > mostrar usuario > comisiones"). El PORCENTAJE lo define el
/// negocio en Configuración: la app no inventa la tasa de comisión.
/// </summary>
public record ComisionVendedor(
    string VendedorNombre,
    int CantidadVentas,
    decimal MontoVendido,
    decimal Comision);

/// <summary>
/// Reporte propio de DealControl (pedido 2026-07-25: "agregar su propio
/// reportes, no mezclar con los datos del prestControl"). Solo toca las tablas
/// del dealer: vehiculo, venta_vehiculo y alquiler.
/// </summary>
public record ReporteDeal(
    DateOnly Desde,
    DateOnly Hasta,
    // Ventas del período
    int CantidadVentas,
    decimal MontoVendido,
    /// <summary>Precio de venta menos costo total del vehículo (adquisición + importación).</summary>
    decimal GananciaVentas,
    // Alquileres del período
    int CantidadAlquileres,
    decimal IngresosAlquiler,
    // Inventario al día de hoy
    int VehiculosDisponibles,
    decimal CapitalInvertido,
    // Financiamiento vivo
    decimal PendienteDeCobro,
    IReadOnlyList<ComisionVendedor> PorVendedor);
