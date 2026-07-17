using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAControl.Common;
using FAControl.Models;
using FAControl.Services;
using Serilog;

namespace FAControl.ViewModels;

/// <summary>
/// Formulario de vehículo (nuevo y edición comparten pantalla). Los montos se
/// capturan como texto y se parsean con la cultura es-DO. Muestra en vivo el
/// costo total y la ganancia estimada. Errores de validación inline.
/// </summary>
public partial class VehiculoFormViewModel : ObservableObject
{
    private static readonly CultureInfo CulturaRd = CultureInfo.GetCultureInfo("es-DO");

    private readonly VehiculoService _servicio;
    private readonly IDialogService _dialogos;
    private long? _vehiculoId; // null = nuevo

    public event Action<long>? Guardado;
    public event Action? Cancelado;

    public VehiculoFormViewModel(VehiculoService servicio, IDialogService dialogos)
    {
        _servicio = servicio;
        _dialogos = dialogos;

        Tipos =
        [
            new Opcion<TipoVehiculo>(TipoVehiculo.Sedan, Textos.De(TipoVehiculo.Sedan)),
            new Opcion<TipoVehiculo>(TipoVehiculo.Suv, Textos.De(TipoVehiculo.Suv)),
            new Opcion<TipoVehiculo>(TipoVehiculo.Jeepeta, Textos.De(TipoVehiculo.Jeepeta)),
            new Opcion<TipoVehiculo>(TipoVehiculo.Camioneta, Textos.De(TipoVehiculo.Camioneta)),
            new Opcion<TipoVehiculo>(TipoVehiculo.Camion, Textos.De(TipoVehiculo.Camion)),
            new Opcion<TipoVehiculo>(TipoVehiculo.Motor, Textos.De(TipoVehiculo.Motor)),
            new Opcion<TipoVehiculo>(TipoVehiculo.Otro, Textos.De(TipoVehiculo.Otro))
        ];
        _tipoSeleccionado = Tipos[0];
    }

    public IReadOnlyList<Opcion<TipoVehiculo>> Tipos { get; }

    [ObservableProperty] private string _titulo = "Nuevo vehículo";
    [ObservableProperty] private string _vin = string.Empty;
    [ObservableProperty] private string _marca = string.Empty;
    [ObservableProperty] private string _modelo = string.Empty;
    [ObservableProperty] private string _anio = string.Empty;
    [ObservableProperty] private string _color = string.Empty;
    [ObservableProperty] private string _placa = string.Empty;
    [ObservableProperty] private Opcion<TipoVehiculo> _tipoSeleccionado;
    [ObservableProperty] private string _kilometraje = string.Empty;
    [ObservableProperty] private string _costoAdquisicion = string.Empty;
    [ObservableProperty] private string _gastosImportacion = string.Empty;
    [ObservableProperty] private string _precioVenta = string.Empty;
    [ObservableProperty] private string _notas = string.Empty;
    [ObservableProperty] private string _mensajeError = string.Empty;
    [ObservableProperty] private bool _ocupado;

    // Vista previa de costo/ganancia (se recalcula al teclear los montos)
    [ObservableProperty] private decimal _costoTotal;
    [ObservableProperty] private decimal _gananciaEstimada;

    partial void OnCostoAdquisicionChanged(string value) => RecalcularPreview();
    partial void OnGastosImportacionChanged(string value) => RecalcularPreview();
    partial void OnPrecioVentaChanged(string value) => RecalcularPreview();

    private void RecalcularPreview()
    {
        var costo = ParsearMonto(CostoAdquisicion) + ParsearMonto(GastosImportacion);
        CostoTotal = costo;
        GananciaEstimada = ParsearMonto(PrecioVenta) - costo;
    }

    public void PrepararNuevo()
    {
        _vehiculoId = null;
        Titulo = "Nuevo vehículo";
        Vin = Marca = Modelo = Anio = Color = Placa = Kilometraje =
            CostoAdquisicion = GastosImportacion = PrecioVenta = Notas = string.Empty;
        TipoSeleccionado = Tipos[0];
        MensajeError = string.Empty;
        RecalcularPreview();
    }

