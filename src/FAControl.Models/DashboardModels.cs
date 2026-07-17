namespace FAControl.Models;

/// <summary>Total cobrado en un día de negocio (para el gráfico de tendencia).</summary>
public record CobroDiario(DateOnly Fecha, decimal Monto);

/// <summary>
/// Cuota que requiere atención: vence pronto o ya venció.
/// El semáforo exacto lo calcula CuotaEstadoCalculator en la capa de presentación.
/// </summary>
public record AlertaCobro(
    long PrestamoId,
    string PrestamoCodigo,
    string ClienteNombre,
    int NumeroCuota,
    DateOnly FechaVencimiento,
    decimal MontoTotal,
    decimal MontoPagado)
{
    public decimal SaldoPendiente => MontoTotal - MontoPagado;
}

/// <summary>
/// Datos completos del panel de control (una sola llamada al cargar).
/// Morosidad = saldo de cuotas ya vencidas sin cubrir, de préstamos activos.
/// </summary>
public record DashboardDatos(
    decimal CapitalColocado,
    decimal CobrosDelMes,
    decimal CobrosMesAnterior,
    int ClientesActivos,
    int PrestamosActivos,
    decimal Morosidad,
    IReadOnlyList<CobroDiario> CobrosPorDia,
    IReadOnlyList<AlertaCobro> Alertas);
