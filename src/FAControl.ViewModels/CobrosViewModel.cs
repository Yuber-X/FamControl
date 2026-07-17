using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAControl.Common;
using FAControl.Models;
using FAControl.Services;
using Serilog;

namespace FAControl.ViewModels;

/// <summary>
/// Registro de cobros: seleccionar préstamo activo → ver cuotas pendientes con
/// semáforo → capturar monto (con preview de cómo se distribuye) → registrar.
/// El recibo resultante se notifica con el evento PagoRegistrado (la View abre
/// la ventana de recibo — lógica de UI, no de negocio).
/// </summary>
public partial class CobrosViewModel : ObservableObject
{
    private static readonly CultureInfo CulturaRd = CultureInfo.GetCultureInfo("es-DO");

    private readonly PagoService _pagos;
    private readonly PrestamoService _prestamos;
    private readonly IDialogService _dialogos;
    private IReadOnlyList<Cuota> _cuotasImpagas = [];

    public event Action<ReciboPago>? PagoRegistrado;

    public CobrosViewModel(PagoService pagos, PrestamoService prestamos, IDialogService dialogos)
    {
        _pagos = pagos;
        _prestamos = prestamos;
        _dialogos = dialogos;

        Metodos =
        [
            new Opcion<MetodoPago>(MetodoPago.Efectivo, Textos.De(MetodoPago.Efectivo)),
            new Opcion<MetodoPago>(MetodoPago.Transferencia, Textos.De(MetodoPago.Transferencia)),
            new Opcion<MetodoPago>(MetodoPago.Cheque, Textos.De(MetodoPago.Cheque)),
            new Opcion<MetodoPago>(MetodoPago.Otro, Textos.De(MetodoPago.Otro))
        ];
        _metodoSeleccionado = Metodos[0];
    }

    // ---------- Selección de préstamo ----------

    public ObservableCollection<PrestamoResumen> PrestamosActivos { get; } = [];
    public IReadOnlyList<Opcion<MetodoPago>> Metodos { get; }

    [ObservableProperty] private PrestamoResumen? _prestamoSeleccionado;
    [ObservableProperty] private bool _cargando;

    // ---------- Cuotas y captura ----------

    public ObservableCollection<CuotaFila> CuotasPendientes { get; } = [];
    public ObservableCollection<AplicacionFila> PreviewDistribucion { get; } = [];
    public ObservableCollection<PagoFila> PagosRecientes { get; } = [];

    [ObservableProperty] private string _montoTexto = string.Empty;
    [ObservableProperty] private bool _esLiquidacion;
    [ObservableProperty] private Opcion<MetodoPago> _metodoSeleccionado;
    [ObservableProperty] private string _notas = string.Empty;
    [ObservableProperty] private string _mensajeValidacion = string.Empty;
    [ObservableProperty] private bool _tienePreview;
    [ObservableProperty] private decimal _deudaTotal;
    [ObservableProperty] private decimal _saldoProximaCuota;
    [ObservableProperty] private decimal _montoLiquidacion;
    [ObservableProperty] private bool _tienePrestamo;

    partial void OnPrestamoSeleccionadoChanged(PrestamoResumen? value) =>
        _ = CargarCuotasAsync(); // fire-and-forget: CargarCuotasAsync captura sus propias excepciones

    partial void OnMontoTextoChanged(string value) => RecalcularPreview();

    partial void OnEsLiquidacionChanged(bool value)
    {
        if (value)
            MontoTexto = MontoLiquidacion.ToString("0.##", CulturaRd);
        RecalcularPreview();
    }

    public async Task CargarAsync(long? preseleccionarPrestamoId = null)
    {
        try
        {
            Cargando = true;
            var resumenes = await _prestamos.ObtenerResumenesAsync();
            PrestamosActivos.Clear();
            foreach (var resumen in resumenes.Where(r => r.Estado == EstadoPrestamo.Activo))
                PrestamosActivos.Add(resumen);

            PrestamoSeleccionado = preseleccionarPrestamoId is null
                ? PrestamoSeleccionado is null ? null : PrestamosActivos.FirstOrDefault(p => p.Id == PrestamoSeleccionado.Id)
                : PrestamosActivos.FirstOrDefault(p => p.Id == preseleccionarPrestamoId);

            await CargarPagosRecientesAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cargando la pantalla de cobros");
            _dialogos.MostrarError("Cobros", $"No se pudo cargar la pantalla de cobros.\n\n{ex.Message}");
        }
        finally
        {
            Cargando = false;
        }
    }

