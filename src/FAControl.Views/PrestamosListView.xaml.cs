using System.Windows.Controls;
using System.Windows.Input;
using FAControl.ViewModels;

namespace FAControl.Views;

public partial class PrestamosListView : UserControl
{
    public PrestamosListView() => InitializeComponent();

    // Solo lógica de UI: doble click en una fila abre el detalle
    private void Tabla_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid { SelectedItem: PrestamoFila fila } &&
            DataContext is PrestamosViewModel vm)
        {
            vm.VerDetalleCommand.Execute(fila);
        }
    }
}
