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
/// Almacén de contratos de PrestControl. El contrato es el pagaré del préstamo.
///
/// La vista previa lateral se quitó el 2026-08-01 ("ya es innecesaria"):
/// ocupaba media pantalla para mostrar en miniatura algo que igual había que
/// abrir grande para leer. Ahora la lista usa todo el ancho y cada fila lleva
/// sus acciones: Archivos (el expediente, en su propia pantalla) y Pagaré.
/// </summary>
public partial class ContratosViewModel : ObservableObject
{
    private readonly ContratoService _contratos;
    private readonly IDialogService _dialogos;
    private List<ContratoFila> _todos = [];

    /// <summary>
    /// La App abre la ventana con los TRES contratos del préstamo (2026-09-03).
    /// Antes esto mandaba un solo <c>PagareImpreso</c> y abría el pagaré directo.
    /// </summary>
    public event Action<PagareNotarialImpreso, DuenoExpediente>? ContratosSolicitados;

    /// <summary>El shell navega a la pantalla del expediente de ese contrato.</summary>
    public event Action<long>? ArchivosSolicitados;

    public ContratosViewModel(ContratoService contratos, IDialogService dialogos,
        ExpedienteViewModel expediente)
    {
        _contratos = contratos;
        _dialogos = dialogos;
        Expediente = expediente;
    }

    /// <summary>
    /// El expediente donde se archiva lo que se imprima (2026-09-03). La vista
    /// se lo pasa a la ventana de contratos; aquí no se usa para nada más.
    /// </summary>
    public ExpedienteViewModel Expediente { get; }

    public ObservableCollection<ContratoFila> Contratos { get; } = [];

    [ObservableProperty] private ContratoFila? _seleccionado;
    [ObservableProperty] private string _textoBusqueda = string.Empty;
    [ObservableProperty] private bool _cargando;
    [ObservableProperty] private string _contadorTexto = string.Empty;

    partial void OnTextoBusquedaChanged(string value) => Filtrar();

    public async Task CargarAsync()
    {
        try
        {
            Cargando = true;
            var resumenes = await _contratos.ObtenerContratosAsync();
            _todos = resumenes.Select(r => new ContratoFila(r)).ToList();
            Filtrar();
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

        ContadorTexto = _todos.Count == 0
            ? "Sin contratos registrados"
            : filtrados.Count == _todos.Count
                ? $"{_todos.Count} contrato(s)"
                : $"{filtrados.Count} de {_todos.Count} contrato(s)";
    }

    /// <summary>
    /// Entra a la pantalla del expediente de ese contrato: los papeles que
    /// entregó el cliente. Es una PÁGINA, no una ventana suelta.
    /// </summary>
    [RelayCommand]
    private void VerArchivos(ContratoFila? fila)
    {
        if (fila is not null)
            ArchivosSolicitados?.Invoke(fila.Id);
    }

    /// <summary>
    /// Abre los contratos del préstamo para verlos o imprimirlos. Antes esto
    /// salía de la vista previa lateral; al sacarla, la acción pasó a la fila
    /// para no perder la posibilidad de imprimir.
    ///
    /// Desde el 2026-09-03 no abre el pagaré directo: abre la ventana con los
    /// tres documentos, cada uno con su vista previa e impresión.
    /// </summary>
    [RelayCommand]
    private async Task VerContratosAsync(ContratoFila? fila)
    {
        if (fila is null)
            return;

        try
        {
            // Se arma el NOTARIAL porque trae adentro el pagaré común: los tres
            // documentos salen de la misma consulta.
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
