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
/// fechas + tarifa. Muestra en vivo los días y el total. Al registrar,
/// el vehículo pasa a 'alquilado' (Service, atómico).
///
/// La tarifa se puede escribir por DÍA o por MES y la otra se completa sola
/// (pedido del cliente 2026-08-06): muchos alquileres se pactan hablando de
/// "tanto al mes" y el usuario venía sacando la cuenta a mano.
/// </summary>
public partial class AlquilerNuevoViewModel : ObservableObject
{
    private static readonly CultureInfo CulturaRd = CultureInfo.GetCultureInfo("es-DO");

    /// <summary>
    /// Mes comercial de 30 días. Es el mismo criterio que ya usa
    /// <see cref="AmortizacionService.TasaPorPeriodo"/> para pasar de tasa
    /// mensual a diaria, así que la suite entera cuenta los meses igual.
    /// </summary>
    private const decimal DiasPorMes = 30m;

    /// <summary>
    /// Evita el ida y vuelta entre los dos campos de tarifa. Sin esto no solo
    /// habría bucle: el número que el usuario escribió se le deformaría solo
    /// (10,000 al mes → 333.33 al día → 9,999.90 al mes).
    /// </summary>
    private bool _sincronizandoTarifa;

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
    /// <summary>Tarifa mensual. Es una comodidad de entrada: lo que se guarda siempre es la diaria.</summary>
    [ObservableProperty] private string _tarifaMesTexto = string.Empty;
    [ObservableProperty] private string _notas = string.Empty;
    [ObservableProperty] private string _mensajeError = string.Empty;
    [ObservableProperty] private bool _ocupado;

    // Vista previa de días y total
    [ObservableProperty] private int _diasPreview;
    [ObservableProperty] private decimal _totalPreview;

    partial void OnFechaInicioChanged(DateTime value) => RecalcularPreview();
    partial void OnFechaFinChanged(DateTime value) => RecalcularPreview();

    partial void OnTarifaTextoChanged(string value)
    {
        SincronizarTarifa(value, mensualEsElOrigen: false);
        RecalcularPreview();
    }

    partial void OnTarifaMesTextoChanged(string value) =>
        SincronizarTarifa(value, mensualEsElOrigen: true);

    /// <summary>
    /// Completa el campo de tarifa que el usuario NO está escribiendo.
    /// Si lo tipeado no es un número válido, el otro campo se vacía en vez de
    /// quedarse con un valor viejo que ya no corresponde.
    /// </summary>
    private void SincronizarTarifa(string texto, bool mensualEsElOrigen)
    {
        if (_sincronizandoTarifa)
            return;

        _sincronizandoTarifa = true;
        try
        {
            var hayNumero = decimal.TryParse(texto, NumberStyles.Number, CulturaRd, out var valor) && valor > 0m;

            if (mensualEsElOrigen)
                TarifaTexto = hayNumero ? Formatear(valor / DiasPorMes) : string.Empty;
            else
                TarifaMesTexto = hayNumero ? Formatear(valor * DiasPorMes) : string.Empty;
        }
        finally
        {
            _sincronizandoTarifa = false;
        }
        // El preview no se toca acá: sale de la tarifa DIARIA, y si esta cambió
        // su propio OnTarifaTextoChanged ya lo recalculó.
    }

    private static string Formatear(decimal valor) =>
        Math.Round(valor, 2, MidpointRounding.AwayFromZero).ToString("0.##", CulturaRd);

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
            TarifaTexto = TarifaMesTexto = Notas = string.Empty;
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
            // Se valida la DIARIA porque es la que se guarda; si el usuario
            // escribió la mensual, esta ya se completó sola.
            if (!decimal.TryParse(TarifaTexto, NumberStyles.Number, CulturaRd, out var tarifa) || tarifa <= 0m)
                throw new ArgumentException("Ingresá la tarifa, por día o por mes.");

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
