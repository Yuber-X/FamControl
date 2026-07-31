using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FAControl.Models;
using FAControl.ViewModels;
using Microsoft.Win32;

namespace FAControl.Views;

/// <summary>
/// Detalle de un alquiler (031). Code-behind solo de UI: abrir diálogos y
/// elegir archivos. Las reglas de qué se puede corregir y cómo se cierra el
/// contrato viven en el servicio.
/// </summary>
public partial class AlquilerDetalleView : UserControl
{
    private AlquilerDetalleViewModel? _vm;

    public AlquilerDetalleView() => InitializeComponent();

    private void AlquilerDetalleView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        _vm = e.NewValue as AlquilerDetalleViewModel;
        if (_vm is null)
            return;

        _vm.CierreSolicitado = PedirCierre;
        _vm.EdicionSolicitada = PedirCorreccion;
    }

    /// <summary>
    /// Pregunta cómo terminó el alquiler. Devuelve null si el usuario se
    /// arrepintió: abrir el diálogo no cierra nada.
    /// </summary>
    private CierreAlquilerDatos? PedirCierre(CierreAlquilerPedido pedido)
    {
        var ventana = new CerrarAlquilerWindow(pedido) { Owner = Window.GetWindow(this) };
        return ventana.ShowDialog() == true ? ventana.Resultado : null;
    }

    private EdicionAlquiler? PedirCorreccion(AlquilerParaEditar datos)
    {
        var ventana = new EditarAlquilerWindow(datos) { Owner = Window.GetWindow(this) };
        return ventana.ShowDialog() == true ? ventana.Resultado : null;
    }

    /// <summary>Los archivos del alquiler, en la misma pantalla dedicada del resto.</summary>
    private void VerContratos_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null)
            return;

        var ventana = new ExpedienteWindow(_vm.Expediente,
            $"Contrato y documentos — {_vm.Codigo}", _vm.ClienteNombre)
        {
            Owner = Window.GetWindow(this)
        };
        ventana.ShowDialog();
    }

    private async void SubirDocumentos_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null)
            return;

        var dialogo = new OpenFileDialog
        {
            Title = "Elegí los documentos del alquiler",
            Multiselect = true,
            Filter = ExpedienteViewModel.FiltroArchivos
        };
        if (dialogo.ShowDialog(Window.GetWindow(this)) != true)
            return;

        await _vm.Expediente.AgregarArchivosAsync(dialogo.FileNames);
    }

    private void Documento_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_vm is null || (sender as DataGrid)?.SelectedItem is not DocumentoFila fila)
            return;

        var ventana = new DocumentoAccionesWindow(_vm.Expediente, fila)
        {
            Owner = Window.GetWindow(this)
        };
        ventana.ShowDialog();
    }
}
