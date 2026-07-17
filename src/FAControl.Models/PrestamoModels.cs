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
    string? Notas);

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
