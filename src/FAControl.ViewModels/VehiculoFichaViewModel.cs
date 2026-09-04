using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAControl.Common;
using FAControl.Models;
using FAControl.Services;
using Serilog;

namespace FAControl.ViewModels;

/// <summary>Fila del historial de reparaciones en la ficha.</summary>
public record ReparacionFila(VehiculoReparacion Reparacion)
{
    public long Id => Reparacion.Id;
    public string FechaTexto => Reparacion.Fecha.ToString(Textos.FormatoFecha, Textos.CulturaRd);
    public string Detalle => Reparacion.Detalle;
    public decimal Costo => Reparacion.Costo;
    public string RegistradaPor => Reparacion.RegistradaPor ?? "—";
}

/// <summary>
/// Ficha completa del vehículo (pedido 2026-07-25): datos, comprador/contrato,
/// historial de reparaciones e impresión en PDF. El Vendedor la ve SIN costos.
/// </summary>
public partial class VehiculoFichaViewModel : ObservableObject
{
    private readonly VehiculoService _servicio;
    private readonly AjustesLocales _ajustes;
    private readonly IDialogService _dialogos;
    private long _vehiculoId;
    private FichaVehiculo? _ficha;

    public event Action? VolverSolicitado;
    /// <summary>La View abre la vista previa imprimible.</summary>
    public event Action<FichaVehiculoImpresa>? ImpresionSolicitada;

    public VehiculoFichaViewModel(VehiculoService servicio, AjustesLocales ajustes, IDialogService dialogos)
    {
        _servicio = servicio;
        _ajustes = ajustes;
        _dialogos = dialogos;
    }

    public ObservableCollection<ReparacionFila> Reparaciones { get; } = [];

    [ObservableProperty] private string _codigo = string.Empty;
    [ObservableProperty] private string _descripcion = string.Empty;
    [ObservableProperty] private string _estadoTexto = string.Empty;
    [ObservableProperty] private string _tipoTexto = string.Empty;
    [ObservableProperty] private string _vinTexto = "—";
    [ObservableProperty] private string _placaTexto = "—";
    [ObservableProperty] private string _matriculaTexto = "—";
    [ObservableProperty] private string _colorTexto = "—";
    [ObservableProperty] private string _anioTexto = "—";
    [ObservableProperty] private string _kilometrajeTexto = "—";
    [ObservableProperty] private string _notasTexto = "—";
    [ObservableProperty] private decimal _precioVenta;
    [ObservableProperty] private decimal _costoAdquisicion;
    [ObservableProperty] private decimal _gastosImportacion;
    [ObservableProperty] private decimal _costoTotal;
    [ObservableProperty] private decimal _gananciaEstimada;
    [ObservableProperty] private string _compradorTexto = "Sin venta registrada.";
    [ObservableProperty] private decimal _costoReparaciones;
    [ObservableProperty] private bool _hayReparaciones;
    [ObservableProperty] private bool _sinReparaciones = true;

    /// <summary>El Vendedor no ve costos ni ganancias (pedido 2026-07-25).</summary>
    public bool PuedeVerCostos => SesionActual.TienePermiso(Permisos.InventarioEditar);
    /// <summary>Registrar reparaciones exige poder editar el inventario.</summary>
    public bool PuedeEditar => SesionActual.TienePermiso(Permisos.InventarioEditar);

    // ---- Alta de reparación (inline) ----
    [ObservableProperty] private DateTime _reparacionFecha = DateTime.Today;
    [ObservableProperty] private string _reparacionDetalle = string.Empty;
    [ObservableProperty] private string _reparacionCostoTexto = string.Empty;
    [ObservableProperty] private string _mensajeReparacion = string.Empty;

    public async Task CargarAsync(long vehiculoId)
    {
        try
        {
            _vehiculoId = vehiculoId;
            var ficha = await _servicio.ObtenerFichaAsync(vehiculoId);
            _ficha = ficha;
            var v = ficha.Vehiculo;

            Codigo = v.Codigo;
            Descripcion = v.Descripcion;
            EstadoTexto = Textos.De(v.Estado);
            TipoTexto = Textos.De(v.Tipo);
            VinTexto = TextoODash(v.Vin);
            PlacaTexto = TextoODash(v.Placa);
            MatriculaTexto = TextoODash(v.Matricula);
            ColorTexto = TextoODash(v.Color);
            AnioTexto = v.Anio?.ToString(Textos.CulturaRd) ?? "—";
            KilometrajeTexto = v.Kilometraje is { } km ? $"{km:N0} km" : "—";
            NotasTexto = TextoODash(v.Notas);
            PrecioVenta = v.PrecioVenta;
            CostoAdquisicion = v.CostoAdquisicion;
            GastosImportacion = v.GastosImportacion;
            CostoTotal = v.CostoTotal;
            GananciaEstimada = v.GananciaEstimada;
            CompradorTexto = ArmarCompradorTexto(ficha);

            Reparaciones.Clear();
            foreach (var reparacion in ficha.Reparaciones)
                Reparaciones.Add(new ReparacionFila(reparacion));
            CostoReparaciones = ficha.CostoReparaciones;
            HayReparaciones = Reparaciones.Count > 0;
            SinReparaciones = !HayReparaciones;

            MensajeReparacion = string.Empty;
            OnPropertyChanged(nameof(PuedeVerCostos));
            OnPropertyChanged(nameof(PuedeEditar));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cargando la ficha del vehículo {Id}", vehiculoId);
            _dialogos.MostrarError("Ficha del vehículo", $"No se pudo cargar la ficha.\n\n{ex.Message}");
        }
    }

