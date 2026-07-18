using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAControl.Common;
using FAControl.Models;
using FAControl.Services;
using Serilog;

namespace FAControl.ViewModels;

/// <summary>Fila de la lista de ventas al contado.</summary>
public record VentaFila(VentaResumen Resumen)
{
    public long Id => Resumen.Id;
    public string Codigo => Resumen.Codigo;
    public string Vehiculo => Resumen.VehiculoDescripcion;
    public string Cliente => Resumen.ClienteNombre;
    public string FechaTexto => Resumen.FechaVentaUtc.ToLocalTime().ToString(Textos.FormatoFecha, Textos.CulturaRd);
    public decimal Precio => Resumen.Precio;
    public string MetodoTexto => Textos.De(Resumen.MetodoPago);
}

/// <summary>Lista de ventas al contado (DealerControl). El alta la abre el shell.</summary>
public partial class VentasViewModel : ObservableObject
{
    private readonly VentaVehiculoService _servicio;
    private readonly IDialogService _dialogos;

    public event Action? NuevoSolicitado;

    public VentasViewModel(VentaVehiculoService servicio, IDialogService dialogos)
    {
        _servicio = servicio;
        _dialogos = dialogos;
    }

    public ObservableCollection<VentaFila> Filas { get; } = [];

    public bool PuedeEditar => SesionActual.TienePermiso(Permisos.VehiculosEditar);

    [ObservableProperty] private string _contadorTexto = string.Empty;

    public async Task CargarAsync()
    {
        try
        {
            var ventas = await _servicio.ObtenerResumenesAsync();
            OnPropertyChanged(nameof(PuedeEditar));
            Filas.Clear();
            foreach (var v in ventas)
                Filas.Add(new VentaFila(v));
            ContadorTexto = ventas.Count == 0 ? "Sin ventas registradas" : $"{ventas.Count} venta(s)";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cargando las ventas al contado");
            _dialogos.MostrarError("Ventas", $"No se pudieron cargar las ventas.\n\n{ex.Message}");
        }
    }

    [RelayCommand]
    private void Nuevo() => NuevoSolicitado?.Invoke();
}
