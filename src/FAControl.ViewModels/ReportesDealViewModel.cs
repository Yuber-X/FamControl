using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAControl.Common;
using FAControl.Models;
using FAControl.Services;
using Serilog;

namespace FAControl.ViewModels;

/// <summary>Fila de comisiones por vendedor.</summary>
public record ComisionFila(ComisionVendedor Comision)
{
    public string VendedorNombre => Comision.VendedorNombre;
    public int CantidadVentas => Comision.CantidadVentas;
    public decimal MontoVendido => Comision.MontoVendido;
    public decimal Comisiones => Comision.Comision;
}

/// <summary>
/// Reportes PROPIOS de DealControl (pedido 2026-07-25: "agregar su propio
/// reportes, no mezclar con los datos del prestControl"): ventas, ganancia,
/// alquileres, inventario y comisiones por vendedor.
/// </summary>
public partial class ReportesDealViewModel : ObservableObject
{
    private readonly ReporteDealService _reportes;
    private readonly AjustesLocales _ajustes;
    private readonly IDialogService _dialogos;

    public ReportesDealViewModel(ReporteDealService reportes, AjustesLocales ajustes,
        IDialogService dialogos)
    {
        _reportes = reportes;
        _ajustes = ajustes;
        _dialogos = dialogos;

        var hoy = FechaNegocio.Hoy;
        _desde = new DateTime(hoy.Year, hoy.Month, 1);
        _hasta = hoy.ToDateTime(TimeOnly.MinValue);
    }

    public ObservableCollection<ComisionFila> PorVendedor { get; } = [];

    [ObservableProperty] private DateTime _desde;
    [ObservableProperty] private DateTime _hasta;
    [ObservableProperty] private bool _tieneReporte;
    [ObservableProperty] private string _rangoTexto = string.Empty;

    [ObservableProperty] private int _cantidadVentas;
    [ObservableProperty] private decimal _montoVendido;
    [ObservableProperty] private decimal _gananciaVentas;
    [ObservableProperty] private int _cantidadAlquileres;
    [ObservableProperty] private decimal _ingresosAlquiler;
    [ObservableProperty] private int _vehiculosDisponibles;
    [ObservableProperty] private decimal _capitalInvertido;
    [ObservableProperty] private decimal _pendienteDeCobro;
    [ObservableProperty] private string _comisionTexto = string.Empty;
    [ObservableProperty] private bool _hayComisiones;

    public async Task CargarAsync() => await GenerarAsync();

    // ---------- Atajos de rango ----------

    [RelayCommand]
    private Task EsteMesAsync()
    {
        var hoy = FechaNegocio.Hoy;
        Desde = new DateTime(hoy.Year, hoy.Month, 1);
        Hasta = hoy.ToDateTime(TimeOnly.MinValue);
        return GenerarAsync();
    }

    [RelayCommand]
    private Task MesPasadoAsync()
    {
        var hoy = FechaNegocio.Hoy;
        var inicioMes = new DateTime(hoy.Year, hoy.Month, 1);
        Desde = inicioMes.AddMonths(-1);
        Hasta = inicioMes.AddDays(-1);
        return GenerarAsync();
    }

    [RelayCommand]
    private Task TrimestreAsync()
    {
        var hoy = FechaNegocio.Hoy;
        Desde = new DateTime(hoy.Year, hoy.Month, 1).AddMonths(-2);
        Hasta = hoy.ToDateTime(TimeOnly.MinValue);
        return GenerarAsync();
    }

    [RelayCommand]
    private Task AnioAsync()
    {
        var hoy = FechaNegocio.Hoy;
        Desde = new DateTime(hoy.Year, 1, 1);
        Hasta = hoy.ToDateTime(TimeOnly.MinValue);
        return GenerarAsync();
    }

    [RelayCommand]
    private async Task GenerarAsync()
    {
        try
        {
            var reporte = await _reportes.ObtenerReporteAsync(
                DateOnly.FromDateTime(Desde), DateOnly.FromDateTime(Hasta));

            RangoTexto = $"{reporte.Desde:dd/MM/yyyy} – {reporte.Hasta:dd/MM/yyyy}";
            CantidadVentas = reporte.CantidadVentas;
            MontoVendido = reporte.MontoVendido;
            GananciaVentas = reporte.GananciaVentas;
            CantidadAlquileres = reporte.CantidadAlquileres;
            IngresosAlquiler = reporte.IngresosAlquiler;
            VehiculosDisponibles = reporte.VehiculosDisponibles;
            CapitalInvertido = reporte.CapitalInvertido;
            PendienteDeCobro = reporte.PendienteDeCobro;

            PorVendedor.Clear();
            foreach (var comision in reporte.PorVendedor)
                PorVendedor.Add(new ComisionFila(comision));
            HayComisiones = PorVendedor.Count > 0;

            ComisionTexto = _ajustes.PorcentajeComisionVendedor > 0m
                ? $"Comisión configurada: {_ajustes.PorcentajeComisionVendedor:0.##}% del monto vendido."
                : "Sin % de comisión configurado (Configuración → Datos del negocio): la columna irá en cero.";

            TieneReporte = true;
        }
        catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException)
        {
            _dialogos.MostrarError("Reporte del dealer", ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error generando el reporte de DealControl");
            _dialogos.MostrarError("Reporte del dealer", $"No se pudo generar el reporte.\n\n{ex.Message}");
        }
    }
}
