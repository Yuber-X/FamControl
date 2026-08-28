using System.Windows;
using FAControl.Models;
using FAControl.Printing;
using Serilog;

namespace FAControl.Views;

/// <summary>
/// Vista previa e impresión del estado de un préstamo en hoja carta
/// (pedido del cliente 2026-07-16: "Prestamo > Imprimir").
/// El visual mostrado es EXACTAMENTE el que se imprime.
/// </summary>
public partial class PrestamoImpresionWindow : Window
{
    private readonly PrestamoImpreso _prestamo;

    public PrestamoImpresionWindow(PrestamoImpreso prestamo)
    {
        InitializeComponent();
        ChromeVentana.OcultarBotones(this);
        _prestamo = prestamo;
        ContenedorPrestamo.Content = PrestamoVisualFactory.Crear(prestamo);
    }

    private void BotonImprimir_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // FlowDocument y no PrintVisual: la tabla de amortización de un
            // préstamo largo no cabe en una hoja y PrintVisual no pagina, así
            // que recortaba la cola de la tabla sin avisar (mismo defecto que
            // fue el BLOCKER del pagaré el 2026-07-17). En pantalla se sigue
            // mostrando el visual, que ahí sí resuelve el alto con scroll.
            ImpresoraRecibos.ImprimirDocumento(
                PrestamoDocumentFactory.Crear(_prestamo), $"Préstamo {_prestamo.Codigo}");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error imprimiendo el préstamo {Codigo}", _prestamo.Codigo);
            MessageBox.Show(this, $"No se pudo imprimir el préstamo.\n\n{ex.Message}",
                "Imprimir préstamo", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BotonCerrar_Click(object sender, RoutedEventArgs e) => Close();
}