    private async Task CargarCuotasAsync()
    {
        CuotasPendientes.Clear();
        PreviewDistribucion.Clear();
        _cuotasImpagas = [];
        TienePrestamo = PrestamoSeleccionado is not null;
        TienePreview = false;
        MensajeValidacion = string.Empty;
        DeudaTotal = SaldoProximaCuota = MontoLiquidacion = 0m;
        EsLiquidacion = false;
        MontoTexto = string.Empty;

        if (PrestamoSeleccionado is null)
            return;

        try
        {
            var hoy = FechaNegocio.Hoy;
            var cuotas = await _prestamos.ObtenerCuotasAsync(PrestamoSeleccionado.Id);
            _cuotasImpagas = cuotas
                .Where(c => c.Estado is EstadoCuota.Pendiente or EstadoCuota.Vencida or EstadoCuota.EnMora)
                .OrderBy(c => c.NumeroCuota)
                .ToList();

            foreach (var cuota in _cuotasImpagas)
                CuotasPendientes.Add(new CuotaFila(cuota, CuotaEstadoCalculator.Calcular(cuota, hoy)));

            DeudaTotal = _cuotasImpagas.Sum(c => c.SaldoPendiente);
            SaldoProximaCuota = _cuotasImpagas.FirstOrDefault()?.SaldoPendiente ?? 0m;
            MontoLiquidacion = PagoService.CalcularLiquidacion(_cuotasImpagas, hoy);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cargando cuotas del préstamo {Id}", PrestamoSeleccionado.Id);
            _dialogos.MostrarError("Cobros", $"No se pudieron cargar las cuotas.\n\n{ex.Message}");
        }
    }

    private async Task CargarPagosRecientesAsync()
    {
        var recientes = await _pagos.ObtenerRecientesAsync(15);
        PagosRecientes.Clear();
        foreach (var pago in recientes)
            PagosRecientes.Add(new PagoFila(pago));
    }

    private void RecalcularPreview()
    {
        PreviewDistribucion.Clear();
        TienePreview = false;
        MensajeValidacion = string.Empty;

        if (_cuotasImpagas.Count == 0 || string.IsNullOrWhiteSpace(MontoTexto))
        {
            RegistrarCommand.NotifyCanExecuteChanged();
            return;
        }

        try
        {
            var aplicaciones = EsLiquidacion
                ? PagoService.DistribuirLiquidacion(_cuotasImpagas, FechaNegocio.Hoy)
                : PagoService.DistribuirPago(ParsearMonto(), _cuotasImpagas);

            foreach (var aplicacion in aplicaciones)
                PreviewDistribucion.Add(new AplicacionFila(aplicacion));
            TienePreview = true;
        }
        catch (ArgumentException ex)
        {
            MensajeValidacion = ex.Message;
        }

        RegistrarCommand.NotifyCanExecuteChanged();
    }

    private decimal ParsearMonto()
    {
        if (!decimal.TryParse(MontoTexto, NumberStyles.Number, CulturaRd, out var monto))
            throw new ArgumentException("Ingresá un monto válido (ej. 1,600.00).");
        return monto;
    }

    // ---------- Atajos ----------

    [RelayCommand]
    private void UsarCuotaCompleta()
    {
        EsLiquidacion = false;
        MontoTexto = SaldoProximaCuota.ToString("0.##", CulturaRd);
    }

    [RelayCommand]
    private void UsarLiquidacion() => EsLiquidacion = true;

    // ---------- Registro ----------

    private bool PuedeRegistrar() => TienePreview && PrestamoSeleccionado is not null;

    [RelayCommand(CanExecute = nameof(PuedeRegistrar))]
    private async Task RegistrarAsync()
    {
        if (PrestamoSeleccionado is null)
            return;

        try
        {
            var solicitud = new SolicitudPago(
                PrestamoSeleccionado.Id,
                EsLiquidacion ? MontoLiquidacion : ParsearMonto(),
                MetodoSeleccionado.Valor,
                string.IsNullOrWhiteSpace(Notas) ? null : Notas.Trim(),
                EsLiquidacion);

            var resultado = await _pagos.RegistrarPagoAsync(solicitud);

            if (resultado.PrestamoQuedoPagado)
                _dialogos.Informar("Préstamo saldado",
                    $"Con este cobro el préstamo {PrestamoSeleccionado.Codigo} quedó completamente pagado.");

            Notas = string.Empty;
            PagoRegistrado?.Invoke(resultado.Recibo);
            await CargarAsync(resultado.PrestamoQuedoPagado ? null : PrestamoSeleccionado?.Id);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error registrando el cobro");
            _dialogos.MostrarError("Registrar pago", $"No se pudo registrar el pago.\n\n{ex.Message}");
        }
    }
}
