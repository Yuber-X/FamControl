using System.Windows;
using System.Windows.Controls;
using FAControl.Models;
using FAControl.ViewModels;

namespace FAControl.Views;

public partial class PrestamoNuevoView : UserControl
{
    private PrestamoNuevoViewModel? _vm;

    public PrestamoNuevoView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (_vm is not null)
                _vm.PagareSolicitado -= MostrarPagare;
            _vm = e.NewValue as PrestamoNuevoViewModel;
            if (_vm is not null)
                _vm.PagareSolicitado += MostrarPagare;
        };
    }

    // Lógica de UI: abrir la vista previa del pagaré (mismo patrón que el recibo)
    private void MostrarPagare(PagareImpreso pagare)
    {
        var ventana = new PagareWindow(pagare) { Owner = Window.GetWindow(this) };
        ventana.ShowDialog();
    }
}
