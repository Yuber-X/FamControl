// Portado de POS500.ViewModels el 2026-07-30 al integrar el punto de venta a la
// suite. Usa el SesionActual, los permisos y el IDialogService de FAControl.
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAControl.Common;
using FAControl.Models.Pos;
using FAControl.Services.Pos;
using Serilog;

namespace FAControl.ViewModels.Pos;

/// <summary>Fila de la tabla de clientes.</summary>
public record ClienteFila(Cliente Cliente)
{
    public long Id => Cliente.Id;
    public string CedulaTexto => string.IsNullOrWhiteSpace(Cliente.Cedula) ? "—" : Cliente.Cedula;
    public string Nombre => Cliente.Nombre;
    public string TelefonoTexto => string.IsNullOrWhiteSpace(Cliente.Telefono) ? "—" : Cliente.Telefono;
    public string DireccionTexto => string.IsNullOrWhiteSpace(Cliente.Direccion) ? "—" : Cliente.Direccion;
}

/// <summary>
/// Lista de clientes con búsqueda. Los botones de edición/eliminación solo
/// aparecen con permiso clientes_editar (el Cajero consulta, spec §6).
/// </summary>
public partial class ClientesViewModel : ObservableObject, IPaginaAsincrona
{
    private readonly ClienteService _servicio;
    private readonly IDialogService _dialogos;
    private IReadOnlyList<Cliente> _todos = [];

    public event Action? NuevoSolicitado;
    public event Action<long>? EdicionSolicitada;

    public ClientesViewModel(ClienteService servicio, IDialogService dialogos)
    {
        _servicio = servicio;
        _dialogos = dialogos;
    }

    public ObservableCollection<ClienteFila> Filas { get; } = [];

    [ObservableProperty] private string _textoBusqueda = string.Empty;
    [ObservableProperty] private string _contadorTexto = string.Empty;

    public bool PuedeEditar => SesionActual.TienePermiso("clientes_editar");

    partial void OnTextoBusquedaChanged(string value) => AplicarFiltro();

    public async Task RefrescarAsync()
    {
        try
        {
            OnPropertyChanged(nameof(PuedeEditar));
            _todos = await _servicio.ObtenerTodosAsync();
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
        var visibles = _todos.Where(c => string.IsNullOrEmpty(filtro) ||
            c.Nombre.Contains(filtro, StringComparison.OrdinalIgnoreCase) ||
            (c.Cedula?.Contains(filtro, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (c.Telefono?.Contains(filtro, StringComparison.OrdinalIgnoreCase) ?? false));

        Filas.Clear();
        foreach (var cliente in visibles)
            Filas.Add(new ClienteFila(cliente));

        ContadorTexto = _todos.Count == 0
            ? "Sin clientes registrados"
            : $"Mostrando {Filas.Count} de {_todos.Count} clientes";
    }

    [RelayCommand]
    private void Nuevo() => NuevoSolicitado?.Invoke();

    [RelayCommand]
    private void Editar(ClienteFila? fila)
    {
        if (fila is not null)
            EdicionSolicitada?.Invoke(fila.Id);
    }

    [RelayCommand]
    private async Task EliminarAsync(ClienteFila? fila)
    {
        if (fila is null)
            return;
        if (!_dialogos.Confirmar("Eliminar cliente",
                $"¿Eliminar a {fila.Nombre}?\n\nSus compras pasadas se conservan en el historial."))
            return;

        try
        {
            await _servicio.EliminarAsync(fila.Id);
            await RefrescarAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error eliminando cliente {Id}", fila.Id);
            _dialogos.MostrarError("Eliminar cliente", ex.Message);
        }
    }
}
