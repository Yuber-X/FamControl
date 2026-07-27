using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FAControl.Common;
using FAControl.Models;
using FAControl.Services;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Serilog;
using SkiaSharp;

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

    // ---------- Gráficos (pedido 2026-07-27) ----------
    // Lo que un dealer quiere ver apenas abre: cómo viene el mes contra los
    // anteriores (ventas vs alquiler) y cuánto del inventario está parado.
    [ObservableProperty] private ISeries[] _seriesMeses = [];
    [ObservableProperty] private Axis[] _mesesXAxes = [];
    [ObservableProperty] private Axis[] _mesesYAxes = [];
    [ObservableProperty] private ISeries[] _seriesInventario = [];
    [ObservableProperty] private bool _hayInventario;
    [ObservableProperty] private bool _sinInventario = true;

    // Acento del dealer (azul acero de la marca) y su complemento
    private static readonly SKColor ColorVentas = SKColor.Parse("#3D5A80");
    private static readonly SKColor ColorAlquiler = SKColor.Parse("#C9A15A");
    private static readonly SKColor ColorReservado = SKColor.Parse("#98C1D9");
    private static readonly SKColor ColorBaja = SKColor.Parse("#9AA0AA");
    private static readonly SKColor ColorEtiquetas = SKColor.Parse("#888780");

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

            ConstruirGraficoMeses(resumen.UltimosMeses);
            ConstruirGraficoInventario(resumen.Inventario);
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

    /// <summary>Barras agrupadas: ventas y alquileres de los últimos 6 meses.</summary>
    private void ConstruirGraficoMeses(IReadOnlyList<MesDeal> meses)
    {
        SeriesMeses =
        [
            new ColumnSeries<double>
            {
                Name = "Ventas",
                Values = [.. meses.Select(m => (double)m.MontoVentas)],
                Fill = new SolidColorPaint(ColorVentas),
                Rx = 4,
                Ry = 4
            },
            new ColumnSeries<double>
            {
                Name = "Alquiler",
                Values = [.. meses.Select(m => (double)m.MontoAlquiler)],
                Fill = new SolidColorPaint(ColorAlquiler),
                Rx = 4,
                Ry = 4
            }
        ];
        MesesXAxes =
        [
            new Axis
            {
                Labels = [.. meses.Select(m => m.Etiqueta)],
                TextSize = 10,
                LabelsPaint = new SolidColorPaint(ColorEtiquetas),
                SeparatorsPaint = null
            }
        ];
        MesesYAxes =
        [
            new Axis
            {
                MinLimit = 0,
                TextSize = 10,
                LabelsPaint = new SolidColorPaint(ColorEtiquetas),
                // Los montos de un dealer son grandes: en miles se leen mejor
                Labeler = valor => valor >= 1000
                    ? $"{valor / 1000:0.#}k"
                    : valor.ToString("0", Textos.CulturaRd)
            }
        ];
    }

    /// <summary>Torta del inventario vivo por estado (sin los vendidos).</summary>
    private void ConstruirGraficoInventario(IReadOnlyList<ConteoInventario> inventario)
    {
        SeriesInventario =
        [
            .. inventario.Select(item => new PieSeries<double>
            {
                Name = item.Estado,
                Values = [item.Cantidad],
                Fill = new SolidColorPaint(ColorDe(item.Estado)),
                DataLabelsPaint = new SolidColorPaint(SKColors.White),
                DataLabelsSize = 12,
                DataLabelsPosition = LiveChartsCore.Measure.PolarLabelsPosition.Middle,
                DataLabelsFormatter = punto => $"{item.Estado}: {punto.Coordinate.PrimaryValue:0}"
            })
        ];
        HayInventario = inventario.Any(i => i.Cantidad > 0);
        SinInventario = !HayInventario;
    }

    private static SKColor ColorDe(string estado) => estado switch
    {
        "Disponibles" => ColorVentas,
        "Alquilados" => ColorAlquiler,
        "Reservados" => ColorReservado,
        _ => ColorBaja
    };
}
