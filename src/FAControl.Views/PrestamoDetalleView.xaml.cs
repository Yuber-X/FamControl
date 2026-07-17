using System.Windows;
using System.Windows.Controls;
using FAControl.Models;
using FAControl.Printing;
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
        {
            _vm.ImpresionSolicitada -= MostrarImpresion;
            _vm.IntimacionSolicitada -= MostrarIntimacion;
        }

        _vm = e.NewValue as PrestamoDetalleViewModel;
        if (_vm is not null)
        {
            _vm.ImpresionSolicitada += MostrarImpresion;
            _vm.IntimacionSolicitada += MostrarIntimacion;
        }
    }

    private void MostrarImpresion(PrestamoImpreso prestamo)
    {
        var ventana = new PrestamoImpresionWindow(prestamo) { Owner = Window.GetWindow(this) };
        ventana.ShowDialog();
    }

    private void MostrarIntimacion(IntimacionImpresa intimacion)
    {
        var ventana = new DocumentoPreviewWindow(
            "Intimación de pago",
            $"Requerimiento formal de pago para {intimacion.DeudorNombre} — préstamo {intimacion.CodigoPrestamo}.",
            () => IntimacionDocumentFactory.Crear(intimacion))
        {
            Owner = Window.GetWindow(this)
        };
        ventana.ShowDialog();
    }
}
