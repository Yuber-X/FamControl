using System.Windows.Controls;
using System.Windows.Input;
using FAControl.Models;
using FAControl.ViewModels;

namespace FAControl.Views;

public partial class PrestamosListView : UserControl
{
    private PrestamosViewModel? _vm;

    public PrestamosListView()
    {
        InitializeComponent();

        // Mismo patrón que las demás vistas: el ViewModel es SINGLETON y una
        // vista que ya no está en pantalla no puede seguir abriendo ventanas
        // (cliente 2026-08-20).
        DataContextChanged += (_, _) => Reenganchar();
        Loaded += (_, _) => Reenganchar();
        Unloaded += (_, _) => Desenganchar();
    }

    private void Reenganchar()
    {
        Desenganchar();
        _vm = DataContext as PrestamosViewModel;
        if (_vm is not null)
            _vm.ContratosSolicitados += MostrarContratos;
    }

    private void Desenganchar()
    {
        if (_vm is null)
            return;
        _vm.ContratosSolicitados -= MostrarContratos;
    }

    private void MostrarContratos(PagareNotarialImpreso contrato, DuenoExpediente dueno)
    {
        new ContratosWindow(contrato, TipoContrato.Pagare, dueno, _vm?.Expediente)
            .MostrarDesde(this);
    }

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
