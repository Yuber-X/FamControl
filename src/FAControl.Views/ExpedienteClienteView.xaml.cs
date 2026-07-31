using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FAControl.ViewModels;
using Microsoft.Win32;

namespace FAControl.Views;

/// <summary>
/// Expediente del cliente: los papeles que entregó, con sus dos modos de ver.
///
/// CONTROL COMPARTIDO (2026-07-31, pedido de Yuber: "¿no sería mejor hacer una
/// réplica del expediente del cliente que ya tiene DealControl?"). Nació dentro
/// de Financiamiento de venta y funcionaba bien, así que en vez de escribirlo
/// dos veces más se sacó acá y lo usan las tres estancias.
///
/// NO MEZCLA DATOS: cada pantalla le pasa SU propio ExpedienteViewModel como
/// DataContext, y ese ViewModel ya sabe de qué contrato cuelga —una venta, un
/// préstamo o un alquiler—. Este control no decide nada, solo muestra.
///
/// Code-behind solo de UI: abrir los diálogos de archivo de Windows y pasarle
/// las rutas al ViewModel, que es quien decide qué se puede guardar.
/// </summary>
public partial class ExpedienteClienteView : UserControl
{
    public ExpedienteClienteView() => InitializeComponent();

    /// <summary>El expediente que le tocó mostrar, o null si todavía no hay.</summary>
    private ExpedienteViewModel? Vm => DataContext as ExpedienteViewModel;

    private async void SubirDocumentos_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm)
            return;

        var dialogo = new OpenFileDialog
        {
            Title = "Elegí los documentos del cliente",
            Multiselect = true,     // pedido: poder subir varios de una vez
            Filter = ExpedienteViewModel.FiltroArchivos
        };
        if (dialogo.ShowDialog(Window.GetWindow(this)) != true)
            return;

        await vm.AgregarArchivosAsync(dialogo.FileNames);
    }

    private async void ExportarExpediente_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm)
            return;

        var dialogo = new SaveFileDialog
        {
            Title = "¿Dónde guardo el ZIP del expediente?",
            FileName = "Expediente.zip",
            Filter = "Archivo comprimido (*.zip)|*.zip"
        };
        if (dialogo.ShowDialog(Window.GetWindow(this)) != true)
            return;

        await vm.ExportarZipAsync(dialogo.FileName);
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
        if (Vm is not { } vm)
            return;

        var ventana = new DocumentoAccionesWindow(vm, fila) { Owner = Window.GetWindow(this) };
        ventana.ShowDialog();
    }
}
