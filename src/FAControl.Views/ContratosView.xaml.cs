using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using FAControl.Models;
using FAControl.Printing;
using FAControl.ViewModels;

namespace FAControl.Views;

/// <summary>
/// Almacén de contratos. Muestra la vista previa del pagaré del contrato
/// seleccionado y abre la ventana completa para verlo/imprimirlo.
/// El FlowDocument se reconstruye en code-behind porque no se puede bindear.
/// </summary>
public partial class ContratosView : UserControl
{
    private ContratosViewModel? _vm;

    public ContratosView() => InitializeComponent();

    private void ContratosView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= Vm_PropertyChanged;
            _vm.PagareSolicitado -= MostrarPagare;
        }
        _vm = e.NewValue as ContratosViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += Vm_PropertyChanged;
            _vm.PagareSolicitado += MostrarPagare;
        }
        ActualizarVistaPrevia();
    }

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ContratosViewModel.VistaPrevia))
            ActualizarVistaPrevia();
    }

    private void ActualizarVistaPrevia() =>
        Visor.Document = _vm?.VistaPrevia is { } pagare
            ? PagareDocumentFactory.Crear(pagare)
            : null;

    private void MostrarPagare(PagareImpreso pagare)
    {
        var ventana = new PagareWindow(pagare) { Owner = Window.GetWindow(this) };
        ventana.ShowDialog();
    }
}
