using FAControl.Common;
using FAControl.Data;
using FAControl.Models;

namespace FAControl.Services;

/// <summary>
/// Panel de control: calcula los límites del mes de negocio (RD, UTC-4)
/// y delega las consultas agregadas al repositorio.
/// </summary>
public class DashboardService
{
    /// <summary>RD es UTC-4 fijo (sin horario de verano).</summary>
    private const int OffsetRdHoras = 4;

    private readonly DashboardRepository _repositorio;

    public DashboardService(DashboardRepository repositorio) => _repositorio = repositorio;

    public Task<DashboardDatos> ObtenerAsync(CancellationToken ct = default)
    {
        var hoy = FechaNegocio.Hoy;
        var inicioMesLocal = new DateTime(hoy.Year, hoy.Month, 1);
        var inicioMesSiguienteLocal = inicioMesLocal.AddMonths(1);
        var inicioMesAnteriorLocal = inicioMesLocal.AddMonths(-1);

        return _repositorio.ObtenerAsync(
            hoy,
            inicioMesLocal.AddHours(OffsetRdHoras),           // local → UTC
            inicioMesSiguienteLocal.AddHours(OffsetRdHoras),
            inicioMesAnteriorLocal.AddHours(OffsetRdHoras),
            ct);
    }
}
