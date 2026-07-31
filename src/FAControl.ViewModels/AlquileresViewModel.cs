using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAControl.Common;
using FAControl.Models;
using FAControl.Services;
using Serilog;

namespace FAControl.ViewModels;

/// <summary>Fila de la lista de alquileres (rent a car).</summary>
public record AlquilerFila(AlquilerResumen Resumen)
{
    public long Id => Resumen.Id;
    public string Codigo => Resumen.Codigo;
    public string Vehiculo => Resumen.VehiculoDescripcion;
    public string Cliente => Resumen.ClienteNombre;
    public string PeriodoTexto =>
        $"{Resumen.FechaInicio.ToString(Textos.FormatoFecha, Textos.CulturaRd)} → " +
        $"{Resumen.FechaFin.ToString(Textos.FormatoFecha, Textos.CulturaRd)}";
    public int Dias => Resumen.Dias;
    public decimal MontoTotal => Resumen.MontoTotal;
    public string EstadoTexto => Textos.De(Resumen.Estado);
    public string Registro => Resumen.Registro;
    public bool EstaActivo => Resumen.Estado == EstadoAlquiler.Activo;
}

/// <summary>
/// Lista de alquileres (DealerControl). Desde 031 el cierre no se hace aca:
/// cada fila abre su pantalla de detalles, que es donde estan editar y cerrar.
/// </summary>
public partial class AlquileresViewModel : ObservableObject
{
    private readonly AlquilerService _servicio;
    private readonly IDialogService _dialogos;

    public event Action? NuevoSolicitado;
    /// <summary>El shell abre la pantalla de detalles del alquiler (031).</summary>
    public event Action<long>? DetalleSolicitado;

    public AlquileresViewModel(AlquilerService servicio, IDialogService dialogos)
    {
        _servicio = servicio;
        _dialogos = dialogos;
    }

    public ObservableCollection<AlquilerFila> Filas { get; } = [];

    public bool PuedeEditar => SesionActual.TienePermiso(Permisos.VehiculosEditar);

    [ObservableProperty] private string _contadorTexto = string.Empty;

    public async Task CargarAsync()
    {
        try
        {
            var alquileres = await _servicio.ObtenerResumenesAsync();
            OnPropertyChanged(nameof(PuedeEditar));
            Filas.Clear();
            foreach (var a in alquileres)
                Filas.Add(new AlquilerFila(a));
            ContadorTexto = alquileres.Count == 0 ? "Sin alquileres registrados" : $"{alquileres.Count} alquiler(es)";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cargando los alquileres");
            _dialogos.MostrarError("Alquileres", $"No se pudieron cargar los alquileres.\n\n{ex.Message}");
        }
    }

    [RelayCommand]
    private void Nuevo() => NuevoSolicitado?.Invoke();

    /// <summary>
    /// Abre el detalle del alquiler (031). Devolver y cancelar YA NO estan en el
    /// grid: viven adentro, en un solo boton que pregunta cual de las dos es.
    /// El cliente lo pidio asi ("¿los btn devolver y cancelar no hacen
    /// practicamente lo mismo? si es asi, con un solo btn seria suficiente"), y
    /// ademas cerrar un contrato desde una fila de la lista, sin ver el periodo
    /// ni el monto, es demasiado facil de hacer sobre el alquiler equivocado.
    /// </summary>
    [RelayCommand]
    private void VerDetalles(AlquilerFila? fila)
    {
        if (fila is not null)
            DetalleSolicitado?.Invoke(fila.Id);
    }
}
