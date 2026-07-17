using System.Windows;
using System.Windows.Controls;
using FAControl.Models;
using FAControl.ViewModels;

namespace FAControl.Views;

public partial class PrestamoDetalleView : UserControl
{
    private PrestamoDetalleViewModel? _vm;

    public PrestamoDetalleView() => InitializeComponent();

    // Lógica de UI: abrir la vista previa imprimible (mismo patrón que CobrosView)
    private void PrestamoDetalleView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
            _vm.ImpresionSolicitada -= MostrarImpresion;

        _vm = e.NewValue as PrestamoDetalleViewModel;
        if (_vm is not null)
            _vm.ImpresionSolicitada += MostrarImpresion;
    }

    private void MostrarImpresion(PrestamoImpreso prestamo)
    {
        var ventana = new PrestamoImpresionWindow(prestamo) { Owner = Window.GetWindow(this) };
        ventana.ShowDialog();
    }
}
