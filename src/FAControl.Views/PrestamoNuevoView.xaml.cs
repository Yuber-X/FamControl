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
        _vm = DataContext as PrestamoNuevoViewModel;
        if (_vm is not null)
            _vm.PagareSolicitado += MostrarPagare;
    }

    private void Desenganchar()
    {
        if (_vm is null)
            return;
        _vm.PagareSolicitado -= MostrarPagare;
    }

    // Lógica de UI: abrir la vista previa del pagaré (mismo patrón que el recibo)
    private void MostrarPagare(PagareImpreso pagare)
    {
        new PagareWindow(pagare).MostrarDesde(this);
    }
}
