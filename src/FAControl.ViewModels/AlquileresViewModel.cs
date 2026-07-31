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

/// <summary>Lista de alquileres (DealerControl). Permite devolver/cancelar los activos.</summary>
public partial class AlquileresViewModel : ObservableObject
{
    private readonly AlquilerService _servicio;
    private readonly IDialogService _dialogos;

    public event Action? NuevoSolicitado;

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

    [RelayCommand]
    private async Task DevolverAsync(AlquilerFila? fila)
    {
        if (fila is null || !fila.EstaActivo)
            return;
        if (!_dialogos.Confirmar("Registrar devolución",
                $"¿Marcar el alquiler {fila.Codigo} como devuelto? El vehículo vuelve a estar disponible."))
            return;
        await CerrarAsync(fila, cancelado: false);
    }

    [RelayCommand]
    private async Task CancelarAsync(AlquilerFila? fila)
    {
        if (fila is null || !fila.EstaActivo)
            return;
        if (!_dialogos.Confirmar("Cancelar alquiler",
                $"¿Cancelar el alquiler {fila.Codigo}? El vehículo vuelve a estar disponible."))
            return;
        await CerrarAsync(fila, cancelado: true);
    }

    private async Task CerrarAsync(AlquilerFila fila, bool cancelado)
    {
        try
        {
            await _servicio.CerrarAsync(fila.Id, cancelado);
            await CargarAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cerrando el alquiler {Id}", fila.Id);
            _dialogos.MostrarError("Alquileres", ex.Message);
        }
    }
}
