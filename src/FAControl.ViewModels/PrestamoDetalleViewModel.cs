using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAControl.Common;
using FAControl.Models;
using FAControl.Services;
using Serilog;

namespace FAControl.ViewModels;

/// <summary>Detalle de un préstamo: datos del contrato + tabla de cuotas con semáforo.</summary>
public partial class PrestamoDetalleViewModel : ObservableObject
{
    private readonly PrestamoService _prestamos;
    private readonly ClienteService _clientes;
    private readonly IDialogService _dialogos;
    private long _prestamoId;

    public event Action<long>? CobrarSolicitado;
    public event Action? VolverSolicitado;

    public PrestamoDetalleViewModel(PrestamoService prestamos, ClienteService clientes, IDialogService dialogos)
    {
        _prestamos = prestamos;
        _clientes = clientes;
        _dialogos = dialogos;
    }

    public ObservableCollection<CuotaFila> Cuotas { get; } = [];

    [ObservableProperty] private string _codigo = string.Empty;
    [ObservableProperty] private string _clienteNombre = string.Empty;
    [ObservableProperty] private string _estadoTexto = string.Empty;
    [ObservableProperty] private EstadoPrestamo _estado;
    [ObservableProperty] private bool _esActivo;
    [ObservableProperty] private decimal _montoCapital;
    [ObservableProperty] private string _tasaTexto = string.Empty;
    [ObservableProperty] private string _modalidadTexto = string.Empty;
    [ObservableProperty] private string _metodoTexto = string.Empty;
    [ObservableProperty] private string _fechaInicioTexto = string.Empty;
    [ObservableProperty] private string _garantiaTexto = string.Empty;
    [ObservableProperty] private string _notasTexto = string.Empty;
    [ObservableProperty] private decimal _totalAPagar;
    [ObservableProperty] private decimal _totalPagado;
    [ObservableProperty] private decimal _saldoPendiente;
    [ObservableProperty] private string _progresoTexto = string.Empty;

    public async Task CargarAsync(long prestamoId)
    {
        try
        {
            _prestamoId = prestamoId;
            var prestamo = await _prestamos.ObtenerPorIdAsync(prestamoId)
                ?? throw new InvalidOperationException($"No existe el préstamo con id {prestamoId}.");
            var cliente = await _clientes.ObtenerPorIdAsync(prestamo.ClienteId);
            var cuotas = await _prestamos.ObtenerCuotasAsync(prestamoId);

            Codigo = prestamo.Codigo;
            ClienteNombre = cliente?.NombreCompleto ?? "(cliente eliminado)";
            Estado = prestamo.Estado;
            EstadoTexto = Textos.De(prestamo.Estado);
            EsActivo = prestamo.Estado == EstadoPrestamo.Activo;
            MontoCapital = prestamo.MontoCapital;
            TasaTexto = $"{prestamo.TasaInteres:0.##}% mensual";
            ModalidadTexto = Textos.De(prestamo.Modalidad);
            MetodoTexto = Textos.De(prestamo.MetodoAmortizacion);
            FechaInicioTexto = prestamo.FechaInicio.ToString("dd/MM/yyyy");
            GarantiaTexto = string.IsNullOrWhiteSpace(prestamo.Garantia) ? "—" : prestamo.Garantia;
            NotasTexto = string.IsNullOrWhiteSpace(prestamo.Notas) ? "—" : prestamo.Notas;

            var hoy = FechaNegocio.Hoy;
            Cuotas.Clear();
            foreach (var cuota in cuotas)
                Cuotas.Add(new CuotaFila(cuota, CuotaEstadoCalculator.Calcular(cuota, hoy)));

            TotalAPagar = cuotas.Sum(c => c.MontoTotal);
            TotalPagado = cuotas.Sum(c => c.MontoPagado);
            SaldoPendiente = TotalAPagar - TotalPagado;
            ProgresoTexto = $"{cuotas.Count(c => c.Estado == EstadoCuota.Pagada)}/{cuotas.Count} cuotas pagadas";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cargando el detalle del préstamo {Id}", prestamoId);
            _dialogos.MostrarError("Detalle de préstamo", $"No se pudo cargar el préstamo.\n\n{ex.Message}");
        }
    }

    [RelayCommand]
    private void Cobrar() => CobrarSolicitado?.Invoke(_prestamoId);

    [RelayCommand]
    private void Volver() => VolverSolicitado?.Invoke();

    [RelayCommand]
    private async Task CancelarPrestamoAsync()
    {
        if (!_dialogos.Confirmar("Cancelar préstamo",
            $"¿Cancelar el préstamo {Codigo} de {ClienteNombre}?\n\n" +
            "Las cuotas sin pagar quedarán canceladas. Esta acción no se puede deshacer."))
            return;

        try
        {
            await _prestamos.CancelarAsync(_prestamoId, "Cancelado desde el detalle del préstamo");
            _dialogos.Informar("Préstamo cancelado", $"El préstamo {Codigo} fue cancelado.");
            await CargarAsync(_prestamoId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cancelando el préstamo {Id}", _prestamoId);
            _dialogos.MostrarError("Cancelar préstamo", $"No se pudo cancelar el préstamo.\n\n{ex.Message}");
        }
    }
}
