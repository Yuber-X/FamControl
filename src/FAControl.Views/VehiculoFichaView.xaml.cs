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

        // Se engancha al ViewModel (que es SINGLETON) mientras esta vista esté
        // en pantalla, y se suelta al salir. Sin el Unloaded, cada "cerrar
        // sesión" dejaba una vista muerta suscrita: el evento la seguía
        // llamando y ella intentaba abrir ventanas colgando de un shell ya
        // cerrado (cliente 2026-08-20). Loaded vuelve a enganchar si WPF
        // recicla la instancia.
        DataContextChanged += (_, _) => Reenganchar();
        Loaded += (_, _) => Reenganchar();
        Unloaded += (_, _) => Desenganchar();
    }

    private void Reenganchar()
    {
        Desenganchar();
        _vm = DataContext as VehiculoFichaViewModel;
        if (_vm is not null)
            _vm.ImpresionSolicitada += MostrarImpresion;
    }

    private void Desenganchar()
    {
        if (_vm is null)
            return;
        _vm.ImpresionSolicitada -= MostrarImpresion;
    }

    private void MostrarImpresion(FichaVehiculoImpresa ficha)
    {
        new FichaVehiculoWindow(ficha).MostrarDesde(this);
    }
}
