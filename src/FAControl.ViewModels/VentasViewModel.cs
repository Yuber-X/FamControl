using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAControl.Common;
using FAControl.Models;
using FAControl.Services;
using Serilog;

namespace FAControl.ViewModels;

/// <summary>Fila de la lista de ventas al contado.</summary>
public record VentaFila(VentaResumen Resumen)
{
    public long Id => Resumen.Id;
    public string Codigo => Resumen.Codigo;
    public string Vehiculo => Resumen.VehiculoDescripcion;
    public string Cliente => Resumen.ClienteNombre;
    public string FechaTexto => Resumen.FechaVentaUtc.ToLocalTime().ToString(Textos.FormatoFecha, Textos.CulturaRd);
    public decimal Precio => Resumen.Precio;
    public string MetodoTexto => Textos.De(Resumen.MetodoPago);
    /// <summary>Cómo se pactó la venta (016): contado, plazos o separación.</summary>
    public string TipoTexto => Resumen.TipoVenta switch
    {
        TipoVenta.Plazos => "Por plazos",
        TipoVenta.Separacion => "Separación",
        _ => "Contado"
    };
    /// <summary>Solo las financiadas/separadas tienen pantalla de plazos.</summary>
    public bool TienePlan => Resumen.TipoVenta != TipoVenta.Contado;
}

/// <summary>Lista de ventas al contado (DealerControl). El alta la abre el shell.</summary>
public partial class VentasViewModel : ObservableObject
{
    private readonly VentaVehiculoService _servicio;
    private readonly IDialogService _dialogos;
    private readonly AjustesLocales _ajustes;

    public event Action? NuevoSolicitado;
    /// <summary>
    /// La View abre la vista previa imprimible de la factura (2026-07-25).
    /// Va con el id de la venta porque desde ahí se puede reemplazar por la
    /// factura firmada y escaneada (2026-07-27).
    /// </summary>
    public event Action<FacturaVentaImpresa, long>? FacturaSolicitada;
    /// <summary>El shell abre la pantalla de plazos de una venta financiada (016).</summary>
    public event Action<long>? FinanciamientoSolicitado;

    public VentasViewModel(VentaVehiculoService servicio, IDialogService dialogos,
        AjustesLocales ajustes, ExpedienteViewModel expediente)
    {
        _servicio = servicio;
        _dialogos = dialogos;
        _ajustes = ajustes;
        Expediente = expediente;
    }

    /// <summary>Expediente digital: desde la factura se sube la versión firmada.</summary>
    public ExpedienteViewModel Expediente { get; }

    public ObservableCollection<VentaFila> Filas { get; } = [];

    /// <summary>
    /// FIX 2026-07-25: usaba el permiso viejo 'vehiculos_editar'; vender exige
    /// 'ventas' (el Vendedor puede vender — pedido del cliente).
    /// </summary>
    public bool PuedeEditar => SesionActual.TienePermiso(Permisos.Ventas);

    [ObservableProperty] private string _contadorTexto = string.Empty;

    public async Task CargarAsync()
    {
        try
        {
            var ventas = await _servicio.ObtenerResumenesAsync();
            OnPropertyChanged(nameof(PuedeEditar));
            Filas.Clear();
            foreach (var v in ventas)
                Filas.Add(new VentaFila(v));
            ContadorTexto = ventas.Count == 0 ? "Sin ventas registradas" : $"{ventas.Count} venta(s)";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cargando las ventas al contado");
            _dialogos.MostrarError("Ventas", $"No se pudieron cargar las ventas.\n\n{ex.Message}");
        }
    }

    [RelayCommand]
    private void Nuevo() => NuevoSolicitado?.Invoke();

    [RelayCommand]
    private void VerPlazos(VentaFila? fila)
    {
        if (fila is not null)
            FinanciamientoSolicitado?.Invoke(fila.Id);
    }

    /// <summary>Arma la factura imprimible de la venta (pedido 2026-07-25).</summary>
    [RelayCommand]
    private async Task VerFacturaAsync(VentaFila? fila)
    {
        if (fila is null)
            return;
        try
        {
            var d = await _servicio.ObtenerFacturaAsync(fila.Id);
            FacturaSolicitada?.Invoke(new FacturaVentaImpresa(
                NegocioNombre: _ajustes.NombreNegocio,
                NegocioRnc: _ajustes.RncNegocio,
                NegocioTelefono: _ajustes.TelefonoNegocio,
                NegocioCiudad: _ajustes.CiudadNegocio,
                Codigo: d.Codigo,
                FechaTexto: FechaNegocio.AUtcLocal(d.FechaVentaUtc).ToString(Textos.FormatoFecha, Textos.CulturaRd),
                Precio: d.Precio,
                MetodoTexto: Textos.De(d.MetodoPago),
                Notas: d.Notas,
                VendedorNombre: d.VendedorNombre,
                ClienteNombre: d.ClienteNombre,
                ClienteCedula: d.ClienteCedula ?? "—",
                ClienteTelefono: d.ClienteTelefono ?? "—",
                ClienteDireccion: d.ClienteDireccion ?? "—",
                VehiculoDescripcion: d.VehiculoDescripcion,
                Vin: d.Vin ?? "—",
                Placa: d.Placa ?? "—",
                Matricula: d.Matricula ?? "—",
                Color: d.Color ?? "—",
                AnioTexto: d.Anio?.ToString(Textos.CulturaRd) ?? "—"), fila.Id);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error armando la factura de la venta {Id}", fila.Id);
            _dialogos.MostrarError("Factura", $"No se pudo abrir la factura.\n\n{ex.Message}");
        }
    }
}
