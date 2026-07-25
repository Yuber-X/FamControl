namespace FAControl.Models;

/// <summary>
/// Resultado de distribuir un monto entre cuotas: cuánto recibe cada cuota
/// y cómo se desglosa (primero interés, luego capital).
/// InteresExonerado solo aplica en liquidación anticipada: interés de cuotas
/// futuras que se perdona (la cuota queda pagada pagando solo su capital).
/// </summary>
public record AplicacionPago(
    Cuota Cuota,
    decimal MontoAplicado,
    decimal InteresAplicado,
    decimal CapitalAplicado,
    bool QuedaPagada,
    decimal InteresExonerado = 0m);

/// <summary>Solicitud de registro de pago que arma el ViewModel de Cobros.</summary>
public record SolicitudPago(
    long PrestamoId,
    decimal Monto,
    MetodoPago MetodoPago,
    string? Notas,
    bool EsLiquidacion = false,
    /// <summary>Abono a capital adicional (cliente 2026-07-17). Se aplica sobre
    /// el capital de las cuotas más próximas, exonerando su interés.</summary>
    decimal AbonoCapital = 0m);

/// <summary>Línea del recibo impreso (una por cuota afectada).</summary>
public record ReciboLinea(
    string NumeroRecibo,
    int NumeroCuota,
    decimal InteresAplicado,
    decimal CapitalAplicado,
    decimal MontoAplicado);

/// <summary>
/// Contenido completo de un recibo de pago (una operación de cobro puede
/// afectar varias cuotas; cada cuota lleva su propio numero_recibo único).
/// </summary>
public record ReciboPago(
    string NumeroReciboPrincipal,
    DateTime FechaPagoUtc,
    string ClienteNombre,
    string PrestamoCodigo,
    IReadOnlyList<ReciboLinea> Lineas,
    decimal TotalPagado,
    MetodoPago MetodoPago,
    decimal SaldoRestantePrestamo,
    decimal InteresExonerado,
    string? Notas,
    string CobradoPor,
    // Marca en el recibo (pedido del cliente 2026-07-25): nombre, RNC y
    // teléfono del negocio + número de comprobante fiscal cuando exista.
    string NegocioNombre = "",
    string NegocioRnc = "",
    string NegocioTelefono = "",
    string? Ncf = null);

/// <summary>Resultado de una operación de cobro completa.</summary>
public record ResultadoPago(
    IReadOnlyList<Pago> Pagos,
    bool PrestamoQuedoPagado,
    ReciboPago Recibo);

/// <summary>Fila del historial de pagos recientes (join pago→cuota→prestamo→cliente).</summary>
public record PagoResumen(
    long Id,
    string NumeroRecibo,
    DateTime FechaPagoUtc,
    string ClienteNombre,
    string PrestamoCodigo,
    int NumeroCuota,
    decimal MontoPagado,
    MetodoPago MetodoPago);
