using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FAControl.Models;
using FAControl.ViewModels;

namespace FAControl.Views;

/// <summary>
/// Almacén de contratos. Code-behind solo de UI: el doble clic en una fila
/// entra a los archivos de ese contrato, que es la acción más frecuente, y el
/// botón "Pagaré" abre la ventana de vista previa/impresión.
///
/// La vista previa lateral del pagaré vivía acá y se quitó el 2026-08-01; con
/// ella se fue el manejo del FlowDocument y, sin querer, la suscripción a
/// PagareSolicitado. El evento quedó disparando al vacío: el botón no hacía
/// nada. Ahora abre la misma ventana que usa PrestamoNuevoView.
/// </summary>
public partial class ContratosView : UserControl
{
    private ContratosViewModel? _vm;

    public ContratosView()
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
        _vm = DataContext as ContratosViewModel;
        if (_vm is not null)
            _vm.PagareSolicitado += MostrarPagare;
    }

    private void Desenganchar()
    {
        if (_vm is null)
            return;
        _vm.PagareSolicitado -= MostrarPagare;
    }

    private void MostrarPagare(PagareImpreso pagare)
    {
        new PagareWindow(pagare).MostrarDesde(this);
    }

    private void Tabla_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid { SelectedItem: ContratoFila fila } &&
            DataContext is ContratosViewModel vm)
        {
            vm.VerArchivosCommand.Execute(fila);
        }
    }
}
