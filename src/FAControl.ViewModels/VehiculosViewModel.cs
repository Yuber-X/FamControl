using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAControl.Common;
using FAControl.Models;
using FAControl.Services;
using Serilog;

namespace FAControl.ViewModels;

/// <summary>Fila de la tabla de inventario (DealerControl).</summary>
public record VehiculoFila(VehiculoResumen Resumen)
{
    public long Id => Resumen.Id;
    public string Codigo => Resumen.Codigo;
    public string Descripcion => Resumen.Descripcion;
    public string TipoTexto => Textos.De(Resumen.Tipo);
    public string AnioTexto => Resumen.Anio?.ToString(Textos.CulturaRd) ?? "—";
    public string PlacaTexto => string.IsNullOrWhiteSpace(Resumen.Placa) ? "—" : Resumen.Placa!;
    public decimal CostoTotal => Resumen.CostoTotal;
    public decimal PrecioVenta => Resumen.PrecioVenta;
    public decimal GananciaEstimada => Resumen.GananciaEstimada;
    public string EstadoTexto => Textos.De(Resumen.Estado);
    public bool Disponible => Resumen.Estado == EstadoVehiculo.Disponible;
}

/// <summary>Criterio del filtro rápido del inventario.</summary>
public enum FiltroVehiculo
{
    Todos,
    Disponibles,
    Reservados,
    Vendidos,
    Alquilados
}

/// <summary>
/// Inventario de vehículos (DealerControl). Lista con búsqueda por código,
/// marca, modelo o placa, y filtro por estado. La ficha/edición y el alta
/// las abre el shell vía eventos.
/// </summary>
public partial class VehiculosViewModel : ObservableObject
{
    private readonly VehiculoService _servicio;
    private readonly IDialogService _dialogos;
    private IReadOnlyList<VehiculoResumen> _todos = [];

    public event Action? NuevoSolicitado;
    public event Action<long>? EditarSolicitado;

    public VehiculosViewModel(VehiculoService servicio, IDialogService dialogos)
    {
        _servicio = servicio;
        _dialogos = dialogos;

        Filtros =
        [
            new Opcion<FiltroVehiculo>(FiltroVehiculo.Todos, "Todos"),
            new Opcion<FiltroVehiculo>(FiltroVehiculo.Disponibles, "Disponibles"),
            new Opcion<FiltroVehiculo>(FiltroVehiculo.Reservados, "Reservados"),
            new Opcion<FiltroVehiculo>(FiltroVehiculo.Vendidos, "Vendidos"),
            new Opcion<FiltroVehiculo>(FiltroVehiculo.Alquilados, "Alquilados")
        ];
        _filtroSeleccionado = Filtros[0];
    }

    public ObservableCollection<VehiculoFila> Filas { get; } = [];
    public IReadOnlyList<Opcion<FiltroVehiculo>> Filtros { get; }

    /// <summary>Solo quien puede editar ve los botones de alta/edición/baja.</summary>
    public bool PuedeEditar => SesionActual.TienePermiso(Permisos.VehiculosEditar);

    [ObservableProperty] private string _textoBusqueda = string.Empty;
    [ObservableProperty] private Opcion<FiltroVehiculo> _filtroSeleccionado;
    [ObservableProperty] private string _contadorTexto = string.Empty;

    partial void OnTextoBusquedaChanged(string value) => AplicarFiltro();
    partial void OnFiltroSeleccionadoChanged(Opcion<FiltroVehiculo> value) => AplicarFiltro();

    public async Task CargarAsync()
    {
        try
        {
            _todos = await _servicio.ObtenerResumenesAsync();
            OnPropertyChanged(nameof(PuedeEditar));
            AplicarFiltro();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cargando el inventario de vehículos");
            _dialogos.MostrarError("Vehículos", $"No se pudo cargar el inventario.\n\n{ex.Message}");
        }
    }

    private void AplicarFiltro()
    {
        var filtro = TextoBusqueda.Trim();
        var visibles = _todos
            .Where(v => FiltroSeleccionado.Valor switch
            {
                FiltroVehiculo.Disponibles => v.Estado == EstadoVehiculo.Disponible,
                FiltroVehiculo.Reservados => v.Estado == EstadoVehiculo.Reservado,
                FiltroVehiculo.Vendidos => v.Estado == EstadoVehiculo.Vendido,
                FiltroVehiculo.Alquilados => v.Estado == EstadoVehiculo.Alquilado,
                _ => true
            })
            .Where(v => string.IsNullOrEmpty(filtro) ||
                v.Codigo.Contains(filtro, StringComparison.OrdinalIgnoreCase) ||
                v.Descripcion.Contains(filtro, StringComparison.OrdinalIgnoreCase) ||
                (v.Placa?.Contains(filtro, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();

        Filas.Clear();
        foreach (var resumen in visibles)
            Filas.Add(new VehiculoFila(resumen));

        ContadorTexto = _todos.Count == 0
            ? "Sin vehículos en el inventario"
            : $"Mostrando {Filas.Count} de {_todos.Count} vehículos";
    }

    [RelayCommand]
    private void Nuevo() => NuevoSolicitado?.Invoke();

    [RelayCommand]
    private void Editar(VehiculoFila? fila)
    {
        if (fila is not null)
            EditarSolicitado?.Invoke(fila.Id);
    }

    [RelayCommand]
    private async Task EliminarAsync(VehiculoFila? fila)
    {
        if (fila is null)
            return;
        if (!_dialogos.Confirmar("Eliminar vehículo",
                $"¿Eliminar el vehículo {fila.Codigo} ({fila.Descripcion})?\n\n" +
                "Se puede recuperar; queda oculto del inventario."))
            return;

        try
        {
            await _servicio.EliminarAsync(fila.Id);
            await CargarAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error eliminando el vehículo {Id}", fila.Id);
            _dialogos.MostrarError("Eliminar vehículo", ex.Message);
        }
    }
}