    private static string ArmarCompradorTexto(FichaVehiculo ficha)
    {
        if (ficha.Venta is { } venta)
            return $"{venta.ClienteNombre} — venta {venta.Codigo} del " +
                   $"{FechaNegocio.AUtcLocal(venta.FechaVentaUtc).ToString(Textos.FormatoFecha, Textos.CulturaRd)} " +
                   $"por RD$ {venta.Precio.ToString("N2", Textos.CulturaRd)}";
        if (ficha.CreditoCodigo is { } codigo)
            return $"{ficha.CreditoClienteNombre} — venta financiada {codigo} (AutoControl)";
        return "Sin venta registrada.";
    }

    private static string TextoODash(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? "—" : valor;

    [RelayCommand]
    private void Volver() => VolverSolicitado?.Invoke();

    [RelayCommand]
    private async Task AgregarReparacionAsync()
    {
        MensajeReparacion = string.Empty;
        var costo = 0m;
        if (!string.IsNullOrWhiteSpace(ReparacionCostoTexto) &&
            (!decimal.TryParse(ReparacionCostoTexto, NumberStyles.Number, Textos.CulturaRd, out costo) || costo < 0m))
        {
            MensajeReparacion = "Ingresa un costo válido (ej. 12,500) o dejalo vacío.";
            return;
        }

        try
        {
            await _servicio.AgregarReparacionAsync(_vehiculoId,
                DateOnly.FromDateTime(ReparacionFecha), ReparacionDetalle, costo);
            ReparacionDetalle = string.Empty;
            ReparacionCostoTexto = string.Empty;
            ReparacionFecha = DateTime.Today;
            await CargarAsync(_vehiculoId);
        }
        catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException)
        {
            MensajeReparacion = ex.Message;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error agregando reparación al vehículo {Id}", _vehiculoId);
            _dialogos.MostrarError("Reparaciones", ex.Message);
        }
    }

    [RelayCommand]
    private async Task EliminarReparacionAsync(ReparacionFila? fila)
    {
        if (fila is null)
            return;
        if (!_dialogos.Confirmar("Eliminar reparación",
            $"¿Eliminar la reparación del {fila.FechaTexto} ({fila.Detalle})?"))
            return;
        try
        {
            await _servicio.EliminarReparacionAsync(fila.Id);
            await CargarAsync(_vehiculoId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error eliminando la reparación {Id}", fila.Id);
            _dialogos.MostrarError("Reparaciones", ex.Message);
        }
    }

    /// <summary>Abre la vista previa imprimible de la ficha (PDF con datos completos).</summary>
    [RelayCommand]
    private void Imprimir()
    {
        if (_ficha is null)
            return;
        ImpresionSolicitada?.Invoke(new FichaVehiculoImpresa(
            NegocioNombre: _ajustes.NombreNegocio,
            NegocioRnc: _ajustes.RncNegocio,
            NegocioTelefono: _ajustes.TelefonoNegocio,
            Codigo: Codigo,
            Descripcion: Descripcion,
            TipoTexto: TipoTexto,
            EstadoTexto: EstadoTexto,
            Vin: VinTexto,
            Placa: PlacaTexto,
            Matricula: MatriculaTexto,
            Color: ColorTexto,
            AnioTexto: AnioTexto,
            KilometrajeTexto: KilometrajeTexto,
            Notas: NotasTexto == "—" ? null : NotasTexto,
            MostrarCostos: PuedeVerCostos,
            CostoAdquisicion: CostoAdquisicion,
            GastosImportacion: GastosImportacion,
            PrecioVenta: PrecioVenta,
            CompradorTexto: CompradorTexto == "Sin venta registrada." ? null : CompradorTexto,
            Reparaciones: [.. Reparaciones.Select(r => new ReparacionImpresa(r.FechaTexto, r.Detalle, r.Costo))],
            CostoReparaciones: CostoReparaciones,
            EmitidoPor: SesionActual.Nombre));
    }
}
