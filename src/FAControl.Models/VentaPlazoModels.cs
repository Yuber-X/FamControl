namespace FAControl.Models;

/// <summary>
/// Cómo se pactó la venta del dealer (016). Coincide con ENUM venta_vehiculo.tipo_venta.
/// </summary>
public enum TipoVenta
{
    /// <summary>Pago completo al firmar.</summary>
    Contado,
    /// <summary>Inicial + N plazos pactados con el dealer (sin interés).</summary>
    Plazos,
    /// <summary>Reserva/apartado: el cliente da un adelanto y tiene N días para completar.</summary>
    Separacion
}

/// <summary>Estado de un plazo. Coincide con ENUM venta_plazo.estado.</summary>
public enum EstadoPlazo
{
    Pendiente,
    Pagado,
    Cancelado
}

/// <summary>Un plazo del calendario de pagos de una venta financiada (016).</summary>
public class VentaPlazo
{
    public long Id { get; set; }
    public long VentaId { get; set; }
    public int Numero { get; set; }
    public DateOnly FechaVencimiento { get; set; }
    public decimal Monto { get; set; }
    public decimal MontoPagado { get; set; }
    public EstadoPlazo Estado { get; set; } = EstadoPlazo.Pendiente;

    public decimal SaldoPendiente => Math.Max(0m, Monto - MontoPagado);

    /// <summary>Venció y todavía debe. El semáforo del dealer es simple: al día o atrasado.</summary>
    public bool EstaAtrasado(DateOnly hoy) =>
        Estado == EstadoPlazo.Pendiente && FechaVencimiento < hoy && SaldoPendiente > 0m;
}

/// <summary>
/// Lo que dejó un abono a una venta financiada (2026-07-31). Un solo cobro
/// puede tocar VARIOS plazos: el excedente del plazo actual baja al siguiente,
/// igual que el adelanto de PrestControl.
/// </summary>
public record AbonoVentaResultado(
    IReadOnlyList<string> Recibos,
    decimal Aplicado,
    int PlazosSaldados,
    /// <summary>Lo que queda por pagar de la venta después de este abono.</summary>
    decimal SaldoRestante)
{
    public bool TocoVariosPlazos => Recibos.Count > 1;
    public bool VentaSaldada => SaldoRestante <= 0m;
}

/// <summary>
/// Cancelación de una venta financiada: el cliente devuelve el vehículo (028).
/// </summary>
/// <param name="RetencionPorcentaje">
/// Lo que el negocio se queda de lo ya cobrado, en %. Lo digita el dueño: el
/// contrato de cada dealer lo fija distinto y no es algo que deba decidir el
/// programa (decisión de Yuber, 2026-07-31).
/// </param>
public record CancelacionVenta(long VentaId, string Motivo, decimal RetencionPorcentaje);

/// <summary>Cómo quedó la plata al cancelar (028).</summary>
public record ResultadoCancelacion(
    decimal Cobrado,
    decimal Retenido,
    decimal Devuelto,
    decimal RetencionPorcentaje);

/// <summary>Abono a un plazo, con su recibo (016).</summary>
public class VentaPlazoPago
{
    public long Id { get; set; }
    public long PlazoId { get; set; }
    public string NumeroRecibo { get; set; } = string.Empty;
    public DateTime FechaPagoUtc { get; set; }
    public decimal Monto { get; set; }
    public MetodoPago MetodoPago { get; set; } = MetodoPago.Efectivo;
    public string? Notas { get; set; }
}

/// <summary>
/// Datos que captura la pantalla de venta financiada: inicial + N plazos
/// desde una fecha, repartidos en partes iguales (el resto cae en el último).
/// </summary>
public record PlanPlazos(
    decimal Inicial,
    int CantidadPlazos,
    DateOnly FechaPrimerPlazo,
    /// <summary>Cada cuántos días vence el siguiente plazo (30 = mensual).</summary>
    int CadaDias = 30);

/// <summary>
/// Estado de pago de una venta financiada, tal como lo pidió el cliente:
/// "Total por pagar > lo pendiente > cantidad de plazos > lo pagado".
/// </summary>
public record EstadoFinanciamiento(
    long VentaId,
    string Codigo,
    TipoVenta Tipo,
    decimal Precio,
    decimal Inicial,
    /// <summary>Precio − inicial: lo que se reparte en plazos.</summary>
    decimal TotalAPlazos,
    decimal Pagado,
    int CantidadPlazos,
    int PlazosPagados,
    int PlazosAtrasados,
    DateOnly? FechaLimite,
    IReadOnlyList<VentaPlazo> Plazos)
{
    /// <summary>Lo que falta por cobrar de los plazos.</summary>
    public decimal Pendiente => Math.Max(0m, TotalAPlazos - Pagado);

    /// <summary>Total efectivamente recibido: inicial + abonos a plazos.</summary>
    public decimal RecibidoTotal => Inicial + Pagado;

    public bool EstaSaldada => Pendiente <= 0m;

    /// <summary>Separación vencida: pasó la fecha límite y aún debe (derecho de 15 días).</summary>
    public bool SeparacionVencida(DateOnly hoy) =>
        Tipo == TipoVenta.Separacion && FechaLimite is { } limite && hoy > limite && !EstaSaldada;
}
