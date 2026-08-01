using System.Windows.Controls;
using System.Windows.Input;
using FAControl.ViewModels;

namespace FAControl.Views;

/// <summary>
/// Almacén de contratos. Code-behind solo de UI: el doble clic en una fila
/// entra a los archivos de ese contrato, que es la acción más frecuente.
///
/// La vista previa del pagaré vivía acá y se quitó el 2026-08-01; con ella se
/// fue todo el manejo del FlowDocument, que era la única razón por la que esta
/// clase tenía lógica.
/// </summary>
public partial class ContratosView : UserControl
{
    public ContratosView() => InitializeComponent();

    private void Tabla_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid { SelectedItem: ContratoFila fila } &&
            DataContext is ContratosViewModel vm)
        {
            vm.VerArchivosCommand.Execute(fila);
        }
    }
}
