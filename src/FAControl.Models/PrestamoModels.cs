namespace FAControl.Models;

/// <summary>Datos que el usuario captura en el wizard "Nuevo préstamo".</summary>
public record NuevoPrestamo(
    long ClienteId,
    decimal MontoCapital,
    decimal TasaInteresMensual,
    int PlazoCuotas,
    Modalidad Modalidad,
    MetodoAmortizacion Metodo,
    DateOnly FechaPrimerPago,
    string? Garantia,
    string? Notas,
    /// <summary>AutoControl: vehículo en garantía. NULL = préstamo personal (PrestControl).</summary>
    long? VehiculoId = null,
    /// <summary>Comprobante fiscal pegado a mano (Facturador Gratuito DGII). NULL = sin comprobante.</summary>
    string? Ncf = null,
    /// <summary>True = tomar el siguiente NCF de la secuencia configurada (ignora <see cref="Ncf"/>).</summary>
    bool AsignarNcfAuto = false,
    /// <summary>
    /// Préstamo ANTIGUO con fecha atrasada (pedido 2026-07-25): las primeras N
    /// cuotas nacen pagadas, con recibos históricos fechados en su vencimiento.
    /// </summary>
    int CuotasPagadasAlCrear = 0,
    /// <summary>
    /// Solo para <see cref="MetodoAmortizacion.CapitalDiferido"/> (2026-08-06):
    /// primera cuota que ya lleva abono a capital (modo manual). NULL = que lo
    /// decida el sistema (modo automático).
    /// </summary>
    int? CuotaInicioCapital = null);

/// <summary>
/// Fila de la lista de préstamos: préstamo + cliente + agregados de sus cuotas
/// calculados en SQL (una sola consulta para toda la lista).
/// </summary>
public record PrestamoResumen(
    long Id,
    string Codigo,
    long ClienteId,
    string ClienteNombre,
    decimal MontoCapital,
    decimal TasaInteres,
    int PlazoCuotas,
    Modalidad Modalidad,
    MetodoAmortizacion Metodo,
    DateOnly FechaInicio,
    EstadoPrestamo Estado,
    decimal TotalAPagar,
    decimal TotalPagado,
    int CuotasPagadas,
    DateOnly? ProximoVencimiento)
{
    public decimal SaldoPendiente => TotalAPagar - TotalPagado;
}

/// <summary>
/// Correccion de un prestamo ya registrado (029). El cliente lo pidio para
/// arreglar errores de digitacion, no para renegociar: renegociar es cancelar
/// y volver a prestar, que deja rastro de las dos cosas.
///
/// Que se puede tocar depende de si ya hubo cobros — ver
/// <see cref="EdicionPermitida"/> y PrestamoService.EditarAsync.
/// </summary>
public record EdicionPrestamo(
    long PrestamoId,
    decimal MontoCapital,
    decimal TasaInteresMensual,
    int PlazoCuotas,
    Modalidad Modalidad,
    MetodoAmortizacion Metodo,
    DateOnly FechaPrimerPago,
    string? Garantia,
    string? Notas,
    /// <summary>Por que se corrige. Va a la auditoria; obligatorio.</summary>
    string Motivo,
    /// <summary>
    /// Solo para <see cref="MetodoAmortizacion.CapitalDiferido"/>: primera cuota
    /// con abono a capital. NULL = automatico. Corregir un prestamo diferido sin
    /// este dato lo recalcularia con la cuota sugerida en vez de la pactada.
    /// </summary>
    int? CuotaInicioCapital = null);

/// <summary>
/// Que tanto se puede corregir de un prestamo, segun si ya tiene cobros.
///
/// EL PORQUE: al registrar un cobro se emite un recibo con numero unico y se
/// entrega impreso. Ese papel afirma un monto de cuota, un plazo y un saldo. Si
/// despues se cambiaran el capital o la tasa, la tabla de amortizacion se
/// recalcula y el recibo que tiene el cliente en la mano pasa a decir algo que
/// el sistema ya no sostiene. Por eso, con un solo cobro registrado, la plata
/// queda congelada y solo se corrige lo descriptivo.
/// </summary>
/// <param name="Motivo">Explicacion para mostrar en pantalla cuando hay limites.</param>
public record EdicionPermitida(bool Todo, int CobrosRegistrados, string Motivo)
{
    /// <summary>Garantia y notas se pueden corregir siempre: no son dinero.</summary>
    public bool SoloDescriptivo => !Todo;

    public static EdicionPermitida Completa() =>
        new(true, 0, "Este préstamo todavía no tiene cobros: se puede corregir por completo.");

    public static EdicionPermitida Limitada(int cobros) => new(false, cobros,
        $"Este préstamo ya tiene {cobros} cobro(s) con recibo emitido. Los montos, el plazo y " +
        "las fechas quedan fijos —el recibo que tiene el cliente depende de ellos—; " +
        "se pueden corregir la garantía y las notas.");
}
/// <summary>
/// Como queda la cuota con los valores que el usuario esta tipeando, para la
/// vista previa del formulario de correccion. El texto lo arma el ViewModel
/// llamando al MISMO AmortizacionService que despues persiste el servicio, asi
/// lo que se ve en pantalla es exactamente lo que se va a guardar.
/// </summary>
public record VistaPreviaCuota(string Titular, string Detalle);

/// <summary>
/// Todo lo que el dialogo de correccion necesita para abrirse. Incluye el
/// delegado de vista previa porque la capa de Views NO referencia Services (lo
/// impide el grafo de proyectos, a proposito): el calculo baja del ViewModel.
/// </summary>
public record PrestamoParaEditar(
    long PrestamoId,
    string Codigo,
    Prestamo Actual,
    EdicionPermitida Permitido,
    Func<ParametrosAmortizacion, VistaPreviaCuota> Previsualizar);
