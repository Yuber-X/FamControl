using FAControl.Common;
using FAControl.Data;
using FAControl.Models;

namespace FAControl.Services;

/// <summary>
/// Ficha del cliente en DealControl (pedido 2026-07-27). Exige 'clientes',
/// el mismo permiso que la lista: quien puede ver clientes puede ver su ficha.
///
/// Los montos NO se ocultan al Vendedor aquí a propósito: es lo que ese cliente
/// le debe al negocio, no el costo ni la ganancia del vehículo (eso sí está
/// restringido, en el inventario y en la ficha del vehículo).
/// </summary>
public class ClienteDealService
{
    private readonly ClienteDealRepository _repositorio;

    public ClienteDealService(ClienteDealRepository repositorio) => _repositorio = repositorio;

    private static void Exigir()
    {
        if (!SesionActual.TienePermiso(Permisos.Clientes))
            throw new UnauthorizedAccessException("No tienes permiso para ver la ficha de clientes.");
    }

    public Task<MetricasClienteDeal> ObtenerMetricasAsync(long clienteId, CancellationToken ct = default)
    {
        Exigir();
        return _repositorio.ObtenerMetricasAsync(clienteId, ct);
    }

    public Task<IReadOnlyList<VehiculoDeCliente>> ObtenerVehiculosAsync(long clienteId,
        CancellationToken ct = default)
    {
        Exigir();
        return _repositorio.ObtenerVehiculosAsync(clienteId, ct);
    }
}
