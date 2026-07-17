using System.Windows.Controls;
using System.Windows.Input;
using FAControl.ViewModels;

namespace FAControl.Views;

public partial class ClientesListView : UserControl
{
    public ClientesListView() => InitializeComponent();

    // Solo lógica de UI: doble click en una fila abre la ficha
    private void Tabla_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid { SelectedItem: ClienteFila fila } &&
            DataContext is ClientesViewModel vm)
        {
            vm.VerFichaCommand.Execute(fila);
        }
    }
}
