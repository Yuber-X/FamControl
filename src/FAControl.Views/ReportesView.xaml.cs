using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using FAControl.Common;
using FAControl.Models;
using FAControl.Printing;
using FAControl.ViewModels;

namespace FAControl.Views;

public partial class ReportesView : UserControl
{
    private ReportesViewModel? _vm;

    public ReportesView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (_vm is not null)
                _vm.ImpresionSolicitada -= MostrarReporte;
            _vm = e.NewValue as ReportesViewModel;
            if (_vm is not null)
                _vm.ImpresionSolicitada += MostrarReporte;
        };
    }

    // Lógica de UI: abrir la vista previa imprimible del reporte de clientes
    private void MostrarReporte(ReporteClientesImpreso reporte)
    {
        var ventana = new DocumentoPreviewWindow(
            reporte.Titulo, reporte.Titulo,
            () => ReporteClientesDocumentFactory.Crear(reporte))
        {
            Owner = Window.GetWindow(this)
        };
        ventana.ShowDialog();
    }

    // Solo lógica de UI: pedir la ruta y delegar al ViewModel
    private async void BotonExportar_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ReportesViewModel vm)
            return;

        var dialogo = new SaveFileDialog
        {
            Title = "Exportar datos a Excel",
            FileName = NombreExport.Sugerido(),   // termina con el modo (2026-08-01)
            Filter = "Libro de Excel (*.xlsx)|*.xlsx"
        };
        if (dialogo.ShowDialog(Window.GetWindow(this)) == true)
            await vm.ExportarAsync(dialogo.FileName);
    }
}
