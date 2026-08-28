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
    /// <summary>
    /// Comprobante fiscal del COBRO (042). Un abono puede repartirse en varios
    /// plazos y generar varias filas, pero fiscalmente es UN documento: el NCF
    /// va solo en la primera y las demas quedan en NULL.
    /// </summary>
    public string? Ncf { get; set; }
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

/// <summary>
/// Correccion de una venta ya registrada (033). Igual que en prestamos y
/// alquileres, lo que se puede tocar depende de si ya hubo cobros — ver
/// VentaPlazoService.EditarVentaAsync.
/// </summary>
public record EdicionVenta(
    long VentaId,
    decimal Precio,
    decimal Inicial,
    MetodoPago Metodo,
    string? Notas,
    /// <summary>Por que se corrige. Va a la auditoria; obligatorio.</summary>
    string Motivo,
    /// <summary>
    /// Cuantos plazos tiene el plan corregido (035). NULL = dejar los que
    /// tiene. Cambiarlo rehace el calendario repartiendo el saldo entre esa
    /// cantidad, conservando la fecha del primer vencimiento y el intervalo.
    /// </summary>
    int? CantidadPlazos = null);

/// <summary>
/// Como quedo la venta despues de corregirla (035). Lo devuelve el servicio
/// para que la pantalla pueda explicarle al usuario que paso con la plata que
/// el cliente ya habia entregado.
/// </summary>
public record ResultadoEdicionVenta(
    string Codigo,
    int CantidadPlazos,
    decimal TotalAPlazos,
    /// <summary>Lo que el cliente ya habia entregado, y que se re-imputo al plan nuevo.</summary>
    decimal YaCobrado,
    /// <summary>Cuantos plazos quedaron saldados con esa plata.</summary>
    int PlazosSaldados,
    /// <summary>
    /// Lo que sobro despues de cubrir todo el plan. Queda a favor del cliente:
    /// el sistema NO mueve plata solo (decision de Yuber, 2026-07-31).
    /// </summary>
    decimal SaldoAFavor)
{
    public bool QuedoSaldada => YaCobrado >= TotalAPlazos;
    public bool HaySaldoAFavor => SaldoAFavor > 0m;
}

/// <summary>
/// Que tanto se puede corregir de una venta, segun si ya tiene abonos.
///
/// EL PORQUE es el mismo que en prestamos: cada abono emite un recibo numerado
/// que se entrega impreso, y ese papel afirma un saldo. Si despues se cambiara
/// el precio, el calendario de plazos se recalcula y el recibo que tiene el
/// cliente pasa a decir algo que el sistema ya no sostiene.
/// </summary>
public record EdicionVentaPermitida(bool Todo, int AbonosRegistrados, string Motivo)
{
    public bool SoloDescriptivo => !Todo;

    public static EdicionVentaPermitida Completa() =>
        new(true, 0, "Esta venta todavía no tiene abonos: se puede corregir por completo.");

    public static EdicionVentaPermitida Limitada(int abonos) => new(false, abonos,
        $"Esta venta ya tiene {abonos} abono(s) con recibo emitido. El precio y la inicial " +
        "quedan fijos —el recibo que tiene el cliente depende de ellos—; se pueden corregir " +
        "el método de pago y las notas.");
}
