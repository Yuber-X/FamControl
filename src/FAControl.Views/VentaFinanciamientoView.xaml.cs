using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FAControl.Models;
using FAControl.Printing;
using FAControl.ViewModels;
using Microsoft.Win32;

namespace FAControl.Views;

/// <summary>
/// Financiamiento de una venta del dealer. Code-behind solo para lógica de UI:
/// abrir el visor de la carta de compromiso o del recibo de separación.
/// </summary>
public partial class VentaFinanciamientoView : UserControl
{
    private VentaFinanciamientoViewModel? _vm;

    public VentaFinanciamientoView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (_vm is not null)
            {
                _vm.CartaSolicitada -= MostrarCarta;
                _vm.SeparacionSolicitada -= MostrarSeparacion;
            }
            _vm = e.NewValue as VentaFinanciamientoViewModel;
        if (_vm is not null)
            _vm.CancelacionSolicitada = PedirCancelacion;
            if (_vm is not null)
            {
                _vm.CartaSolicitada += MostrarCarta;
                _vm.SeparacionSolicitada += MostrarSeparacion;
            }
        };
    }

    private void MostrarCarta(CartaCompromisoImpresa carta)
    {
        var ventana = new DocumentoDealWindow("Carta de compromiso",
            $"CartaCompromiso_{carta.Codigo}",
            () => DocumentosDealFactory.CrearCartaCompromiso(carta))
        { Owner = Window.GetWindow(this) };
        ventana.ShowDialog();
    }

    private void MostrarSeparacion(ReciboSeparacionImpreso recibo)
    {
        var ventana = new DocumentoDealWindow("Recibo de separación",
            $"Separacion_{recibo.Codigo}",
            () => DocumentosDealFactory.CrearReciboSeparacion(recibo))
        { Owner = Window.GetWindow(this) };
        ventana.ShowDialog();
    }

    // ---------- Expediente digital (018, pedido 2026-07-27) ----------
    // Los diálogos de archivo son UI pura: la View los abre y le pasa las rutas
    // al ViewModel, que es quien decide qué se puede guardar y qué no.

    private async void SubirDocumentos_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null)
            return;

        var dialogo = new OpenFileDialog
        {
            Title = "Elegí los documentos del cliente",
            Multiselect = true,     // pedido: poder subir varios de una vez
            Filter = ExpedienteViewModel.FiltroArchivos
        };
        if (dialogo.ShowDialog(Window.GetWindow(this)) != true)
            return;

        await _vm.Expediente.AgregarArchivosAsync(dialogo.FileNames);
    }

    private async void ExportarExpediente_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null)
            return;

        var dialogo = new SaveFileDialog
        {
            Title = "¿Dónde guardo el ZIP del expediente?",
            FileName = $"Expediente_{_vm.Codigo}.zip",
            Filter = "Archivo comprimido (*.zip)|*.zip"
        };
        if (dialogo.ShowDialog(Window.GetWindow(this)) != true)
            return;

        await _vm.Expediente.ExportarZipAsync(dialogo.FileName);
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
        if (_vm is null)
            return;
        var ventana = new DocumentoAccionesWindow(_vm.Expediente, fila)
        { Owner = Window.GetWindow(this) };
        ventana.ShowDialog();
    }

    /// <summary>
    /// Pide motivo y porcentaje de retención para cancelar la venta (028).
    /// Devuelve null si el usuario se arrepintió: abrir el diálogo no cancela nada.
    /// </summary>
    private (string Motivo, decimal Porcentaje, bool Fijar)? PedirCancelacion(
        string codigo, decimal cobrado, decimal porcentaje, bool fija)
    {
        var ventana = new CancelarVentaWindow(codigo, cobrado, porcentaje, fija)
        {
            Owner = Window.GetWindow(this)
        };
        return ventana.ShowDialog() == true
            ? (ventana.Motivo, ventana.Porcentaje, ventana.FijarPorcentaje)
            : null;
    }
}
