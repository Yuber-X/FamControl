using System.Windows;
using System.Windows.Controls;
using FAControl.Models;
using FAControl.ViewModels;

namespace FAControl.Views;

/// <summary>
/// Detalle de un alquiler (031). Code-behind solo de UI: abrir los diálogos de
/// cierre y corrección. Las reglas de qué se puede corregir y cómo se cierra el
/// contrato viven en el servicio; los archivos los maneja ExpedienteClienteView,
/// que es el mismo control que usa DealControl.
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
        _vm.RenovacionSolicitada = PedirRenovacion;
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

    /// <summary>El cliente sigue con el auto: hasta cuándo y a qué precio (039).</summary>
    private RenovacionAlquiler? PedirRenovacion(RenovacionAlquilerPedido pedido)
    {
        var ventana = new RenovarAlquilerWindow(pedido) { Owner = Window.GetWindow(this) };
        return ventana.ShowDialog() == true ? ventana.Resultado : null;
    }
}
