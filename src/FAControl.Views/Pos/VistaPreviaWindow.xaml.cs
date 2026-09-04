using System.Windows;
using FAControl.Printing.Pos;

namespace FAControl.Views.Pos;

/// <summary>
/// Vista previa genérica de un documento imprimible (cierre de caja).
/// A diferencia del ticket de venta, el cierre SIEMPRE se previsualiza
/// antes de imprimir (pedido Yuber 2026-07-12). Code-behind de solo UI.
/// </summary>
public partial class VistaPreviaWindow : Window
{
    private readonly FrameworkElement _visual;
    private readonly string _descripcion;
    private readonly bool _paginar;

    /// <param name="paginar">
    /// Repartir en varias hojas lo que no entre en una. Lo pide el cierre en
    /// Carta: con varios cajeros el visual pasa el alto de la hoja y
    /// PrintVisual recortaba la cola sin avisar.
    /// </param>
    public VistaPreviaWindow(FrameworkElement visual, string titulo, string descripcion,
        bool paginar = false)
    {
        InitializeComponent();
        VentanaAjustable.Ajustar(this);
        Title = titulo;
        _visual = visual;
        _descripcion = descripcion;
        _paginar = paginar;
        Contenedor.Content = visual;
    }

    private void BotonImprimir_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Diálogo del sistema: aquí el usuario elige impresora y papel
            if (ImpresoraTickets.Imprimir(_visual, _descripcion, paginar: _paginar))
                Close();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Error imprimiendo {Descripcion}", _descripcion);
            MessageBox.Show(this,
                "No se pudo imprimir.\n\n" + ex.Message,
                "Impresión", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BotonCerrar_Click(object sender, RoutedEventArgs e) => Close();
}
