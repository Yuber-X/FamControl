using System.Windows;
using System.Windows.Controls;
using FAControl.Models;
using FAControl.ViewModels;

namespace FAControl.Views;

/// <summary>
/// Ficha del vehículo. Code-behind solo para lógica de UI: abrir la vista
/// previa imprimible cuando el ViewModel lo pide.
/// </summary>
public partial class VehiculoFichaView : UserControl
{
    private VehiculoFichaViewModel? _vm;

    public VehiculoFichaView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (_vm is not null)
                _vm.ImpresionSolicitada -= MostrarImpresion;
            _vm = e.NewValue as VehiculoFichaViewModel;
            if (_vm is not null)
                _vm.ImpresionSolicitada += MostrarImpresion;
        };
    }

    private void MostrarImpresion(FichaVehiculoImpresa ficha)
    {
        var ventana = new FichaVehiculoWindow(ficha) { Owner = Window.GetWindow(this) };
        ventana.ShowDialog();
    }
}
