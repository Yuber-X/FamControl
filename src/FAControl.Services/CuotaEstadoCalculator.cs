using FAControl.Models;

namespace FAControl.Services;

/// <summary>
/// Semáforo de cobros: estado de una cuota calculado EN TIEMPO REAL
/// (no se persiste como estado permanente).
///
/// Reglas (CLAUDE.md del proyecto):
///  - Pagada:     los pagos cubren el monto total de la cuota
///  - Al día:     vence a más de 7 días de hoy
///  - Por vencer: vence hoy o dentro de los próximos 7 días
///  - Vencida:    venció hace 1 a 15 días sin cubrirse
///  - En mora:    venció hace más de 15 días sin cubrirse
///  - Cancelada:  la cuota pertenece a un préstamo cancelado
/// </summary>
public static class CuotaEstadoCalculator
{
    private const int DiasPorVencer = 7;
    private const int DiasToleranciaMora = 15;

    /// <summary>Calcula el estado del semáforo para una cuota en la fecha de negocio indicada.</summary>
    public static SemaforoCuota Calcular(
        DateOnly fechaVencimiento,
        decimal montoTotal,
        decimal montoPagado,
        EstadoCuota estadoPersistido,
        DateOnly hoy)
    {
        if (estadoPersistido == EstadoCuota.Cancelada)
            return SemaforoCuota.Cancelada;

        if (montoPagado >= montoTotal)
            return SemaforoCuota.Pagada;

        var diasParaVencer = fechaVencimiento.DayNumber - hoy.DayNumber;

        if (diasParaVencer > DiasPorVencer)
            return SemaforoCuota.AlDia;

        if (diasParaVencer >= 0)
            return SemaforoCuota.PorVencer;

        var diasVencida = -diasParaVencer;
        return diasVencida <= DiasToleranciaMora
            ? SemaforoCuota.Vencida
            : SemaforoCuota.EnMora;
    }

    /// <summary>Sobrecarga de conveniencia para una entidad Cuota.</summary>
    public static SemaforoCuota Calcular(Cuota cuota, DateOnly hoy) =>
        Calcular(cuota.FechaVencimiento, cuota.MontoTotal, cuota.MontoPagado, cuota.Estado, hoy);
}
