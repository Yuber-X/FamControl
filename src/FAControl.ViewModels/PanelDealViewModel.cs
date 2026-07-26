using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FAControl.Common;
using FAControl.Models;
using FAControl.Services;
using Serilog;

namespace FAControl.ViewModels;

/// <summary>Fila de "últimos movimientos" del panel del dealer.</summary>
public record MovimientoDealFila(MovimientoDeal Movimiento)
{
    public string Tipo => Movimiento.Tipo;
    public string Codigo => Movimiento.Codigo;
    public string FechaTexto =>
        FechaNegocio.AUtcLocal(Movimiento.FechaUtc).ToString(Textos.FormatoFecha, Textos.CulturaRd);
    public string ClienteNombre => Movimiento.ClienteNombre;
    public string VehiculoDescripcion => Movimiento.VehiculoDescripcion;
    public decimal Monto => Movimiento.Monto;
    public bool EsVenta => Movimiento.Tipo == "Venta";
}

/// <summary>
/// Panel principal de DealControl (pedido 2026-07-25): inventario, ventas del
/// mes, alquileres e inversión. SOLO datos del dealer — cero PrestControl.
/// </summary>
public partial class PanelDealViewModel : ObservableObject
{
    private readonly PanelDealService _panel;
    private readonly IDialogService _dialogos;

    public PanelDealViewModel(PanelDealService panel, IDialogService dialogos)
    {
        _panel = panel;
        _dialogos = dialogos;
    }

    public ObservableCollection<MovimientoDealFila> Movimientos { get; } = [];

    [ObservableProperty] private int _vehiculosDisponibles;
    [ObservableProperty] private string _alquiladosTexto = string.Empty;
    [ObservableProperty] private decimal _capitalInvertido;
    [ObservableProperty] private decimal _montoVentasMes;
    [ObservableProperty] private string _ventasMesTexto = string.Empty;
    [ObservableProperty] private decimal _gananciaVentasMes;
    [ObservableProperty] private decimal _ingresosAlquilerMes;
    [ObservableProperty] private int _alquileresActivos;
    [ObservableProperty] private string _alquileresActivosTexto = string.Empty;
    [ObservableProperty] private bool _hayMovimientos;
    [ObservableProperty] private bool _sinMovimientos = true;

    public async Task CargarAsync()
    {
        try
        {
            var resumen = await _panel.ObtenerResumenAsync();

            VehiculosDisponibles = resumen.VehiculosDisponibles;
            AlquiladosTexto = resumen.VehiculosAlquilados == 1
                ? "1 alquilado ahora"
                : $"{resumen.VehiculosAlquilados} alquilados ahora";
            CapitalInvertido = resumen.CapitalInvertido;
            MontoVentasMes = resumen.MontoVentasMes;
            VentasMesTexto = resumen.VentasMes == 1
                ? "1 venta este mes"
                : $"{resumen.VentasMes} ventas este mes";
            GananciaVentasMes = resumen.GananciaVentasMes;
            IngresosAlquilerMes = resumen.IngresosAlquilerMes;
            AlquileresActivos = resumen.AlquileresActivos;
            AlquileresActivosTexto = resumen.AlquileresActivos == 1
                ? "1 alquiler activo"
                : $"{resumen.AlquileresActivos} alquileres activos";

            Movimientos.Clear();
            foreach (var movimiento in resumen.UltimosMovimientos)
                Movimientos.Add(new MovimientoDealFila(movimiento));
            HayMovimientos = Movimientos.Count > 0;
            SinMovimientos = !HayMovimientos;
        }
        catch (UnauthorizedAccessException ex)
        {
            _dialogos.MostrarError("Panel del dealer", ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cargando el panel de DealControl");
            _dialogos.MostrarError("Panel del dealer", $"No se pudo cargar el panel.\n\n{ex.Message}");
        }
    }
}
