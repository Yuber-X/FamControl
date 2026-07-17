using System.Globalization;
using FAControl.Common;
using FAControl.Models;

namespace FAControl.Services;

/// <summary>
/// Almacén de contratos (cliente 2026-07-17): cada préstamo tiene su pagaré.
/// Este servicio compone el pagaré de un préstamo EXISTENTE a partir del
/// préstamo, su cliente, sus cuotas y los datos del negocio, para poder
/// reimprimirlo o verlo desde el almacén sin recalcular nada.
/// </summary>
public class ContratoService
{
    private static readonly CultureInfo CulturaRd = CultureInfo.GetCultureInfo("es-DO");
    private const string FormatoFecha = @"dd'/'MM'/'yyyy";

    private readonly PrestamoService _prestamos;
    private readonly ClienteService _clientes;
    private readonly AjustesLocales _ajustes;

    public ContratoService(PrestamoService prestamos, ClienteService clientes, AjustesLocales ajustes)
    {
        _prestamos = prestamos;
        _clientes = clientes;
        _ajustes = ajustes;
    }

    /// <summary>Todos los préstamos como filas del almacén de contratos.</summary>
    public Task<IReadOnlyList<PrestamoResumen>> ObtenerContratosAsync(CancellationToken ct = default) =>
        _prestamos.ObtenerResumenesAsync(ct);

    /// <summary>
    /// Reconstruye el pagaré de un préstamo existente (para verlo o reimprimirlo).
    /// Toma las cuotas TAL COMO están guardadas: el contrato refleja el préstamo
    /// original, no un recálculo.
    /// </summary>
    public async Task<PagareImpreso> ArmarPagareAsync(long prestamoId, CancellationToken ct = default)
    {
        var prestamo = await _prestamos.ObtenerPorIdAsync(prestamoId, ct)
            ?? throw new InvalidOperationException($"No existe el préstamo con id {prestamoId}.");
        var cliente = await _clientes.ObtenerPorIdAsync(prestamo.ClienteId, ct);
        var cuotas = await _prestamos.ObtenerCuotasAsync(prestamoId, ct);

        return new PagareImpreso(
            NombreNegocio: _ajustes.NombreNegocio,
            Prestamista: _ajustes.Prestamista,
            Ciudad: _ajustes.CiudadNegocio,
            Telefono: _ajustes.TelefonoNegocio,
            Email: _ajustes.EmailNegocio,
            Rnc: _ajustes.RncNegocio,
            DeudorNombre: cliente?.NombreCompleto ?? "(cliente eliminado)",
            DeudorCedula: string.IsNullOrWhiteSpace(cliente?.Cedula) ? "—" : cliente.Cedula,
            CodigoPrestamo: prestamo.Codigo,
            MontoPrestado: prestamo.MontoCapital,
            TotalAPagar: cuotas.Sum(c => c.MontoTotal),
            Cuotas: [.. cuotas.OrderBy(c => c.NumeroCuota).Select(c => new PagareCuota(
                c.NumeroCuota,
                c.FechaVencimiento.ToString(FormatoFecha, CulturaRd),
                c.MontoTotal))]);
    }
}
