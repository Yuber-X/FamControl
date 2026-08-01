using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FAControl.ViewModels;

namespace FAControl.Views;

public partial class ClientesListView : UserControl
{
    public ClientesListView()
    {
        InitializeComponent();

        // Los encabezados y la columna de alquileres se ajustan a la estancia
        // ACÁ y no con un Binding: las columnas de un DataGrid no viven en el
        // árbol visual, así que no heredan DataContext y un Binding en Header o
        // en Visibility no resuelve. Es una limitación conocida de WPF, y
        // esquivarla con un proxy congelado sería más código que esto.
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is not ClientesViewModel vm)
                return;

            ColumnaContratos.Header = vm.TituloContratos;
            ColumnaSaldo.Header = vm.TituloSaldo;
            // El dealer alquila; PrestControl no. Donde no aplica, la columna
            // no se muestra vacía: no se muestra.
            ColumnaAlquileres.Visibility = vm.EsDealer ? Visibility.Visible : Visibility.Collapsed;
        };
    }

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
