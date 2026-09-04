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
/// Formulario de venta del dealer: elegir un vehículo disponible y un cliente,
/// fijar el precio y el método de pago. Desde 2026-07-25 soporta las TRES
/// formas del dealer (016): al contado, financiada por plazos (inicial + N
/// pagos sin interés) y separación/apartado con fecha límite de derecho.
/// Al registrar, el Service marca el vehículo vendido (o reservado, si es
/// separación) de forma atómica.
/// </summary>
public partial class VentaNuevaViewModel : ObservableObject
{
    private static readonly CultureInfo CulturaRd = CultureInfo.GetCultureInfo("es-DO");

    private readonly VentaVehiculoService _ventas;
    private readonly VehiculoService _vehiculos;
    private readonly ClienteService _clientes;
    private readonly IDialogService _dialogos;

    /// <summary>
    /// Venta registrada: lleva el id para que el shell abra su financiamiento
    /// (033). Antes volvia a la lista, donde habia que buscar la venta recien
    /// hecha para seguir trabajando en ella.
    /// </summary>
    public event Action<long>? Registrado;
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

        TiposVenta =
        [
            new Opcion<TipoVenta>(TipoVenta.Contado, "Al contado"),
            new Opcion<TipoVenta>(TipoVenta.Plazos, "Financiada por plazos"),
            new Opcion<TipoVenta>(TipoVenta.Separacion, "Separación / apartado")
        ];
        _tipoSeleccionado = TiposVenta[0];
        _fechaPrimerPlazo = FechaNegocio.Hoy.AddMonths(1).ToDateTime(TimeOnly.MinValue);
    }

    public ObservableCollection<VehiculoResumen> Vehiculos { get; } = [];
    public ObservableCollection<Cliente> Clientes { get; } = [];
    public IReadOnlyList<Opcion<MetodoPago>> MetodosPago { get; }
    public IReadOnlyList<Opcion<TipoVenta>> TiposVenta { get; }

    [ObservableProperty] private VehiculoResumen? _vehiculoSeleccionado;
    [ObservableProperty] private Cliente? _clienteSeleccionado;
    [ObservableProperty] private string _precioTexto = string.Empty;
    [ObservableProperty] private Opcion<MetodoPago> _metodoSeleccionado;
    [ObservableProperty] private string _notas = string.Empty;
    [ObservableProperty] private string _mensajeError = string.Empty;
    [ObservableProperty] private bool _ocupado;

    // ---- Financiamiento del dealer (016) ----
    [ObservableProperty] private Opcion<TipoVenta> _tipoSeleccionado;
    [ObservableProperty] private string _inicialTexto = string.Empty;
    [ObservableProperty] private string _cantidadPlazosTexto = "12";
    [ObservableProperty] private DateTime _fechaPrimerPlazo;
    [ObservableProperty] private string _cadaDiasTexto = "30";
    [ObservableProperty] private string _adelantoSeparacionTexto = string.Empty;
    [ObservableProperty] private string _diasSeparacionTexto = "15";
    [ObservableProperty] private string _previewPlazosTexto = string.Empty;

    public bool EsPlazos => TipoSeleccionado?.Valor == TipoVenta.Plazos;
    public bool EsSeparacion => TipoSeleccionado?.Valor == TipoVenta.Separacion;

    partial void OnTipoSeleccionadoChanged(Opcion<TipoVenta> value)
    {
        OnPropertyChanged(nameof(EsPlazos));
        OnPropertyChanged(nameof(EsSeparacion));
        MensajeError = string.Empty;
        RecalcularPreview();
    }

    partial void OnPrecioTextoChanged(string value) => RecalcularPreview();
    partial void OnInicialTextoChanged(string value) => RecalcularPreview();
    partial void OnCantidadPlazosTextoChanged(string value) => RecalcularPreview();

    /// <summary>Vista previa del plan: cuánto queda por plazo (el resto cae en el último).</summary>
    private void RecalcularPreview()
    {
        PreviewPlazosTexto = string.Empty;
        if (!EsPlazos)
            return;
        if (!decimal.TryParse(PrecioTexto, NumberStyles.Number, CulturaRd, out var precio) || precio <= 0m)
            return;
        decimal.TryParse(InicialTexto, NumberStyles.Number, CulturaRd, out var inicial);
        if (!int.TryParse(CantidadPlazosTexto, NumberStyles.Integer, CulturaRd, out var cantidad) || cantidad < 1)
            return;

        var saldo = precio - inicial;
        if (saldo <= 0m)
        {
            PreviewPlazosTexto = "Con esa inicial no queda saldo por financiar.";
            return;
        }
        var porPlazo = Math.Round(saldo / cantidad, 2, MidpointRounding.AwayFromZero);
        PreviewPlazosTexto = $"Saldo a financiar: {saldo.ToString("N2", CulturaRd)} DOP · " +
                             $"{cantidad} plazo(s) de ~{porPlazo.ToString("N2", CulturaRd)} DOP";
    }

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
            TipoSeleccionado = TiposVenta[0];
            InicialTexto = AdelantoSeparacionTexto = string.Empty;
            CantidadPlazosTexto = "12";
            CadaDiasTexto = "30";
            DiasSeparacionTexto = "15";
            FechaPrimerPlazo = FechaNegocio.Hoy.AddMonths(1).ToDateTime(TimeOnly.MinValue);
            PreviewPlazosTexto = string.Empty;

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
                throw new ArgumentException("Elige el vehículo a vender.");
            if (ClienteSeleccionado is null)
                throw new ArgumentException("Elige el cliente comprador.");
            if (!decimal.TryParse(PrecioTexto, NumberStyles.Number, CulturaRd, out var precio) || precio <= 0m)
                throw new ArgumentException("Ingresa un precio válido mayor que cero.");

            // Financiamiento del dealer (016): se arma el plan según el tipo
            PlanPlazos? plan = null;
            var diasSeparacion = 15;
            var adelanto = 0m;

            switch (TipoSeleccionado.Valor)
            {
                case TipoVenta.Plazos:
                    decimal.TryParse(InicialTexto, NumberStyles.Number, CulturaRd, out var inicial);
                    if (inicial < 0m)
                        throw new ArgumentException("La inicial no puede ser negativa.");
                    if (!int.TryParse(CantidadPlazosTexto, NumberStyles.Integer, CulturaRd, out var cantidad) || cantidad < 1)
                        throw new ArgumentException("Ingresa la cantidad de plazos (ej. 12).");
                    if (!int.TryParse(CadaDiasTexto, NumberStyles.Integer, CulturaRd, out var cadaDias) || cadaDias < 1)
                        throw new ArgumentException("Ingresa cada cuántos días vence un plazo (30 = mensual).");
                    plan = new PlanPlazos(inicial, cantidad,
                        DateOnly.FromDateTime(FechaPrimerPlazo), cadaDias);
                    break;

                case TipoVenta.Separacion:
                    if (!decimal.TryParse(AdelantoSeparacionTexto, NumberStyles.Number, CulturaRd, out adelanto) || adelanto <= 0m)
                        throw new ArgumentException("Ingresa el adelanto que dejó el cliente por la separación.");
                    if (!int.TryParse(DiasSeparacionTexto, NumberStyles.Integer, CulturaRd, out diasSeparacion) || diasSeparacion < 1)
                        throw new ArgumentException("Ingresa los días de derecho de la separación (el dealer usa 15).");
                    break;
            }

            var datos = new VentaVehiculoDatos(
                VehiculoSeleccionado.Id, ClienteSeleccionado.Id, precio,
                MetodoSeleccionado.Valor, Notas,
                TipoVenta: TipoSeleccionado.Valor,
                Plan: plan,
                DiasSeparacion: diasSeparacion,
                AdelantoSeparacion: adelanto);

            var (ventaId, codigo) = await _ventas.RegistrarAsync(datos);
            var mensaje = TipoSeleccionado.Valor switch
            {
                TipoVenta.Plazos =>
                    $"Venta {codigo}: {VehiculoSeleccionado.Descripcion} financiado a " +
                    $"{ClienteSeleccionado.NombreCompleto} en {plan!.CantidadPlazos} plazo(s).",
                TipoVenta.Separacion =>
                    $"Separación {codigo}: {VehiculoSeleccionado.Descripcion} reservado para " +
                    $"{ClienteSeleccionado.NombreCompleto} por {diasSeparacion} días.",
                _ =>
                    $"Venta {codigo}: {VehiculoSeleccionado.Descripcion} vendido a {ClienteSeleccionado.NombreCompleto}."
            };
            _dialogos.Informar("Venta registrada", mensaje);
            Registrado?.Invoke(ventaId);
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
