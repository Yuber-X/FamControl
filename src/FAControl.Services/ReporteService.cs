using FAControl.Data;
using FAControl.Models;

namespace FAControl.Services;

/// <summary>
/// Reporte "Ingresos por período" (mockup Reportes): totales del rango,
/// cuotas cobradas vs programadas y desglose por semanas de 7 días
/// contadas desde la fecha inicial.
/// </summary>
public class ReporteService
{
    /// <summary>RD es UTC-4 fijo (sin horario de verano).</summary>
    private const int OffsetRdHoras = 4;

    private readonly ReporteRepository _repositorio;

    public ReporteService(ReporteRepository repositorio) => _repositorio = repositorio;

    public async Task<ReporteIngresos> ObtenerIngresosAsync(DateOnly desde, DateOnly hasta,
        CancellationToken ct = default)
    {
        if (hasta < desde)
            throw new ArgumentException("La fecha final no puede ser anterior a la inicial.");

        // Rango local [desde, hasta] → instantes UTC [inicio, fin)
        var inicioUtc = desde.ToDateTime(TimeOnly.MinValue).AddHours(OffsetRdHoras);
        var finUtc = hasta.AddDays(1).ToDateTime(TimeOnly.MinValue).AddHours(OffsetRdHoras);

        var porDia = await _repositorio.ObtenerIngresosDiariosAsync(inicioUtc, finUtc, ct);
        var (cobradas, programadas) = await _repositorio.ContarCuotasAsync(inicioUtc, finUtc, desde, hasta, ct);

        return new ReporteIngresos(
            desde, hasta,
            porDia.Sum(d => d.Interes),
            porDia.Sum(d => d.Capital),
            porDia.Sum(d => d.Total),
            cobradas, programadas,
            porDia,
            AgruparPorSemana(porDia, desde, hasta));
    }

    /// <summary>Buckets de 7 días desde la fecha inicial (Sem. 1, Sem. 2, ...).</summary>
    public static List<IngresoSemanal> AgruparPorSemana(IReadOnlyList<IngresoDiario> porDia,
        DateOnly desde, DateOnly hasta)
    {
        var semanas = new List<IngresoSemanal>();
        var numero = 1;
        for (var inicio = desde; inicio <= hasta; inicio = inicio.AddDays(7))
        {
            var fin = inicio.AddDays(6) < hasta ? inicio.AddDays(6) : hasta;
            var delRango = porDia.Where(d => d.Fecha >= inicio && d.Fecha <= fin).ToList();
            semanas.Add(new IngresoSemanal(
                numero++, inicio, fin,
                delRango.Sum(d => d.Capital),
                delRango.Sum(d => d.Interes)));
        }
        return semanas;
    }
}
