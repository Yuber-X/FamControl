using FAControl.Common;
using FAControl.Data;
using FAControl.Models;

namespace FAControl.Services;

/// <summary>
/// Expediente de contratos y reporte propio de DealControl (pedido 2026-07-25).
/// Los contratos exigen el permiso 'ventas'; el reporte, 'reportes'.
/// </summary>
public class ReporteDealService
{
    private readonly ReporteDealRepository _repositorio;
    private readonly AjustesLocales _ajustes;

    public ReporteDealService(ReporteDealRepository repositorio, AjustesLocales ajustes)
    {
        _repositorio = repositorio;
        _ajustes = ajustes;
    }

    /// <summary>
    /// Expediente de contratos del dealer, del más reciente al más viejo:
    /// VENTAS y ALQUILERES juntos (032 — "también debe mostrarse en contratos").
    ///
    /// Cada uno se consulta por su lado y se mezclan acá, ordenados por fecha.
    /// Los datos no se cruzan en la base: solo comparten pantalla.
    ///
    /// Los alquileres se incluyen solo si el usuario tiene permiso para verlos.
    /// Un vendedor al que no le habilitaron rent a car no debería enterarse de
    /// esos contratos por la puerta de atrás.
    /// </summary>
    public async Task<IReadOnlyList<ContratoDealFila>> ObtenerContratosAsync(
        CancellationToken ct = default)
    {
        if (!SesionActual.TienePermiso(Permisos.Ventas))
            throw new UnauthorizedAccessException("No tenés permiso para ver los contratos del dealer.");

        var ventas = await _repositorio.ObtenerContratosAsync(FechaNegocio.Hoy, ct);
        if (!SesionActual.TienePermiso(Permisos.Alquileres))
            return ventas;

        var alquileres = await _repositorio.ObtenerContratosDeAlquilerAsync(ct);
        return [.. ventas.Concat(alquileres).OrderByDescending(c => c.FechaUtc)];
    }

    /// <summary>
    /// Reporte del dealer en un rango. El % de comisión sale de Configuración:
    /// es una regla del negocio, no una constante de la app.
    /// </summary>
    public Task<ReporteDeal> ObtenerReporteAsync(DateOnly desde, DateOnly hasta,
        CancellationToken ct = default)
    {
        if (!SesionActual.TienePermiso(Permisos.Reportes))
            throw new UnauthorizedAccessException("No tenés permiso para ver los reportes del dealer.");
        if (hasta < desde)
            throw new ArgumentException("La fecha final no puede ser anterior a la inicial.");
        return _repositorio.ObtenerReporteAsync(desde, hasta, _ajustes.PorcentajeComisionVendedor, ct);
    }
}
