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

        // Se engancha al ViewModel (que es SINGLETON) mientras esta vista esté
        // en pantalla, y se suelta al salir. Sin el Unloaded, cada "cerrar
        // sesión" dejaba una vista muerta suscrita: el evento la seguía
        // llamando y ella intentaba abrir ventanas colgando de un shell ya
        // cerrado (cliente 2026-08-20). Loaded vuelve a enganchar si WPF
        // recicla la instancia.
        DataContextChanged += (_, _) => Reenganchar();
        Loaded += (_, _) => Reenganchar();
        Unloaded += (_, _) => Desenganchar();
    }

    private void Reenganchar()
    {
        Desenganchar();
        _vm = DataContext as ReportesViewModel;
        if (_vm is not null)
            _vm.ImpresionSolicitada += MostrarReporte;
    }

    private void Desenganchar()
    {
        if (_vm is null)
            return;
        _vm.ImpresionSolicitada -= MostrarReporte;
    }

    // Lógica de UI: abrir la vista previa imprimible del reporte de clientes
    private void MostrarReporte(ReporteClientesImpreso reporte)
    {
        var ventana = new DocumentoPreviewWindow(
            reporte.Titulo, reporte.Titulo,
            () => ReporteClientesDocumentFactory.Crear(reporte));
        ventana.MostrarDesde(this);
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
