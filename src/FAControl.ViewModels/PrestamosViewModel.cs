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
    private IReadOnlyList<PrestamoResumen> _todos = [];

    public event Action<long>? DetalleSolicitado;
    public event Action? NuevoSolicitado;

    public PrestamosViewModel(PrestamoService servicio, IDialogService dialogos)
    {
        _servicio = servicio;
        _dialogos = dialogos;

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
}
