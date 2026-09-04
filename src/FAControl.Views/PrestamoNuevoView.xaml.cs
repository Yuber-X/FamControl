using System.Windows;
using System.Windows.Controls;
using FAControl.Models;
using FAControl.Printing;
using FAControl.ViewModels;
using Serilog;

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
        if (_vm is null)
            return;
        _vm.ContratosParaImprimir += AtenderContratos;
        _vm.VistaPreviaCambiada += RedibujarLateral;
    }

    private void Desenganchar()
    {
        if (_vm is null)
            return;
        _vm.ContratosParaImprimir -= AtenderContratos;
        _vm.VistaPreviaCambiada -= RedibujarLateral;
    }

    /// <summary>
    /// Con la lista vacía se abre la ventana de vista previa para que el
    /// usuario mire y decida; con contratos adentro se mandan directo a la
    /// impresora, que es lo que hace "Crear e imprimir".
    /// </summary>
    private void AtenderContratos(PagareNotarialImpreso contrato, DuenoExpediente? dueno,
        IReadOnlyList<TipoContrato> aImprimir)
    {
        if (aImprimir.Count == 0)
        {
            new ContratosWindow(contrato, TipoContrato.Pagare, dueno, _vm?.Expediente)
                .MostrarDesde(this);
            return;
        }

        // Se imprimen en orden y se sigue con el siguiente aunque uno falle: si
        // la impresora se queda sin papel en el segundo documento, el tercero
        // igual tiene que salir. Los errores se juntan y se avisan una sola vez.
        var fallaron = new List<string>();
        foreach (var tipo in aImprimir)
        {
            try
            {
                ImpresionDeContratos.ImprimirYArchivar(tipo, contrato, dueno, _vm?.Expediente);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error imprimiendo el contrato {Tipo} del préstamo {Codigo}",
                    tipo, contrato.Deuda.CodigoPrestamo);
                fallaron.Add(TiposDeContrato.Nombre(tipo));
            }
        }

        if (fallaron.Count == 0)
            return;

        MessageBox.Show(VentanaDuena.DeLaVista(this) ?? Application.Current.MainWindow,
            $"El préstamo se creó bien, pero no se pudo imprimir: {string.Join(", ", fallaron)}." +
            "\n\nSe puede reimprimir desde Préstamos → Contratos.",
            "Imprimir contratos", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    /// <summary>
    /// Redibuja el panel lateral. El FlowDocument se arma aquí y no en el
    /// ViewModel porque es un objeto de WPF: la capa de ViewModels no conoce
    /// Printing ni System.Windows.Documents.
    /// </summary>
    private void RedibujarLateral()
    {
        if (_vm?.VistaPreviaTipo is not { } tipo)
        {
            VisorContrato.Document = null;
            return;
        }

        var contrato = _vm.ContratoBorrador();
        if (contrato is null)
        {
            VisorContrato.Document = null;
            return;
        }

        VisorContrato.Document = ContratoDocumentFactory.Crear(tipo, contrato);
    }
}
