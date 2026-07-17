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

/// <summary>Fila del desglose semanal del reporte.</summary>
public record SemanaFila(IngresoSemanal Semana, CultureInfo Cultura)
{
    public string Etiqueta =>
        $"Sem. {Semana.NumeroSemana} ({Semana.Desde.ToString("dd", Cultura)}–{Semana.Hasta.ToString("dd MMM", Cultura)})";
    public decimal Capital => Semana.Capital;
    public decimal Interes => Semana.Interes;
    public decimal Total => Semana.Total;
}

/// <summary>
/// Reporte "Ingresos por período" (mockup Reportes): rango de fechas con
/// atajos, 4 KPIs, ganancia por semana (barras apiladas) y desglose semanal.
/// </summary>
public partial class ReportesViewModel : ObservableObject
{
    private static readonly CultureInfo CulturaRd = CultureInfo.GetCultureInfo("es-DO");
    private static readonly SKColor ColorInteres = SKColor.Parse("#4F46E5");
    private static readonly SKColor ColorCapital = SKColor.Parse("#D4D4D3");
    private static readonly SKColor ColorTextoTerciario = SKColor.Parse("#888780");

    private readonly ReporteService _reportes;
    private readonly ExportacionService _exportacion;
    private readonly IDialogService _dialogos;

    public ReportesViewModel(ReporteService reportes, ExportacionService exportacion, IDialogService dialogos)
    {
        _reportes = reportes;
        _exportacion = exportacion;
        _dialogos = dialogos;

        var hoy = FechaNegocio.Hoy;
        _desde = new DateTime(hoy.Year, hoy.Month, 1);
        _hasta = hoy.ToDateTime(TimeOnly.MinValue);
    }

    public ObservableCollection<SemanaFila> Semanas { get; } = [];

    [ObservableProperty] private DateTime _desde;
    [ObservableProperty] private DateTime _hasta;
    [ObservableProperty] private bool _tieneReporte;
    [ObservableProperty] private string _rangoTexto = string.Empty;
    [ObservableProperty] private decimal _interesCobrado;
    [ObservableProperty] private decimal _capitalRecuperado;
    [ObservableProperty] private decimal _totalCobrado;
    [ObservableProperty] private int _cuotasCobradas;
    [ObservableProperty] private string _cuotasProgramadasTexto = string.Empty;
    [ObservableProperty] private decimal _totalCapitalSemanas;
    [ObservableProperty] private decimal _totalInteresSemanas;
    [ObservableProperty] private ISeries[] _series = [];
    [ObservableProperty] private Axis[] _xAxes = [];
    [ObservableProperty] private Axis[] _yAxes = [];

    public Task CargarAsync() => GenerarAsync();

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

    // ---------- Generación ----------

    [RelayCommand]
    private async Task GenerarAsync()
    {
        try
        {
            var reporte = await _reportes.ObtenerIngresosAsync(
                DateOnly.FromDateTime(Desde), DateOnly.FromDateTime(Hasta));

            RangoTexto = $"{reporte.Desde:dd/MM/yyyy} – {reporte.Hasta:dd/MM/yyyy}";
            InteresCobrado = reporte.InteresCobrado;
            CapitalRecuperado = reporte.CapitalRecuperado;
            TotalCobrado = reporte.TotalCobrado;
            CuotasCobradas = reporte.CuotasCobradas;
            CuotasProgramadasTexto = $"de {reporte.CuotasProgramadas} programadas";

            Semanas.Clear();
            foreach (var semana in reporte.PorSemana)
                Semanas.Add(new SemanaFila(semana, CulturaRd));
            TotalCapitalSemanas = reporte.PorSemana.Sum(s => s.Capital);
            TotalInteresSemanas = reporte.PorSemana.Sum(s => s.Interes);

            ConstruirGrafico(reporte.PorSemana);
            TieneReporte = true;
        }
        catch (ArgumentException ex)
        {
            _dialogos.MostrarError("Reporte", ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error generando el reporte de ingresos");
            _dialogos.MostrarError("Reporte", $"No se pudo generar el reporte.\n\n{ex.Message}");
        }
    }

    /// <summary>Barras apiladas por semana (capital gris + interés indigo, como el mockup).</summary>
    private void ConstruirGrafico(IReadOnlyList<IngresoSemanal> semanas)
    {
        // Valores double: solo presentación en píxeles; los montos siguen en decimal
        Series =
        [
            new StackedColumnSeries<double>
            {
                Values = semanas.Select(s => (double)s.Capital).ToArray(),
                Fill = new SolidColorPaint(ColorCapital),
                Name = "Capital",
                Rx = 3, Ry = 3
            },
            new StackedColumnSeries<double>
            {
                Values = semanas.Select(s => (double)s.Interes).ToArray(),
                Fill = new SolidColorPaint(ColorInteres),
                Name = "Interés",
                Rx = 3, Ry = 3
            }
        ];
        XAxes =
        [
            new Axis
            {
                Labels = semanas.Select(s => $"Sem. {s.NumeroSemana}").ToArray(),
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

    /// <summary>Exporta todos los datos a Excel (la View pide la ruta con SaveFileDialog).</summary>
    public async Task ExportarAsync(string ruta)
    {
        try
        {
            await _exportacion.ExportarAsync(ruta);
            _dialogos.Informar("Exportar", $"Datos exportados a:\n{ruta}");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error exportando a Excel desde Reportes");
            _dialogos.MostrarError("Exportar", $"No se pudo exportar.\n\n{ex.Message}");
        }
    }
}
