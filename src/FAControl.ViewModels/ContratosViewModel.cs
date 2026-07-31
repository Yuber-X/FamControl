using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAControl.Common;
using FAControl.Models;
using FAControl.Services;
using Serilog;

namespace FAControl.ViewModels;

/// <summary>Fila del almacén de contratos.</summary>
public record ContratoFila(PrestamoResumen Resumen)
{
    public long Id => Resumen.Id;
    public string Codigo => Resumen.Codigo;
    public string ClienteNombre => Resumen.ClienteNombre;
    public decimal MontoCapital => Resumen.MontoCapital;
    public string FechaTexto => Resumen.FechaInicio.ToString(Textos.FormatoFecha, Textos.CulturaRd);
    public string EstadoTexto => Textos.De(Resumen.Estado);
}

/// <summary>
/// Almacén de contratos (cliente 2026-07-17): lista de todos los contratos con
/// vista previa lateral del seleccionado y botones para reimprimir y ver
/// completo. El contrato es el pagaré del préstamo.
/// </summary>
public partial class ContratosViewModel : ObservableObject
{
    private readonly ContratoService _contratos;
    private readonly IDialogService _dialogos;
    private List<ContratoFila> _todos = [];

    /// <summary>La App abre la vista completa/impresión del pagaré.</summary>
    public event Action<PagareImpreso>? PagareSolicitado;

    public ContratosViewModel(ContratoService contratos, IDialogService dialogos,
        ExpedienteViewModel expediente)
    {
        _contratos = contratos;
        _dialogos = dialogos;
        Expediente = expediente;
    }

    /// <summary>
    /// Expediente del contrato elegido (026): los papeles firmados del cliente,
    /// con la misma pantalla y las mismas reglas que en DealControl.
    /// </summary>
    public ExpedienteViewModel Expediente { get; }

    /// <summary>Préstamo elegido, para archivar en su expediente lo que se imprime.</summary>
    public DuenoExpediente? DuenoDelSeleccionado =>
        Seleccionado is { } fila ? DuenoExpediente.DePrestamo(fila.Resumen.Id) : null;

    public ObservableCollection<ContratoFila> Contratos { get; } = [];

    [ObservableProperty] private ContratoFila? _seleccionado;
    [ObservableProperty] private string _textoBusqueda = string.Empty;
    [ObservableProperty] private bool _cargando;
    [ObservableProperty] private bool _tieneVistaPrevia;

    /// <summary>Pagaré del contrato seleccionado (para la vista previa lateral).</summary>
    [ObservableProperty] private PagareImpreso? _vistaPrevia;

    partial void OnSeleccionadoChanged(ContratoFila? value)
    {
        OnPropertyChanged(nameof(DuenoDelSeleccionado));
        _ = CargarVistaPreviaAsync(value);
        // El expediente sigue al contrato elegido
        if (value is not null)
            _ = Expediente.CargarAsync(DuenoExpediente.DePrestamo(value.Resumen.Id));
    }

    partial void OnTextoBusquedaChanged(string value) => Filtrar();

    public async Task CargarAsync()
    {
        try
        {
            Cargando = true;
            var resumenes = await _contratos.ObtenerContratosAsync();
            _todos = resumenes.Select(r => new ContratoFila(r)).ToList();
            Filtrar();
            // Selecciona el primero para que la vista previa no arranque vacía
            Seleccionado = Contratos.FirstOrDefault();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cargando el almacén de contratos");
            _dialogos.MostrarError("Contratos", $"No se pudieron cargar los contratos.\n\n{ex.Message}");
        }
        finally
        {
            Cargando = false;
        }
    }

    private void Filtrar()
    {
        var texto = TextoBusqueda?.Trim() ?? string.Empty;
        var filtrados = string.IsNullOrEmpty(texto)
            ? _todos
            : _todos.Where(c =>
                c.ClienteNombre.Contains(texto, StringComparison.OrdinalIgnoreCase) ||
                c.Codigo.Contains(texto, StringComparison.OrdinalIgnoreCase)).ToList();

        Contratos.Clear();
        foreach (var fila in filtrados)
            Contratos.Add(fila);
    }

    private async Task CargarVistaPreviaAsync(ContratoFila? fila)
    {
        if (fila is null)
        {
            VistaPrevia = null;
            TieneVistaPrevia = false;
            return;
        }

        try
        {
            VistaPrevia = await _contratos.ArmarPagareAsync(fila.Id);
            TieneVistaPrevia = true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error armando el pagaré del contrato {Id}", fila.Id);
            _dialogos.MostrarError("Contratos", $"No se pudo cargar el contrato.\n\n{ex.Message}");
            TieneVistaPrevia = false;
        }
    }

    /// <summary>Abre el pagaré completo (misma ventana que imprime).</summary>
    [RelayCommand]
    private void VerCompleto()
    {
        if (VistaPrevia is not null)
            PagareSolicitado?.Invoke(VistaPrevia);
    }
}
