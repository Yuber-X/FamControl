using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAControl.Common;
using FAControl.Models;
using FAControl.Services;
using Serilog;

namespace FAControl.ViewModels;

/// <summary>Fila del expediente de contratos del dealer.</summary>
public record ContratoDealItem(ContratoDealFila Fila)
{
    public long VentaId => Fila.VentaId;
    public string Codigo => Fila.Codigo;
    public string FechaTexto =>
        FechaNegocio.AUtcLocal(Fila.FechaUtc).ToString(Textos.FormatoFecha, Textos.CulturaRd);
    public string ClienteNombre => Fila.ClienteNombre;
    public string VendedorNombre => Fila.VendedorNombre;
    public string VehiculoDescripcion => Fila.VehiculoDescripcion;
    public string MatriculaTexto =>
        string.IsNullOrWhiteSpace(Fila.Matricula) ? (Fila.Placa ?? "—") : Fila.Matricula!;
    public decimal Precio => Fila.Precio;
    public int CantidadDocumentos => Fila.CantidadDocumentos;
    public bool TienePlan => Fila.TienePlan;
    public decimal Pendiente => Fila.Pendiente;

    public string TipoTexto => Fila.TipoVenta switch
    {
        TipoVenta.Plazos => "Por plazos",
        TipoVenta.Separacion => "Separación",
        _ => "Contado"
    };

    /// <summary>"3 de 12 pagados · 1 atrasado" — el resumen que pidió el cliente.</summary>
    public string EstadoPagosTexto => Fila.TipoVenta switch
    {
        TipoVenta.Contado => "Saldado al contado",
        TipoVenta.Separacion => Fila.Pendiente <= 0m
            ? "Separación completada"
            : $"Falta {Fila.Pendiente.ToString("N2", Textos.CulturaRd)} por completar",
        _ => $"{Fila.PlazosPagados} de {Fila.PlazosTotales} pagados" +
             (Fila.PlazosAtrasados > 0 ? $" · {Fila.PlazosAtrasados} atrasado(s)" : "")
    };

    public bool TieneAtrasos => Fila.PlazosAtrasados > 0;
}

/// <summary>
/// Expediente de contratos de DealControl (pedido 2026-07-25): por venta se ve
/// el cliente, el usuario que vendió, la cantidad de documentos, la matrícula
/// del auto y el estado de los plazos; "ver detalles" abre el financiamiento
/// con todo del cliente, el vehículo y sus plazos.
/// </summary>
public partial class ContratosDealViewModel : ObservableObject
{
    private readonly ReporteDealService _servicio;
    private readonly IDialogService _dialogos;
    private IReadOnlyList<ContratoDealFila> _todos = [];

    /// <summary>El shell abre el detalle/financiamiento de la venta.</summary>
    public event Action<long>? DetalleSolicitado;

    public ContratosDealViewModel(ReporteDealService servicio, IDialogService dialogos)
    {
        _servicio = servicio;
        _dialogos = dialogos;
    }

    public ObservableCollection<ContratoDealItem> Filas { get; } = [];

    [ObservableProperty] private string _textoBusqueda = string.Empty;
    [ObservableProperty] private string _contadorTexto = string.Empty;

    partial void OnTextoBusquedaChanged(string value) => AplicarFiltro();

    public async Task CargarAsync()
    {
        try
        {
            _todos = await _servicio.ObtenerContratosAsync();
            AplicarFiltro();
        }
        catch (UnauthorizedAccessException ex)
        {
            ContadorTexto = ex.Message;
            Filas.Clear();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cargando los contratos del dealer");
            _dialogos.MostrarError("Contratos", $"No se pudieron cargar los contratos.\n\n{ex.Message}");
        }
    }

    private void AplicarFiltro()
    {
        var filtro = TextoBusqueda.Trim();
        var visibles = _todos.Where(f =>
            string.IsNullOrEmpty(filtro) ||
            f.Codigo.Contains(filtro, StringComparison.OrdinalIgnoreCase) ||
            f.ClienteNombre.Contains(filtro, StringComparison.OrdinalIgnoreCase) ||
            f.VendedorNombre.Contains(filtro, StringComparison.OrdinalIgnoreCase) ||
            f.VehiculoDescripcion.Contains(filtro, StringComparison.OrdinalIgnoreCase) ||
            (f.Matricula?.Contains(filtro, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (f.Placa?.Contains(filtro, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();

        Filas.Clear();
        foreach (var fila in visibles)
            Filas.Add(new ContratoDealItem(fila));

        ContadorTexto = visibles.Count == 0
            ? "Sin contratos registrados"
            : $"{visibles.Count} contrato(s)" +
              (visibles.Count(f => f.PlazosAtrasados > 0) is var conAtraso && conAtraso > 0
                  ? $" · {conAtraso} con plazos atrasados"
                  : "");
    }

    [RelayCommand]
    private void VerDetalles(ContratoDealItem? item)
    {
        if (item is not null)
            DetalleSolicitado?.Invoke(item.VentaId);
    }
}
