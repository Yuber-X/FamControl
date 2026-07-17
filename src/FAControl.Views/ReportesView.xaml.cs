using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using FAControl.Common;
using FAControl.ViewModels;

namespace FAControl.Views;

public partial class ReportesView : UserControl
{
    public ReportesView() => InitializeComponent();

    // Solo lógica de UI: pedir la ruta y delegar al ViewModel
    private async void BotonExportar_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ReportesViewModel vm)
            return;

        var dialogo = new SaveFileDialog
        {
            Title = "Exportar datos a Excel",
            FileName = $"FAControl_Export_{FechaNegocio.Hoy:yyyy-MM-dd}.xlsx",
            Filter = "Libro de Excel (*.xlsx)|*.xlsx"
        };
        if (dialogo.ShowDialog(Window.GetWindow(this)) == true)
            await vm.ExportarAsync(dialogo.FileName);
    }
}
