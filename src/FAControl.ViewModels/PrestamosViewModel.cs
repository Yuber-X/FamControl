using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAControl.Common;
using FAControl.Services;
using FAControl.Models;
using Serilog;

namespace FAControl.ViewModels;

/// <summary>Lista de préstamos con búsqueda por código o cliente.</summary>
public partial class PrestamosViewModel : ObservableObject
{
    private readonly PrestamoService _servicio;
    private readonly IDialogService _dialogos;
    private readonly RecordatorioService _recordatorios;
    private readonly ContratoService _contratos;
    private IReadOnlyList<PrestamoResumen> _todos = [];

    public event Action<long>? DetalleSolicitado;
    public event Action? NuevoSolicitado;

    /// <summary>
    /// La vista abre los TRES contratos de ese préstamo (2026-09-03), sin tener
    /// que entrar al detalle. Reimprimir un contrato es de las cosas que más se
    /// piden por teléfono, y bajaba de tres clics a uno.
    /// </summary>
    public event Action<PagareNotarialImpreso, DuenoExpediente>? ContratosSolicitados;

    public PrestamosViewModel(PrestamoService servicio, IDialogService dialogos,
        RecordatorioService recordatorios, ContratoService contratos,
        ExpedienteViewModel expediente)
    {
        _servicio = servicio;
        _dialogos = dialogos;
        _recordatorios = recordatorios;
        _contratos = contratos;
        Expediente = expediente;

        FiltrosEstado =
        [
            new Opcion<EstadoPrestamo?>(null, "Todos los estados"),
            new Opcion<EstadoPrestamo?>(EstadoPrestamo.Activo, "Activos"),
            new Opcion<EstadoPrestamo?>(EstadoPrestamo.Pagado, "Pagados"),
            new Opcion<EstadoPrestamo?>(EstadoPrestamo.Cancelado, "Cancelados")
        ];
        _filtroEstado = FiltrosEstado[0];
    }

    public ObservableCollection<PrestamoFila> Filas { get; } = [];
    public IReadOnlyList<Opcion<EstadoPrestamo?>> FiltrosEstado { get; }

    /// <summary>
    /// Filtro por modo (lo fija el shell): null = todos; false = solo personales
    /// (PrestControl); true = solo créditos vehiculares (AutoControl).
    /// </summary>
    public bool? SoloVehiculares { get; set; }

    [ObservableProperty]
    private string _textoBusqueda = string.Empty;

    [ObservableProperty]
    private Opcion<EstadoPrestamo?> _filtroEstado;

    [ObservableProperty]
    private bool _cargando;

    [ObservableProperty]
    private string _contadorTexto = string.Empty;

    // Totales del grid (se recalculan con cada búsqueda/filtro):
    // el usuario nunca debería necesitar una calculadora
    [ObservableProperty] private decimal _totalCapital;
    [ObservableProperty] private decimal _totalPorCobrar;
    [ObservableProperty] private decimal _totalCobrado;
    [ObservableProperty] private int _totalActivos;

    partial void OnTextoBusquedaChanged(string value) => AplicarFiltro();
    partial void OnFiltroEstadoChanged(Opcion<EstadoPrestamo?> value) => AplicarFiltro();

    /// <summary>
    /// Reenvía los recordatorios por correo a TODOS los clientes con cuotas por
    /// vencer o vencidas de esta estancia (mismo envío masivo de Configuración).
    /// </summary>
    [RelayCommand]
    private async Task EnviarRecordatoriosAsync()
    {
        try
        {
            Cargando = true;
            var r = await _recordatorios.EnviarAsync();
            var msg = $"{r.CorreosACliente} recordatorio(s) enviado(s)" +
                      (r.SinEmail > 0 ? $" · {r.SinEmail} sin correo" : "") +
                      (r.ResumenAlDueno ? " · resumen al dueño" : "") + ".";
            if (r.Detalle.StartsWith("Con errores"))
                msg += "\n\n" + r.Detalle;
            _dialogos.Informar("Recordatorios", msg);
        }
        catch (InvalidOperationException ex)
        {
            _dialogos.Informar("Recordatorios", ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error enviando recordatorios desde Préstamos");
            _dialogos.MostrarError("Recordatorios", ex.Message);
        }
        finally
        {
            Cargando = false;
        }
    }

    public async Task CargarAsync()
    {
        try
        {
            Cargando = true;
            _todos = await _servicio.ObtenerResumenesAsync(SoloVehiculares);
            AplicarFiltro();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cargando la lista de préstamos");
            _dialogos.MostrarError("Préstamos", $"No se pudo cargar la lista de préstamos.\n\n{ex.Message}");
        }
        finally
        {
            Cargando = false;
        }
    }

    private void AplicarFiltro()
    {
        var filtro = TextoBusqueda.Trim();
        var visibles = _todos
            .Where(p => FiltroEstado.Valor is null || p.Estado == FiltroEstado.Valor)
            .Where(p => string.IsNullOrEmpty(filtro) ||
                p.Codigo.Contains(filtro, StringComparison.OrdinalIgnoreCase) ||
                p.ClienteNombre.Contains(filtro, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Filas.Clear();
        foreach (var resumen in visibles)
            Filas.Add(new PrestamoFila(resumen));

        TotalCapital = visibles.Sum(p => p.MontoCapital);
        TotalPorCobrar = visibles.Where(p => p.Estado == EstadoPrestamo.Activo).Sum(p => p.SaldoPendiente);
        TotalCobrado = visibles.Sum(p => p.TotalPagado);
        TotalActivos = visibles.Count(p => p.Estado == EstadoPrestamo.Activo);

        ContadorTexto = _todos.Count == 0
            ? "Sin préstamos registrados"
            : $"Mostrando {Filas.Count} de {_todos.Count} préstamos";
    }

    [RelayCommand]
    private void Nuevo() => NuevoSolicitado?.Invoke();

    [RelayCommand]
    private void VerDetalle(PrestamoFila? fila)
    {
        if (fila is not null)
            DetalleSolicitado?.Invoke(fila.Id);
    }
    /// <summary>Expediente donde se archiva lo que se imprima (2026-09-03).</summary>
    public ExpedienteViewModel Expediente { get; }

    /// <summary>
    /// Abre los tres contratos del préstamo de esa fila para verlos o
    /// reimprimirlos. Lo que se imprima queda archivado en su expediente.
    /// </summary>
    [RelayCommand]
    private async Task VerContratosAsync(PrestamoFila? fila)
    {
        if (fila is null)
            return;
        try
        {
            var contrato = await _contratos.ArmarNotarialAsync(fila.Id);
            ContratosSolicitados?.Invoke(contrato, DuenoExpediente.DePrestamo(fila.Id));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error armando los contratos del préstamo {Id}", fila.Id);
            _dialogos.MostrarError("Contratos", $"No se pudieron abrir los contratos.\n\n{ex.Message}");
        }
    }

}