    public async Task PrepararEdicionAsync(long vehiculoId)
    {
        var v = await _servicio.ObtenerPorIdAsync(vehiculoId)
            ?? throw new InvalidOperationException("El vehículo no existe o fue eliminado.");

        _vehiculoId = vehiculoId;
        Titulo = $"Editar vehículo — {v.Codigo}";
        Vin = v.Vin ?? string.Empty;
        Marca = v.Marca;
        Modelo = v.Modelo;
        Anio = v.Anio?.ToString(CulturaRd) ?? string.Empty;
        Color = v.Color ?? string.Empty;
        Placa = v.Placa ?? string.Empty;
        TipoSeleccionado = Tipos.First(t => t.Valor == v.Tipo);
        Kilometraje = v.Kilometraje?.ToString(CulturaRd) ?? string.Empty;
        CostoAdquisicion = v.CostoAdquisicion.ToString("0.##", CulturaRd);
        GastosImportacion = v.GastosImportacion.ToString("0.##", CulturaRd);
        PrecioVenta = v.PrecioVenta.ToString("0.##", CulturaRd);
        Notas = v.Notas ?? string.Empty;
        MensajeError = string.Empty;
        RecalcularPreview();
    }

    [RelayCommand]
    private async Task GuardarAsync()
    {
        try
        {
            Ocupado = true;
            MensajeError = string.Empty;

            int? anio = null;
            if (!string.IsNullOrWhiteSpace(Anio))
            {
                if (!int.TryParse(Anio, NumberStyles.Integer, CulturaRd, out var a))
                    throw new ArgumentException("El año debe ser un número.");
                anio = a;
            }

            int? km = null;
            if (!string.IsNullOrWhiteSpace(Kilometraje))
            {
                if (!int.TryParse(Kilometraje, NumberStyles.Integer, CulturaRd, out var k) || k < 0)
                    throw new ArgumentException("El kilometraje debe ser un número válido.");
                km = k;
            }

            var datos = new VehiculoDatos(
                Vin, Marca, Modelo, anio, Color, Placa, TipoSeleccionado.Valor, km,
                ParsearMontoEstricto(CostoAdquisicion, "costo de adquisición"),
                ParsearMontoEstricto(GastosImportacion, "gastos de importación"),
                ParsearMontoEstricto(PrecioVenta, "precio de venta"),
                Notas);

            long id;
            if (_vehiculoId is null)
            {
                (id, var codigo) = await _servicio.CrearAsync(datos);
                _dialogos.Informar("Vehículo registrado", $"El vehículo {codigo} se registró en el inventario.");
            }
            else
            {
                id = _vehiculoId.Value;
                await _servicio.ActualizarAsync(id, datos);
            }

            Guardado?.Invoke(id);
        }
        catch (ArgumentException ex)
        {
            MensajeError = ex.Message;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error guardando el vehículo");
            _dialogos.MostrarError("Guardar vehículo", $"No se pudo guardar el vehículo.\n\n{ex.Message}");
        }
        finally
        {
            Ocupado = false;
        }
    }

    [RelayCommand]
    private void Cancelar() => Cancelado?.Invoke();

    /// <summary>Parseo tolerante para la vista previa (vacío o inválido = 0).</summary>
    private static decimal ParsearMonto(string texto) =>
        decimal.TryParse(texto, NumberStyles.Number, CulturaRd, out var v) && v > 0m ? v : 0m;

    /// <summary>Parseo estricto para guardar: vacío = 0, pero texto inválido lanza.</summary>
    private static decimal ParsearMontoEstricto(string texto, string campo)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return 0m;
        if (!decimal.TryParse(texto, NumberStyles.Number, CulturaRd, out var v))
            throw new ArgumentException($"El {campo} debe ser un monto válido.");
        return v;
    }
}
