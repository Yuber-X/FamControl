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
/// Formulario de nuevo alquiler (rent a car): vehículo disponible + cliente +
/// fechas + tarifa por día. Muestra en vivo los días y el total. Al registrar,
/// el vehículo pasa a 'alquilado' (Service, atómico).
/// </summary>
public partial class AlquilerNuevoViewModel : ObservableObject
{
    private static readonly CultureInfo CulturaRd = CultureInfo.GetCultureInfo("es-DO");

    private readonly AlquilerService _alquileres;
    private readonly VehiculoService _vehiculos;
    private readonly ClienteService _clientes;
    private readonly IDialogService _dialogos;

    public event Action? Registrado;
    public event Action? Cancelado;

    public AlquilerNuevoViewModel(AlquilerService alquileres, VehiculoService vehiculos,
        ClienteService clientes, IDialogService dialogos)
    {
        _alquileres = alquileres;
        _vehiculos = vehiculos;
        _clientes = clientes;
        _dialogos = dialogos;

        _fechaInicio = FechaNegocio.Hoy.ToDateTime(TimeOnly.MinValue);
        _fechaFin = FechaNegocio.Hoy.AddDays(1).ToDateTime(TimeOnly.MinValue);
    }

    public ObservableCollection<VehiculoResumen> Vehiculos { get; } = [];
    public ObservableCollection<Cliente> Clientes { get; } = [];

    [ObservableProperty] private VehiculoResumen? _vehiculoSeleccionado;
    [ObservableProperty] private Cliente? _clienteSeleccionado;
    [ObservableProperty] private DateTime _fechaInicio;
    [ObservableProperty] private DateTime _fechaFin;
    [ObservableProperty] private string _tarifaTexto = string.Empty;
    [ObservableProperty] private string _notas = string.Empty;
    [ObservableProperty] private string _mensajeError = string.Empty;
    [ObservableProperty] private bool _ocupado;

    // Vista previa de días y total
    [ObservableProperty] private int _diasPreview;
    [ObservableProperty] private decimal _totalPreview;

    partial void OnFechaInicioChanged(DateTime value) => RecalcularPreview();
    partial void OnFechaFinChanged(DateTime value) => RecalcularPreview();
    partial void OnTarifaTextoChanged(string value) => RecalcularPreview();

    private void RecalcularPreview()
    {
        var dias = AlquilerService.CalcularDias(DateOnly.FromDateTime(FechaInicio), DateOnly.FromDateTime(FechaFin));
        DiasPreview = dias;
        TotalPreview = decimal.TryParse(TarifaTexto, NumberStyles.Number, CulturaRd, out var t) && t > 0m
            ? Math.Round(t * dias, 2, MidpointRounding.AwayFromZero)
            : 0m;
    }

    public async Task CargarAsync()
    {
        try
        {
            MensajeError = string.Empty;
            VehiculoSeleccionado = null;
            ClienteSeleccionado = null;
            TarifaTexto = Notas = string.Empty;
            FechaInicio = FechaNegocio.Hoy.ToDateTime(TimeOnly.MinValue);
            FechaFin = FechaNegocio.Hoy.AddDays(1).ToDateTime(TimeOnly.MinValue);

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
            Log.Error(ex, "Error preparando el alquiler");
            _dialogos.MostrarError("Nuevo alquiler", $"No se pudo preparar el alquiler.\n\n{ex.Message}");
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
                throw new ArgumentException("Elegí el vehículo a alquilar.");
            if (ClienteSeleccionado is null)
                throw new ArgumentException("Elegí el cliente.");
            if (!decimal.TryParse(TarifaTexto, NumberStyles.Number, CulturaRd, out var tarifa) || tarifa <= 0m)
                throw new ArgumentException("Ingresá una tarifa por día válida.");

            var datos = new AlquilerDatos(
                VehiculoSeleccionado.Id, ClienteSeleccionado.Id,
                DateOnly.FromDateTime(FechaInicio), DateOnly.FromDateTime(FechaFin),
                tarifa, Notas);

            var (_, codigo) = await _alquileres.RegistrarAsync(datos);
            _dialogos.Informar("Alquiler registrado",
                $"Alquiler {codigo}: {VehiculoSeleccionado.Descripcion} para {ClienteSeleccionado.NombreCompleto}.");
            Registrado?.Invoke();
        }
        catch (ArgumentException ex)
        {
            MensajeError = ex.Message;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error registrando el alquiler");
            _dialogos.MostrarError("Nuevo alquiler", $"No se pudo registrar el alquiler.\n\n{ex.Message}");
        }
        finally
        {
            Ocupado = false;
        }
    }

    [RelayCommand]
    private void Cancelar() => Cancelado?.Invoke();
}
