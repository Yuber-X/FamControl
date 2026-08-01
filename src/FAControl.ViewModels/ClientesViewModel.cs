using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAControl.Common;
using FAControl.Models;
using FAControl.Services;
using Serilog;

namespace FAControl.ViewModels;

/// <summary>Fila de la tabla de clientes.</summary>
public record ClienteFila(ClienteResumen Resumen)
{
    public long Id => Resumen.Id;
    public string Cedula => Resumen.Cedula;
    public string NombreCompleto => Resumen.NombreCompleto;
    public string TelefonoTexto => string.IsNullOrWhiteSpace(Resumen.Telefono) ? "—" : Resumen.Telefono;
    /// <summary>
    /// Contratos abiertos. En crédito son préstamos activos; en el dealer,
    /// vehículos que compró. El encabezado de la columna lo aclara según la
    /// estancia — es la misma columna con distinto significado.
    /// </summary>
    public int ContratosAbiertos => Resumen.ContratosAbiertos;
    public int Alquileres => Resumen.Alquileres;
    public decimal SaldoPendiente => Resumen.SaldoPendiente;
    public bool AlDia => Resumen.ContratosAbiertos == 0 || Resumen.SaldoPendiente == 0m;
}

/// <summary>Criterio del filtro rápido de la lista de clientes.</summary>
public enum FiltroCliente
{
    Todos,
    ConPrestamosActivos,
    SinPrestamosActivos,
    ConSaldoPendiente
}

/// <summary>Lista de clientes con búsqueda por nombre, cédula o teléfono.</summary>
public partial class ClientesViewModel : ObservableObject
{
    private readonly ClienteService _servicio;
    private readonly IDialogService _dialogos;
    private IReadOnlyList<ClienteResumen> _todos = [];

    public event Action? NuevoSolicitado;
    public event Action<long>? FichaSolicitada;

    public ClientesViewModel(ClienteService servicio, IDialogService dialogos)
    {
        _servicio = servicio;
        _dialogos = dialogos;

        Filtros =
        [
            new Opcion<FiltroCliente>(FiltroCliente.Todos, "Todos"),
            new Opcion<FiltroCliente>(FiltroCliente.ConPrestamosActivos, "Con préstamos activos"),
            new Opcion<FiltroCliente>(FiltroCliente.SinPrestamosActivos, "Sin préstamos activos"),
            new Opcion<FiltroCliente>(FiltroCliente.ConSaldoPendiente, "Con saldo pendiente")
        ];
        _filtroSeleccionado = Filtros[0];
    }

    public ObservableCollection<ClienteFila> Filas { get; } = [];

    // ---------- La misma tabla, dos estancias (2026-07-31) ----------
    // El dealer no presta: mostrar "Prestamos activos" ahi daba siempre 0.
    // En vez de dos pantallas casi iguales, la columna del contador cambia de
    // titulo y de significado, y la de alquileres solo aparece donde existe.

    public bool EsDealer => SesionActual.Modo == ModoApp.DealerControl;

    /// <summary>Encabezado del contador: lo unico que distingue las dos lecturas.</summary>
    public string TituloContratos => EsDealer ? "Vehículos comprados" : "Préstamos activos";

    public string TituloSaldo => EsDealer ? "Debe (financiado)" : "Saldo pendiente";
    public IReadOnlyList<Opcion<FiltroCliente>> Filtros { get; }

    [ObservableProperty]
    private string _textoBusqueda = string.Empty;

    [ObservableProperty]
    private Opcion<FiltroCliente> _filtroSeleccionado;

    [ObservableProperty]
    private string _contadorTexto = string.Empty;

    partial void OnTextoBusquedaChanged(string value) => AplicarFiltro();
    partial void OnFiltroSeleccionadoChanged(Opcion<FiltroCliente> value) => AplicarFiltro();

    public async Task CargarAsync()
    {
        try
        {
            _todos = await _servicio.ObtenerResumenesAsync();
            AplicarFiltro();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cargando la lista de clientes");
            _dialogos.MostrarError("Clientes", $"No se pudo cargar la lista de clientes.\n\n{ex.Message}");
        }
    }

    private void AplicarFiltro()
    {
        var filtro = TextoBusqueda.Trim();
        var visibles = _todos
            .Where(c => FiltroSeleccionado.Valor switch
            {
                FiltroCliente.ConPrestamosActivos => c.ContratosAbiertos > 0,
                FiltroCliente.SinPrestamosActivos => c.ContratosAbiertos == 0,
                FiltroCliente.ConSaldoPendiente => c.SaldoPendiente > 0m,
                _ => true
            })
            .Where(c => string.IsNullOrEmpty(filtro) ||
                c.NombreCompleto.Contains(filtro, StringComparison.OrdinalIgnoreCase) ||
                c.Cedula.Contains(filtro, StringComparison.OrdinalIgnoreCase) ||
                (c.Telefono?.Contains(filtro, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();

        Filas.Clear();
        foreach (var resumen in visibles)
            Filas.Add(new ClienteFila(resumen));

        ContadorTexto = _todos.Count == 0
            ? "Sin clientes registrados"
            : $"Mostrando {Filas.Count} de {_todos.Count} clientes";
    }

    [RelayCommand]
    private void Nuevo() => NuevoSolicitado?.Invoke();

    [RelayCommand]
    private void VerFicha(ClienteFila? fila)
    {
        if (fila is not null)
            FichaSolicitada?.Invoke(fila.Id);
    }
}
