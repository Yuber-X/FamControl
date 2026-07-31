using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FAControl.ViewModels;
using Microsoft.Win32;

namespace FAControl.Views;

/// <summary>
/// Pantalla dedicada a los archivos de un contrato (pedido del cliente
/// 2026-07-30):
///
///   "en vez de tener un mostrar al lateral, mejor coloquemos un btn de 'ver
///    contratos' donde sería mejor tener una pantalla dedicada a subir
///    contratos, archivos docx, pdf, excel, img, png, etc., con sus iconos;
///    también de un mostrar de 2 formas: 1- en modo listado... 2- en modo
///    iconos... Así se tendría mejor control si hay más de 1 archivo por cliente."
///
/// Sirve a las tres estancias con el MISMO ExpedienteViewModel: préstamos,
/// financiamiento de venta y alquileres. No hay una copia por pantalla — el
/// dueño del expediente ya viaja dentro del ViewModel.
///
/// Code-behind solo de UI: elegir archivos y elegir dónde guardar el ZIP. Qué
/// se puede subir y qué no lo decide el ViewModel.
/// </summary>
public partial class ExpedienteWindow : Window
{
    private readonly ExpedienteViewModel _vm;
    /// <summary>
    /// Guardado en un campo para poder desuscribirlo al cerrar. Con una lambda
    /// nueva en el -= no se quita nada: sería otro delegado.
    /// </summary>
    private readonly NotifyCollectionChangedEventHandler _alCambiarDocumentos;

    public ExpedienteWindow(ExpedienteViewModel expediente, string titulo, string subtitulo)
    {
        InitializeComponent();
        _vm = expediente;
        DataContext = expediente;

        TextoTitulo.Text = titulo;
        TextoSubtitulo.Text = subtitulo;

        // El conteo se sigue a mano: Documentos es una ObservableCollection y no
        // avisa por sí sola cuántos elementos tiene.
        _alCambiarDocumentos = (_, _) => ActualizarConteo();
        _vm.Documentos.CollectionChanged += _alCambiarDocumentos;
        ActualizarConteo();
    }

    private void ActualizarConteo()
    {
        var cantidad = _vm.Documentos.Count;
        TextoConteo.Text = cantidad switch
        {
            0 => string.Empty,
            1 => "1 archivo",
            _ => $"{cantidad} archivos"
        };
    }

    private async void SubirDocumentos_Click(object sender, RoutedEventArgs e)
    {
        var dialogo = new OpenFileDialog
        {
            Title = "Elegí los documentos del cliente",
            Multiselect = true,     // varios de una vez
            Filter = ExpedienteViewModel.FiltroArchivos
        };
        if (dialogo.ShowDialog(this) != true)
            return;

        await _vm.AgregarArchivosAsync(dialogo.FileNames);
    }

    private async void ExportarExpediente_Click(object sender, RoutedEventArgs e)
    {
        var dialogo = new SaveFileDialog
        {
            Title = "¿Dónde guardo el ZIP con todos los archivos?",
            FileName = "Expediente.zip",
            Filter = "Archivo comprimido (*.zip)|*.zip"
        };
        if (dialogo.ShowDialog(this) != true)
            return;

        await _vm.ExportarZipAsync(dialogo.FileName);
    }

    private void Documento_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid grid && grid.SelectedItem is DocumentoFila fila)
            AbrirAcciones(fila);
    }

    private void AccionesDocumento_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is DocumentoFila fila)
            AbrirAcciones(fila);
    }

    private void AbrirAcciones(DocumentoFila fila)
    {
        var ventana = new DocumentoAccionesWindow(_vm, fila) { Owner = this };
        ventana.ShowDialog();
    }

    /// <summary>
    /// El ViewModel es compartido con la pantalla de atrás y vive más que esta
    /// ventana: sin desuscribirse, cada apertura dejaría un handler colgado
    /// apuntando a controles ya destruidos.
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        _vm.Documentos.CollectionChanged -= _alCambiarDocumentos;
        base.OnClosing(e);
    }
}
