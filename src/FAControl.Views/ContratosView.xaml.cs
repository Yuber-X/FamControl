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
        DataContextChanged += (_, e) =>
        {
            // El handler va en un campo: con un lambda nuevo el -= no
            // desuscribe nada y la ventana se abriría dos veces.
            if (_vm is not null)
                _vm.PagareSolicitado -= MostrarPagare;
            _vm = e.NewValue as ContratosViewModel;
            if (_vm is not null)
                _vm.PagareSolicitado += MostrarPagare;
        };
    }

    private void MostrarPagare(PagareImpreso pagare)
    {
        var ventana = new PagareWindow(pagare) { Owner = Window.GetWindow(this) };
        ventana.ShowDialog();
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
