using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FAControl.ViewModels;

namespace FAControl.Views;

public partial class VehiculosListView : UserControl
{
    public VehiculosListView()
    {
        InitializeComponent();
        // Costo y ganancia ocultos al Vendedor (2026-07-25). Va en code-behind
        // porque las columnas del DataGrid no participan del árbol visual.
        DataContextChanged += (_, _) => AplicarVisibilidadCostos();
        Loaded += (_, _) => AplicarVisibilidadCostos();
    }

    private void AplicarVisibilidadCostos()
    {
        var visibles = DataContext is VehiculosViewModel { PuedeVerCostos: true };
        ColumnaCosto.Visibility = visibles ? Visibility.Visible : Visibility.Collapsed;
        ColumnaGanancia.Visibility = visibles ? Visibility.Visible : Visibility.Collapsed;
    }

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
