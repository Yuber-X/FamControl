using System.Windows;
using System.Windows.Controls;
using FAControl.Models;
using FAControl.Printing;
using FAControl.ViewModels;

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
}
