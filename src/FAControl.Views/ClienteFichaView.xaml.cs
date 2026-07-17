using System.Windows.Controls;
using System.Windows.Input;
using FAControl.ViewModels;

namespace FAControl.Views;

public partial class ClienteFichaView : UserControl
{
    public ClienteFichaView() => InitializeComponent();

    // Solo lógica de UI: doble click en un préstamo abre su detalle
    private void Tabla_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid { SelectedItem: PrestamoFila fila } &&
            DataContext is ClienteFichaViewModel vm)
        {
            vm.VerPrestamoCommand.Execute(fila);
        }
    }
}
