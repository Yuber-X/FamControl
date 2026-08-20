namespace FAControl.Models;

/// <summary>
/// Resultado del cálculo de amortización para una cuota.
/// Es un valor inmutable: AmortizacionService lo produce y NUNCA persiste;
/// el ViewModel decide cuándo guardar la tabla en la base de datos.
/// </summary>
public record CuotaCalculada(
    int NumeroCuota,
    DateOnly FechaVencimiento,
    decimal Capital,
    decimal Interes,
    decimal MontoTotal,
    decimal SaldoDespues);

/// <summary>Parámetros de entrada para calcular una tabla de amortización.</summary>
public record ParametrosAmortizacion(
    decimal MontoCapital,
    /// <summary>Tasa de interés MENSUAL en porcentaje (ej. 10 = 10%).</summary>
    decimal TasaInteresMensual,
    int PlazoCuotas,
    Modalidad Modalidad,
    MetodoAmortizacion Metodo,
    DateOnly FechaPrimerPago,
    /// <summary>
    /// Solo para <see cref="MetodoAmortizacion.CapitalDiferido"/>: número de la
    /// primera cuota que ya lleva abono a capital. Las anteriores son de puro
    /// interés. En el ejemplo del cliente (18 cuotas, 6 de interés fijo) vale 7.
    /// Null = que lo decida el sistema (ver <see cref="CuotaInicioCapitalSugerida"/>).
    /// </summary>
    int? CuotaInicioCapital = null);

/// <summary>Totales de una tabla de amortización calculada.</summary>
public record ResumenAmortizacion(
    decimal CuotaFija,
    decimal TotalAPagar,
    decimal InteresTotal,
    decimal Capital);
