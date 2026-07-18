using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAControl.Common;
using FAControl.Models;
using FAControl.Services;
using Serilog;

namespace FAControl.ViewModels;

/// <summary>Fila de un gasto de importación.</summary>
public record GastoFila(VehiculoGasto Gasto)
{
    public long Id => Gasto.Id;
    public string Concepto => Gasto.Concepto;
    public decimal Monto => Gasto.Monto;
    public string FechaTexto => Gasto.Fecha.ToString(Textos.FormatoFecha, Textos.CulturaRd);
}

/// <summary>
/// Gestión de importación (DealerControl): elegir un vehículo y administrar sus
/// gastos (aduana, flete, etc.). El total se refleja en el costo del vehículo.
/// </summary>
public partial class GastosViewModel : ObservableObject
{
    private static readonly CultureInfo CulturaRd = CultureInfo.GetCultureInfo("es-DO");

    private readonly VehiculoGastoService _gastos;
    private readonly VehiculoService _vehiculos;
    private readonly IDialogService _dialogos;

    public GastosViewModel(VehiculoGastoService gastos, VehiculoService vehiculos, IDialogService dialogos)
    {
        _gastos = gastos;
        _vehiculos = vehiculos;
        _dialogos = dialogos;
        _fechaGasto = FechaNegocio.Hoy.ToDateTime(TimeOnly.MinValue);
    }

    public ObservableCollection<VehiculoResumen> Vehiculos { get; } = [];
    public ObservableCollection<GastoFila> Filas { get; } = [];

    public bool PuedeEditar => SesionActual.TienePermiso(Permisos.VehiculosEditar);

    [ObservableProperty] private VehiculoResumen? _vehiculoSeleccionado;
    [ObservableProperty] private string _concepto = string.Empty;
    [ObservableProperty] private string _montoTexto = string.Empty;
    [ObservableProperty] private DateTime _fechaGasto;
    [ObservableProperty] private decimal _totalGastos;
    [ObservableProperty] private string _mensajeError = string.Empty;

    partial void OnVehiculoSeleccionadoChanged(VehiculoResumen? value) => _ = CargarGastosAsync();

    public async Task CargarAsync()
    {
        try
        {
            OnPropertyChanged(nameof(PuedeEditar));
            var vehiculos = await _vehiculos.ObtenerResumenesAsync();
            var sel = VehiculoSeleccionado;
            Vehiculos.Clear();
            foreach (var v in vehiculos)
                Vehiculos.Add(v);
            VehiculoSeleccionado = Vehiculos.FirstOrDefault(v => v.Id == sel?.Id) ?? Vehiculos.FirstOrDefault();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cargando vehículos para gastos");
            _dialogos.MostrarError("Gastos", $"No se pudieron cargar los vehículos.\n\n{ex.Message}");
        }
    }

    private async Task CargarGastosAsync()
    {
        Filas.Clear();
        TotalGastos = 0m;
        if (VehiculoSeleccionado is null)
            return;
        try
        {
            var gastos = await _gastos.ObtenerPorVehiculoAsync(VehiculoSeleccionado.Id);
            foreach (var g in gastos)
                Filas.Add(new GastoFila(g));
            TotalGastos = gastos.Sum(g => g.Monto);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cargando los gastos del vehículo {Id}", VehiculoSeleccionado.Id);
            _dialogos.MostrarError("Gastos", ex.Message);
        }
    }

    [RelayCommand]
    private async Task AgregarAsync()
    {
        try
        {
            MensajeError = string.Empty;
            if (VehiculoSeleccionado is null)
                throw new ArgumentException("Elegí un vehículo primero.");
            if (string.IsNullOrWhiteSpace(Concepto))
                throw new ArgumentException("Ingresá el concepto del gasto.");
            if (!decimal.TryParse(MontoTexto, NumberStyles.Number, CulturaRd, out var monto) || monto <= 0m)
                throw new ArgumentException("Ingresá un monto válido mayor que cero.");

            await _gastos.AgregarAsync(new VehiculoGastoDatos(
                VehiculoSeleccionado.Id, Concepto.Trim(), monto, DateOnly.FromDateTime(FechaGasto)));

            Concepto = MontoTexto = string.Empty;
            await CargarGastosAsync();
        }
        catch (ArgumentException ex)
        {
            MensajeError = ex.Message;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error agregando gasto");
            _dialogos.MostrarError("Gastos", ex.Message);
        }
    }

    [RelayCommand]
    private async Task EliminarAsync(GastoFila? fila)
    {
        if (fila is null)
            return;
        if (!_dialogos.Confirmar("Eliminar gasto", $"¿Eliminar el gasto '{fila.Concepto}'?"))
            return;
        try
        {
            await _gastos.EliminarAsync(fila.Id);
            await CargarGastosAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error eliminando gasto {Id}", fila.Id);
            _dialogos.MostrarError("Gastos", ex.Message);
        }
    }
}
