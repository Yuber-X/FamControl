using System.Windows;
using System.Windows.Controls;
using FAControl.Models;
using FAControl.ViewModels;

namespace FAControl.Views;

/// <summary>
/// Lista de ventas al contado. Code-behind solo para lógica de UI: abrir la
/// vista previa de la factura cuando el ViewModel lo pide (2026-07-25).
/// </summary>
public partial class VentasView : UserControl
{
    private VentasViewModel? _vm;

    public VentasView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (_vm is not null)
                _vm.FacturaSolicitada -= MostrarFactura;
            _vm = e.NewValue as VentasViewModel;
            if (_vm is not null)
                _vm.FacturaSolicitada += MostrarFactura;
        };
    }

    private void MostrarFactura(FacturaVentaImpresa factura)
    {
        var ventana = new FacturaVentaWindow(factura) { Owner = Window.GetWindow(this) };
        ventana.ShowDialog();
    }
}
