using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using FAControl.Models;
using FAControl.Printing;
using FAControl.ViewModels;

namespace FAControl.Views;

/// <summary>
/// Almacén de contratos. Muestra la vista previa del pagaré del contrato
/// seleccionado y abre la ventana completa para verlo/imprimirlo.
/// El FlowDocument se reconstruye en code-behind porque no se puede bindear.
/// </summary>
public partial class ContratosView : UserControl
{
    private ContratosViewModel? _vm;

    public ContratosView() => InitializeComponent();

    private void ContratosView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= Vm_PropertyChanged;
            _vm.PagareSolicitado -= MostrarPagare;
        }
        _vm = e.NewValue as ContratosViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += Vm_PropertyChanged;
            _vm.PagareSolicitado += MostrarPagare;
        }
        ActualizarVistaPrevia();
    }

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ContratosViewModel.VistaPrevia))
            ActualizarVistaPrevia();
    }

    private void ActualizarVistaPrevia() =>
        Visor.Document = _vm?.VistaPrevia is { } pagare
            ? PagareDocumentFactory.Crear(pagare)
            : null;

    // ---------- Expediente del cliente (026) ----------
    // Mismos gestos que en DealControl: subir varios de una, bajar todo en ZIP
    // y doble clic para abrir con la aplicación de Windows que corresponda.

    private async void SubirDocumentos_Click(object sender, RoutedEventArgs e)
    {
        if (_vm?.DuenoDelSeleccionado is null)
            return;

        var dialogo = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Elegí los documentos firmados",
            Multiselect = true,
            Filter = ExpedienteViewModel.FiltroArchivos
        };
        if (dialogo.ShowDialog(Window.GetWindow(this)) == true)
            await _vm.Expediente.AgregarArchivosAsync(dialogo.FileNames);
    }

    private async void ExportarExpediente_Click(object sender, RoutedEventArgs e)
    {
        if (_vm?.Seleccionado is not { } contrato)
            return;

        var dialogo = new Microsoft.Win32.SaveFileDialog
        {
            Title = "¿Dónde guardo el expediente?",
            Filter = "Comprimido (*.zip)|*.zip",
            FileName = $"Expediente_{contrato.Resumen.Codigo}.zip"
        };
        if (dialogo.ShowDialog(Window.GetWindow(this)) == true)
            await _vm.Expediente.ExportarZipAsync(dialogo.FileName);
    }

    private void Documento_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_vm is null || (sender as DataGrid)?.SelectedItem is not DocumentoFila fila)
            return;

        var ventana = new DocumentoAccionesWindow(_vm.Expediente, fila)
        {
            Owner = Window.GetWindow(this)
        };
        ventana.ShowDialog();
    }

    private void MostrarPagare(PagareImpreso pagare)
    {
        // Se le pasa el préstamo para que la copia impresa quede archivada sola
        var ventana = new PagareWindow(pagare, _vm?.DuenoDelSeleccionado, _vm?.Expediente)
        {
            Owner = Window.GetWindow(this)
        };
        ventana.ShowDialog();
    }
}
