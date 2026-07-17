using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using FAControl.Common;
using FAControl.Models;
using FAControl.Services;
using Serilog;
using SkiaSharp;

namespace FAControl.ViewModels;

/// <summary>Fila del panel de alertas de cobro, con su semáforo calculado.</summary>
public record AlertaFila(AlertaCobro Alerta, SemaforoCuota Semaforo)
{
    public long PrestamoId => Alerta.PrestamoId;
    public string ClienteNombre => Alerta.ClienteNombre;
    public string Codigo => Alerta.PrestamoCodigo;
    public string CuotaTexto => $"Cuota {Alerta.NumeroCuota} · vence {Alerta.FechaVencimiento:dd/MM/yyyy}";
    public decimal SaldoPendiente => Alerta.SaldoPendiente;
    public string SemaforoTexto => Textos.De(Semaforo);
}

/// <summary>
/// Panel de control (mockup 2): 4 KPIs, alertas de cobro con navegación
/// directa a Cobros, gráfico de cobros diarios del mes y últimos movimientos.
/// </summary>
public partial class PanelViewModel : ObservableObject
{
    private static readonly CultureInfo CulturaRd = CultureInfo.GetCultureInfo("es-DO");
    private static readonly SKColor ColorPrimario = SKColor.Parse("#4F46E5");
    private static readonly SKColor ColorTextoTerciario = SKColor.Parse("#888780");

    private readonly DashboardService _dashboard;
    private readonly PagoService _pagos;
    private readonly IDialogService _dialogos;

    public event Action<long>? CobrarSolicitado;

    public PanelViewModel(DashboardService dashboard, PagoService pagos, IDialogService dialogos)
    {
        _dashboard = dashboard;
        _pagos = pagos;
        _dialogos = dialogos;
    }

    // ---------- KPIs ----------
    [ObservableProperty] private decimal _capitalColocado;
    [ObservableProperty] private decimal _cobrosDelMes;
    [ObservableProperty] private string _deltaCobrosTexto = string.Empty;
    [ObservableProperty] private bool _deltaCobrosPositivo = true;
    [ObservableProperty] private int _clientesActivos;
    [ObservableProperty] private string _prestamosActivosTexto = string.Empty;
    [ObservableProperty] private decimal _morosidad;
    [ObservableProperty] private string _morosidadPorcentajeTexto = string.Empty;
    [ObservableProperty] private string _mesTexto = string.Empty;

    // ---------- Gráfico (valores double: solo presentación en píxeles,
    //            los montos reales siguen siendo decimal en todo el sistema) ----------
    [ObservableProperty] private ISeries[] _series = [];
    [ObservableProperty] private Axis[] _xAxes = [];
    [ObservableProperty] private Axis[] _yAxes = [];

    // ---------- Alertas y movimientos ----------
    public ObservableCollection<AlertaFila> Alertas { get; } = [];
    public ObservableCollection<PagoFila> Movimientos { get; } = [];

    [ObservableProperty] private bool _tieneAlertas;
    [ObservableProperty] private bool _tieneMovimientos;

    public async Task CargarAsync()
    {
        try
        {
            var hoy = FechaNegocio.Hoy;
            var datos = await _dashboard.ObtenerAsync();
            var movimientos = await _pagos.ObtenerRecientesAsync(10);

            CapitalColocado = datos.CapitalColocado;
            CobrosDelMes = datos.CobrosDelMes;
            (DeltaCobrosTexto, DeltaCobrosPositivo) = CalcularDelta(datos.CobrosDelMes, datos.CobrosMesAnterior);
            ClientesActivos = datos.ClientesActivos;
            PrestamosActivosTexto = $"{datos.PrestamosActivos} préstamo(s) activo(s)";
            Morosidad = datos.Morosidad;
            MorosidadPorcentajeTexto = datos.CapitalColocado > 0m
                ? $"{datos.Morosidad / datos.CapitalColocado * 100m:0.#}% del capital colocado"
                : "Sin capital colocado";
            MesTexto = $"Cobros diarios de {hoy.ToString("MMMM yyyy", CulturaRd)}";

            ConstruirGrafico(datos.CobrosPorDia, hoy);

            Alertas.Clear();
            foreach (var alerta in datos.Alertas)
            {
                var semaforo = CuotaEstadoCalculator.Calcular(
                    alerta.FechaVencimiento, alerta.MontoTotal, alerta.MontoPagado,
                    EstadoCuota.Pendiente, hoy);
                Alertas.Add(new AlertaFila(alerta, semaforo));
            }
            TieneAlertas = Alertas.Count > 0;

            Movimientos.Clear();
            foreach (var pago in movimientos)
                Movimientos.Add(new PagoFila(pago));
            TieneMovimientos = Movimientos.Count > 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cargando el panel de control");
            _dialogos.MostrarError("Panel de control", $"No se pudo cargar el panel.\n\n{ex.Message}");
        }
    }

    private static (string Texto, bool Positivo) CalcularDelta(decimal actual, decimal anterior)
    {
        if (anterior == 0m)
            return actual > 0m ? ("Sin cobros el mes anterior", true) : ("—", true);

        var pct = (actual - anterior) / anterior * 100m;
        var flecha = pct >= 0m ? "↑" : "↓";
        return ($"{flecha} {Math.Abs(pct):0.#}% vs mes anterior", pct >= 0m);
    }

    /// <summary>Barras de cobros por día, del 1 al día de hoy (días sin cobros = 0).</summary>
    private void ConstruirGrafico(IReadOnlyList<CobroDiario> cobros, DateOnly hoy)
    {
        var porDia = cobros.ToDictionary(c => c.Fecha.Day, c => c.Monto);
        var valores = new double[hoy.Day];
        var etiquetas = new string[hoy.Day];
        for (var dia = 1; dia <= hoy.Day; dia++)
        {
            valores[dia - 1] = (double)porDia.GetValueOrDefault(dia, 0m);
            etiquetas[dia - 1] = dia.ToString();
        }

        Series =
        [
            new ColumnSeries<double>
            {
                Values = valores,
                Fill = new SolidColorPaint(ColorPrimario),
                Rx = 4,
                Ry = 4,
                Name = "Cobros"
            }
        ];
        XAxes =
        [
            new Axis
            {
                Labels = etiquetas,
                TextSize = 10,
                LabelsPaint = new SolidColorPaint(ColorTextoTerciario),
                SeparatorsPaint = null
            }
        ];
        YAxes =
        [
            new Axis
            {
                MinLimit = 0,
                TextSize = 10,
                LabelsPaint = new SolidColorPaint(ColorTextoTerciario),
                Labeler = valor => valor >= 1000
                    ? $"{valor / 1000:0.#}k"
                    : valor.ToString("0", CulturaRd)
            }
        ];
    }

    [RelayCommand]
    private void Cobrar(AlertaFila? alerta)
    {
        if (alerta is not null)
            CobrarSolicitado?.Invoke(alerta.PrestamoId);
    }
}
