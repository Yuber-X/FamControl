using System.Windows.Controls;
using System.Windows.Input;
using FAControl.ViewModels;

namespace FAControl.Views;

public partial class VehiculosListView : UserControl
{
    public VehiculosListView() => InitializeComponent();

    // Solo lógica de UI: doble click en una fila abre la edición (si tiene permiso)
    private void Tabla_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid { SelectedItem: VehiculoFila fila } &&
            DataContext is VehiculosViewModel { PuedeEditar: true } vm)
        {
            vm.EditarCommand.Execute(fila);
        }
    }
}
