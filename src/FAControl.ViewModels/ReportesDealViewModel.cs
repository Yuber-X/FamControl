using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAControl.Common;
using FAControl.Models;
using FAControl.Services;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Serilog;
using SkiaSharp;

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

    // ---------- Filtros por usuario y por cliente (pedido 2026-07-30) ----------
    // "los filtros por usuario (lo que vendio y etc.), tambien otro para los
    // clientes (para observar los ingresos de tal cliente)."

    /// <summary>Opcion "todos", primera de los dos combos. Id null = sin filtro.</summary>
    public static readonly OpcionFiltroReporte Todos = new(null, "Todos");

    public ObservableCollection<OpcionFiltroReporte> Usuarios { get; } = [Todos];
    public ObservableCollection<OpcionFiltroReporte> Clientes { get; } = [Todos];

    [ObservableProperty] private OpcionFiltroReporte _usuarioSeleccionado = Todos;
    [ObservableProperty] private OpcionFiltroReporte _clienteSeleccionado = Todos;

    /// <summary>
    /// El inventario (vehiculos disponibles, capital invertido) NO se filtra:
    /// es del negocio, no de una persona. Con un filtro puesto la pantalla lo
    /// aclara, si no el dueño lee "capital invertido" al lado de "cliente: Juan"
    /// como si fuera de Juan.
    /// </summary>
    [ObservableProperty] private bool _hayFiltro;
    [ObservableProperty] private string _filtroTexto = string.Empty;

    partial void OnUsuarioSeleccionadoChanged(OpcionFiltroReporte value) => _ = GenerarAsync();
    partial void OnClienteSeleccionadoChanged(OpcionFiltroReporte value) => _ = GenerarAsync();

    /// <summary>Vuelve a "todos" en los dos combos.</summary>
    [RelayCommand]
    private void LimpiarFiltros()
    {
        // Se cambia el de cliente primero y el de usuario despues: cada uno
        // dispara su recarga, y asi la ultima que corre ya tiene los dos limpios.
        ClienteSeleccionado = Todos;
        UsuarioSeleccionado = Todos;
    }

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

    // ---------- Gráficos del período (pedido 2026-07-27) ----------
    /// <summary>De dónde vino el dinero: ventas vs alquiler.</summary>
    [ObservableProperty] private ISeries[] _seriesOrigen = [];
    /// <summary>Cuánto vendió cada vendedor (barras horizontales).</summary>
    [ObservableProperty] private ISeries[] _seriesVendedores = [];
    [ObservableProperty] private Axis[] _vendedoresXAxes = [];
    [ObservableProperty] private Axis[] _vendedoresYAxes = [];
    [ObservableProperty] private bool _hayIngresos;
    [ObservableProperty] private bool _sinIngresos = true;
    [ObservableProperty] private bool _sinComisiones = true;

    private static readonly SKColor ColorVentas = SKColor.Parse("#3D5A80");
    private static readonly SKColor ColorAlquiler = SKColor.Parse("#C9A15A");
    private static readonly SKColor ColorEtiquetas = SKColor.Parse("#888780");

    public async Task CargarAsync()
    {
        await CargarFiltrosAsync();
        await GenerarAsync();
    }

    /// <summary>
    /// Llena los combos. Se recargan en cada visita a la pantalla: entre una y
    /// otra pudo entrar un cliente nuevo o venderse el primer auto de alguien.
    /// Se conserva lo elegido si sigue existiendo, para no perder el filtro al
    /// volver.
    /// </summary>
    private async Task CargarFiltrosAsync()
    {
        try
        {
            var usuarioElegido = UsuarioSeleccionado?.Id;
            var clienteElegido = ClienteSeleccionado?.Id;

            var usuarios = await _reportes.ObtenerUsuariosDelDealerAsync();
            Usuarios.Clear();
            Usuarios.Add(Todos);
            foreach (var u in usuarios) Usuarios.Add(u);

            var clientes = await _reportes.ObtenerClientesDelDealerAsync();
            Clientes.Clear();
            Clientes.Add(Todos);
            foreach (var c in clientes) Clientes.Add(c);

            UsuarioSeleccionado = Usuarios.FirstOrDefault(u => u.Id == usuarioElegido) ?? Todos;
            ClienteSeleccionado = Clientes.FirstOrDefault(c => c.Id == clienteElegido) ?? Todos;
        }
        catch (Exception ex)
        {
            // Sin combos el reporte sigue sirviendo sin filtrar: no vale la pena
            // tumbar la pantalla entera por esto.
            Log.Warning(ex, "No se pudieron cargar los filtros del reporte del dealer");
        }
    }

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
                DateOnly.FromDateTime(Desde), DateOnly.FromDateTime(Hasta),
                UsuarioSeleccionado?.Id, ClienteSeleccionado?.Id);

            RangoTexto = $"{reporte.Desde:dd/MM/yyyy} – {reporte.Hasta:dd/MM/yyyy}";
            ArmarTextoDeFiltro(reporte);
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
            SinComisiones = !HayComisiones;

            ComisionTexto = _ajustes.PorcentajeComisionVendedor > 0m
                ? $"Comisión configurada: {_ajustes.PorcentajeComisionVendedor:0.##}% del monto vendido."
                : "Sin % de comisión configurado (Configuración → Datos del negocio): la columna irá en cero.";

            // El ORDEN importa: primero se muestra el panel, después se cargan
            // las series.
            //
            // Todo el reporte cuelga de un StackPanel con Visibility atada a
            // TieneReporte. Si las series se asignan mientras ese panel está
            // Collapsed, LiveCharts mide el gráfico con tamaño cero y no vuelve
            // a dibujarlo cuando el panel aparece: quedaba en blanco la PRIMERA
            // vez y salía bien de la segunda en adelante, porque para entonces
            // el panel ya estaba visible (reportado 2026-08-01).
            TieneReporte = true;
            ConstruirGraficos(reporte);
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

    /// <summary>
    /// Dos lecturas rápidas del período: de dónde vino el dinero (torta) y
    /// quién vendió cuánto (barras). Salen de los mismos datos del reporte,
    /// sin consultas extra.
    /// </summary>
    private void ConstruirGraficos(ReporteDeal reporte)
    {
        HayIngresos = reporte.MontoVendido > 0m || reporte.IngresosAlquiler > 0m;
        SinIngresos = !HayIngresos;

        SeriesOrigen =
        [
            new PieSeries<double>
            {
                Name = "Ventas",
                Values = [(double)reporte.MontoVendido],
                Fill = new SolidColorPaint(ColorVentas),
                DataLabelsPaint = new SolidColorPaint(SKColors.White),
                DataLabelsSize = 12,
                DataLabelsPosition = LiveChartsCore.Measure.PolarLabelsPosition.Middle,
                DataLabelsFormatter = _ => "Ventas"
            },
            new PieSeries<double>
            {
                Name = "Alquiler",
                Values = [(double)reporte.IngresosAlquiler],
                Fill = new SolidColorPaint(ColorAlquiler),
                DataLabelsPaint = new SolidColorPaint(SKColors.White),
                DataLabelsSize = 12,
                DataLabelsPosition = LiveChartsCore.Measure.PolarLabelsPosition.Middle,
                DataLabelsFormatter = _ => "Alquiler"
            }
        ];

        var vendedores = reporte.PorVendedor.ToList();
        SeriesVendedores =
        [
            new ColumnSeries<double>
            {
                Name = "Vendido",
                Values = [.. vendedores.Select(v => (double)v.MontoVendido)],
                Fill = new SolidColorPaint(ColorVentas),
                Rx = 4,
                Ry = 4
            }
        ];
        VendedoresXAxes =
        [
            new Axis
            {
                Labels = [.. vendedores.Select(v => v.VendedorNombre)],
                TextSize = 10,
                LabelsRotation = vendedores.Count > 4 ? 25 : 0,
                LabelsPaint = new SolidColorPaint(ColorEtiquetas),
                SeparatorsPaint = null
            }
        ];
        VendedoresYAxes =
        [
            new Axis
            {
                MinLimit = 0,
                TextSize = 10,
                LabelsPaint = new SolidColorPaint(ColorEtiquetas),
                Labeler = valor => valor >= 1000
                    ? $"{valor / 1000:0.#}k"
                    : valor.ToString("0", Textos.CulturaRd)
            }
        ];
    }

    /// <summary>Deja claro sobre que se armo el reporte y que quedo afuera del filtro.</summary>
    private void ArmarTextoDeFiltro(ReporteDeal reporte)
    {
        HayFiltro = reporte.HayFiltro;
        if (!HayFiltro)
        {
            FiltroTexto = string.Empty;
            return;
        }

        var partes = new List<string>();
        if (UsuarioSeleccionado?.Id is not null)
            partes.Add($"usuario: {UsuarioSeleccionado.Nombre}");
        if (ClienteSeleccionado?.Id is not null)
            partes.Add($"cliente: {ClienteSeleccionado.Nombre}");

        FiltroTexto = $"Filtrado por {string.Join(" y ", partes)}. " +
                      "Los vehículos disponibles y el capital invertido son del negocio entero: " +
                      "no dependen del filtro.";
    }
}
