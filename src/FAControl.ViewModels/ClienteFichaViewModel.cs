using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAControl.Common;
using FAControl.Services;
using Serilog;

namespace FAControl.ViewModels;

/// <summary>
/// Ficha de cliente (mockup 3): datos de contacto + cinco métricas
/// + sus préstamos. Desde aquí se edita, se elimina (soft delete protegido)
/// y se le abre un préstamo nuevo con el cliente preseleccionado.
/// </summary>
public partial class ClienteFichaViewModel : ObservableObject
{
    private readonly ClienteService _clientes;
    private readonly PrestamoService _prestamos;
    private readonly IDialogService _dialogos;
    private long _clienteId;

    public event Action<long>? EditarSolicitado;
    public event Action<long>? NuevoPrestamoSolicitado;
    public event Action<long>? PrestamoSeleccionado;
    public event Action? VolverSolicitado;

    public ClienteFichaViewModel(ClienteService clientes, PrestamoService prestamos, IDialogService dialogos)
    {
        _clientes = clientes;
        _prestamos = prestamos;
        _dialogos = dialogos;
    }

    public ObservableCollection<PrestamoFila> Prestamos { get; } = [];

    [ObservableProperty] private string _nombreCompleto = string.Empty;
    [ObservableProperty] private string _cedula = string.Empty;
    [ObservableProperty] private string _telefonoTexto = string.Empty;
    [ObservableProperty] private string _direccionTexto = string.Empty;
    [ObservableProperty] private string _emailTexto = string.Empty;
    [ObservableProperty] private string _notasTexto = string.Empty;
    [ObservableProperty] private string _clienteDesdeTexto = string.Empty;
    [ObservableProperty] private decimal _totalPrestado;
    [ObservableProperty] private decimal _totalCobrado;
    [ObservableProperty] private decimal _saldoPendiente;
    [ObservableProperty] private int _prestamosActivos;
    [ObservableProperty] private int _cuotasVencidas;
    [ObservableProperty] private bool _tienePrestamos;

    public async Task CargarAsync(long clienteId)
    {
        try
        {
            _clienteId = clienteId;
            var cliente = await _clientes.ObtenerPorIdAsync(clienteId)
                ?? throw new InvalidOperationException("El cliente no existe o fue eliminado.");
            var metricas = await _clientes.ObtenerMetricasAsync(clienteId);
            var prestamos = await _prestamos.ObtenerResumenesAsync();

            NombreCompleto = cliente.NombreCompleto;
            Cedula = cliente.Cedula;
            TelefonoTexto = cliente.Telefono ?? "—";
            DireccionTexto = cliente.Direccion ?? "—";
            EmailTexto = cliente.Email ?? "—";
            NotasTexto = string.IsNullOrWhiteSpace(cliente.Notas) ? "—" : cliente.Notas;
            ClienteDesdeTexto = FechaNegocio.AUtcLocal(cliente.CreatedAtUtc).ToString("dd/MM/yyyy");

            TotalPrestado = metricas.TotalPrestado;
            TotalCobrado = metricas.TotalCobrado;
            SaldoPendiente = metricas.SaldoPendiente;
            PrestamosActivos = metricas.PrestamosActivos;
            CuotasVencidas = metricas.CuotasVencidas;

            Prestamos.Clear();
            foreach (var resumen in prestamos.Where(p => p.ClienteId == clienteId))
                Prestamos.Add(new PrestamoFila(resumen));
            TienePrestamos = Prestamos.Count > 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cargando la ficha del cliente {Id}", clienteId);
            _dialogos.MostrarError("Ficha de cliente", $"No se pudo cargar la ficha.\n\n{ex.Message}");
        }
    }

    [RelayCommand]
    private void Editar() => EditarSolicitado?.Invoke(_clienteId);

    [RelayCommand]
    private void NuevoPrestamo() => NuevoPrestamoSolicitado?.Invoke(_clienteId);

    [RelayCommand]
    private void VerPrestamo(PrestamoFila? fila)
    {
        if (fila is not null)
            PrestamoSeleccionado?.Invoke(fila.Id);
    }

    [RelayCommand]
    private void Volver() => VolverSolicitado?.Invoke();

    [RelayCommand]
    private async Task EliminarAsync()
    {
        if (!_dialogos.Confirmar("Eliminar cliente",
            $"¿Eliminar a {NombreCompleto}?\n\n" +
            "Su historial de préstamos y pagos se conserva, pero ya no aparecerá en las listas."))
            return;

        try
        {
            await _clientes.EliminarAsync(_clienteId);
            _dialogos.Informar("Cliente eliminado", $"{NombreCompleto} fue eliminado.");
            VolverSolicitado?.Invoke();
        }
        catch (InvalidOperationException ex)
        {
            // Regla de negocio (préstamos activos): mensaje claro, sin stack
            _dialogos.MostrarError("Eliminar cliente", ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error eliminando el cliente {Id}", _clienteId);
            _dialogos.MostrarError("Eliminar cliente", $"No se pudo eliminar el cliente.\n\n{ex.Message}");
        }
    }
}
