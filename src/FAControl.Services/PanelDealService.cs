using FAControl.Common;
using FAControl.Data;
using FAControl.Models;

namespace FAControl.Services;

/// <summary>
/// Panel principal de DealControl (pedido 2026-07-25). Exige el permiso
/// 'panel' — el Vendedor no lo tiene (el panel muestra totales y ganancias).
/// </summary>
public class PanelDealService
{
    private readonly PanelDealRepository _repositorio;

    public PanelDealService(PanelDealRepository repositorio) => _repositorio = repositorio;

    public Task<ResumenPanelDeal> ObtenerResumenAsync(CancellationToken ct = default)
    {
        if (!SesionActual.TienePermiso(Permisos.Panel))
            throw new UnauthorizedAccessException("No tenés permiso para ver el panel del dealer.");
        return _repositorio.ObtenerResumenAsync(ct);
    }
}
