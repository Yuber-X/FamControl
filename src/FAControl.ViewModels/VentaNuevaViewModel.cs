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
/// Formulario de venta al contado (DealerControl): elegir un vehículo disponible
/// y un cliente, fijar el precio y el método de pago. Al registrar, el vehículo
/// pasa a 'vendido' (Service, atómico).
/// </summary>
public partial class VentaNuevaViewModel : ObservableObject
{
    private static readonly CultureInfo CulturaRd = CultureInfo.GetCultureInfo("es-DO");

    private readonly VentaVehiculoService _ventas;
    private readonly VehiculoService _vehiculos;
    private readonly ClienteService _clientes;
    private readonly IDialogService _dialogos;

    public event Action? Registrado;
    public event Action? Cancelado;

    public VentaNuevaViewModel(VentaVehiculoService ventas, VehiculoService vehiculos,
        ClienteService clientes, IDialogService dialogos)
    {
        _ventas = ventas;
        _vehiculos = vehiculos;
        _clientes = clientes;
        _dialogos = dialogos;

        MetodosPago =
        [
            new Opcion<MetodoPago>(MetodoPago.Efectivo, Textos.De(MetodoPago.Efectivo)),
            new Opcion<MetodoPago>(MetodoPago.Transferencia, Textos.De(MetodoPago.Transferencia)),
            new Opcion<MetodoPago>(MetodoPago.Cheque, Textos.De(MetodoPago.Cheque)),
            new Opcion<MetodoPago>(MetodoPago.Otro, Textos.De(MetodoPago.Otro))
        ];
        _metodoSeleccionado = MetodosPago[0];
    }

    public ObservableCollection<VehiculoResumen> Vehiculos { get; } = [];
    public ObservableCollection<Cliente> Clientes { get; } = [];
    public IReadOnlyList<Opcion<MetodoPago>> MetodosPago { get; }

    [ObservableProperty] private VehiculoResumen? _vehiculoSeleccionado;
    [ObservableProperty] private Cliente? _clienteSeleccionado;
    [ObservableProperty] private string _precioTexto = string.Empty;
    [ObservableProperty] private Opcion<MetodoPago> _metodoSeleccionado;
    [ObservableProperty] private string _notas = string.Empty;
    [ObservableProperty] private string _mensajeError = string.Empty;
    [ObservableProperty] private bool _ocupado;

    partial void OnVehiculoSeleccionadoChanged(VehiculoResumen? value)
    {
        if (value is not null && string.IsNullOrWhiteSpace(PrecioTexto))
            PrecioTexto = value.PrecioVenta.ToString("0.##", CulturaRd);
    }

    public async Task CargarAsync()
    {
        try
        {
            MensajeError = string.Empty;
            VehiculoSeleccionado = null;
            ClienteSeleccionado = null;
            PrecioTexto = Notas = string.Empty;
            MetodoSeleccionado = MetodosPago[0];

            var vehiculos = await _vehiculos.ObtenerResumenesAsync();
            Vehiculos.Clear();
            foreach (var v in vehiculos.Where(v => v.Estado == EstadoVehiculo.Disponible))
                Vehiculos.Add(v);

            var clientes = await _clientes.ObtenerActivosAsync();
            Clientes.Clear();
            foreach (var c in clientes)
                Clientes.Add(c);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error preparando la venta al contado");
            _dialogos.MostrarError("Nueva venta", $"No se pudo preparar la venta.\n\n{ex.Message}");
        }
    }

    [RelayCommand]
    private async Task GuardarAsync()
    {
        try
        {
            Ocupado = true;
            MensajeError = string.Empty;

            if (VehiculoSeleccionado is null)
                throw new ArgumentException("Elegí el vehículo a vender.");
            if (ClienteSeleccionado is null)
                throw new ArgumentException("Elegí el cliente comprador.");
            if (!decimal.TryParse(PrecioTexto, NumberStyles.Number, CulturaRd, out var precio) || precio <= 0m)
                throw new ArgumentException("Ingresá un precio válido mayor que cero.");

            var datos = new VentaVehiculoDatos(
                VehiculoSeleccionado.Id, ClienteSeleccionado.Id, precio,
                MetodoSeleccionado.Valor, Notas);

            var (_, codigo) = await _ventas.RegistrarAsync(datos);
            _dialogos.Informar("Venta registrada",
                $"Venta {codigo}: {VehiculoSeleccionado.Descripcion} vendido a {ClienteSeleccionado.NombreCompleto}.");
            Registrado?.Invoke();
        }
        catch (ArgumentException ex)
        {
            MensajeError = ex.Message;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error registrando la venta");
            _dialogos.MostrarError("Nueva venta", $"No se pudo registrar la venta.\n\n{ex.Message}");
        }
        finally
        {
            Ocupado = false;
        }
    }

    [RelayCommand]
    private void Cancelar() => Cancelado?.Invoke();
}
