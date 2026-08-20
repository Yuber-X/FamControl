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
        _vm = DataContext as VentasViewModel;
        if (_vm is not null)
            _vm.FacturaSolicitada += MostrarFactura;
    }

    private void Desenganchar()
    {
        if (_vm is null)
            return;
        _vm.FacturaSolicitada -= MostrarFactura;
    }

    private void MostrarFactura(FacturaVentaImpresa factura, long ventaId)
    {
        if (_vm is null)
            return;
        new FacturaVentaWindow(factura, ventaId, _vm.Expediente).MostrarDesde(this);
    }
}
