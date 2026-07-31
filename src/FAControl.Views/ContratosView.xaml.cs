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

    // ---------- Expediente del cliente (026) ----------

    /// <summary>
    /// Abre la pantalla dedicada a los archivos de este contrato (pedido de
    /// Yuber 2026-07-31: "en vez de tener un mostrar al lateral, mejor
    /// coloquemos un btn de 'ver contratos'"). El panel lateral se quedaba
    /// corto apenas el cliente tenia mas de un papel.
    ///
    /// Es la MISMA ventana que usa DealControl: el expediente ya sabe de quien
    /// es, asi que no hay una copia por estancia.
    /// </summary>
    private void VerContratos_Click(object sender, RoutedEventArgs e)
    {
        if (_vm?.Seleccionado is not { } contrato)
            return;

        var ventana = new ExpedienteWindow(_vm.Expediente,
            $"Contratos y documentos — {contrato.Resumen.Codigo}",
            contrato.Resumen.ClienteNombre)
        {
            Owner = Window.GetWindow(this)
        };
        ventana.ShowDialog();
    }

    private void MostrarPagare(PagareImpreso pagare)
    {
        // Se le pasa el préstamo para que la copia impresa quede archivada sola
        var ventana = new PagareWindow(pagare, _vm?.DuenoDelSeleccionado, _vm?.Expediente)
        {
            Owner = Window.GetWindow(this)
        };
        ventana.ShowDialog();
    }
}
