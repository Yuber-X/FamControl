using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAControl.Common;
using FAControl.Models;
using FAControl.Services;
using Serilog;

namespace FAControl.ViewModels;

/// <summary>Fila de vehículo comprado o alquilado por el cliente.</summary>
public record VehiculoClienteFila(VehiculoDeCliente Vehiculo)
{
    public long VehiculoId => Vehiculo.VehiculoId;
    public string Tipo => Vehiculo.Tipo;
    public string Codigo => Vehiculo.Codigo;
    public string FechaTexto =>
        FechaNegocio.AUtcLocal(Vehiculo.FechaUtc).ToString(Textos.FormatoFecha, Textos.CulturaRd);
    public string Descripcion => Vehiculo.Descripcion;
    public string MatriculaTexto => string.IsNullOrWhiteSpace(Vehiculo.Matricula) ? "—" : Vehiculo.Matricula;
    public string ChasisTexto => string.IsNullOrWhiteSpace(Vehiculo.Chasis) ? "—" : Vehiculo.Chasis;
    public string ColorTexto => string.IsNullOrWhiteSpace(Vehiculo.Color) ? "—" : Vehiculo.Color;
    public string EstadoTexto => Vehiculo.EstadoTexto;
    public decimal Monto => Vehiculo.Monto;
    public decimal Pendiente => Vehiculo.Pendiente;
    public bool DebeAlgo => Vehiculo.Pendiente > 0m;
}

/// <summary>
/// Ficha de cliente de DEALCONTROL (pedido 2026-07-27). La ficha de
/// PrestControl no sirve acá: hablaba de "Total prestado" y "Préstamos
/// activos", que en un dealer no existen. Métricas propias:
///  * TOTAL TRANSFERIDO — lo que el cliente negoció (compras + alquileres)
///  * TOTAL COBRADO     — lo que ya entró por eso
///  * SALDO PENDIENTE   — lo que falta (plazos o alquiler en curso)
///  * VEHÍCULOS         — cuántos compró y cuántos alquiló
/// y el grid muestra SUS vehículos, con botón para abrir la ficha completa.
/// </summary>
public partial class ClienteFichaDealViewModel : ObservableObject
{
    private readonly ClienteService _clientes;
    private readonly ClienteDealService _deal;
    private readonly IDialogService _dialogos;
    private long _clienteId;

    public event Action<long>? EditarSolicitado;
    public event Action<long>? FichaVehiculoSolicitada;
    public event Action? VolverSolicitado;

    public ClienteFichaDealViewModel(ClienteService clientes, ClienteDealService deal,
        IDialogService dialogos)
    {
        _clientes = clientes;
        _deal = deal;
        _dialogos = dialogos;
    }

    public ObservableCollection<VehiculoClienteFila> Vehiculos { get; } = [];

    [ObservableProperty] private string _nombreCompleto = string.Empty;
    [ObservableProperty] private string _cedula = string.Empty;
    [ObservableProperty] private string _telefonoTexto = string.Empty;
    [ObservableProperty] private string _direccionTexto = string.Empty;
    [ObservableProperty] private string _emailTexto = string.Empty;
    [ObservableProperty] private string _notasTexto = string.Empty;
    [ObservableProperty] private string _clienteDesdeTexto = string.Empty;

    [ObservableProperty] private decimal _totalTransferido;
    [ObservableProperty] private decimal _totalCobrado;
    [ObservableProperty] private decimal _saldoPendiente;
    [ObservableProperty] private int _vehiculosComprados;
    [ObservableProperty] private string _vehiculosTexto = string.Empty;
    [ObservableProperty] private int _plazosAtrasados;
    [ObservableProperty] private string _atrasosTexto = string.Empty;
    [ObservableProperty] private bool _tieneVehiculos;
    [ObservableProperty] private bool _sinVehiculos = true;

    public bool PuedeEditar => SesionActual.TienePermiso(Permisos.ClientesEditar);

    public async Task CargarAsync(long clienteId)
    {
        try
        {
            _clienteId = clienteId;
            var cliente = await _clientes.ObtenerPorIdAsync(clienteId)
                ?? throw new InvalidOperationException("El cliente no existe o fue eliminado.");
            var metricas = await _deal.ObtenerMetricasAsync(clienteId);
            var vehiculos = await _deal.ObtenerVehiculosAsync(clienteId);

            NombreCompleto = cliente.NombreCompleto;
            Cedula = cliente.Cedula;
            TelefonoTexto = cliente.Telefono ?? "—";
            DireccionTexto = cliente.Direccion ?? "—";
            EmailTexto = cliente.Email ?? "—";
            NotasTexto = string.IsNullOrWhiteSpace(cliente.Notas) ? "—" : cliente.Notas;
            ClienteDesdeTexto = FechaNegocio.AUtcLocal(cliente.CreatedAtUtc)
                .ToString(Textos.FormatoFecha, Textos.CulturaRd);

            TotalTransferido = metricas.TotalTransferido;
            TotalCobrado = metricas.TotalCobrado;
            SaldoPendiente = metricas.SaldoPendiente;
            VehiculosComprados = metricas.VehiculosComprados;
            VehiculosTexto = metricas.VehiculosAlquilados == 1
                ? "1 alquilado"
                : $"{metricas.VehiculosAlquilados} alquilados";
            PlazosAtrasados = metricas.PlazosAtrasados;
            AtrasosTexto = metricas.PlazosAtrasados == 0
                ? "Está al día"
                : metricas.PlazosAtrasados == 1 ? "1 plazo vencido" : $"{metricas.PlazosAtrasados} plazos vencidos";

            Vehiculos.Clear();
            foreach (var vehiculo in vehiculos)
                Vehiculos.Add(new VehiculoClienteFila(vehiculo));
            TieneVehiculos = Vehiculos.Count > 0;
            SinVehiculos = !TieneVehiculos;

            OnPropertyChanged(nameof(PuedeEditar));
        }
        catch (UnauthorizedAccessException ex)
        {
            _dialogos.MostrarError("Ficha de cliente", ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cargando la ficha de dealer del cliente {Id}", clienteId);
            _dialogos.MostrarError("Ficha de cliente", $"No se pudo cargar la ficha.\n\n{ex.Message}");
        }
    }

    [RelayCommand]
    private void Editar() => EditarSolicitado?.Invoke(_clienteId);

    [RelayCommand]
    private void Volver() => VolverSolicitado?.Invoke();

    /// <summary>Abre la ficha COMPLETA del vehículo (pedido 2026-07-27).</summary>
    [RelayCommand]
    private void VerFicha(VehiculoClienteFila? fila)
    {
        if (fila is not null)
            FichaVehiculoSolicitada?.Invoke(fila.VehiculoId);
    }
}
