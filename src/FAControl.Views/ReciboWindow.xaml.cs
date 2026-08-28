using System.Windows;
using Microsoft.Win32;
using FAControl.Models;
using FAControl.Printing;
using Serilog;

namespace FAControl.Views;

/// <summary>
/// Vista previa del recibo con impresión y exportación a PDF.
/// El visual mostrado es EXACTAMENTE el que se imprime (patrón POS-400).
/// </summary>
public partial class ReciboWindow : Window
{
    private readonly ReciboPago _recibo;
    private readonly FrameworkElement _visual;

    public ReciboWindow(ReciboPago recibo)
    {
        InitializeComponent();
        ChromeVentana.OcultarBotones(this);
        _recibo = recibo;
        _visual = ReciboVisualFactory.Crear(recibo);
        ContenedorRecibo.Content = _visual;
    }

    private void BotonImprimir_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Visual independiente para la impresora (el de pantalla ya tiene padre visual)
            var visualImpresion = ReciboVisualFactory.Crear(_recibo);
            ImpresoraRecibos.Imprimir(visualImpresion, $"Recibo {_recibo.NumeroReciboPrincipal}");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error imprimiendo el recibo {Recibo}", _recibo.NumeroReciboPrincipal);
            MessageBox.Show(this, $"No se pudo imprimir el recibo.\n\n{ex.Message}",
                "Imprimir recibo", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BotonPdf_Click(object sender, RoutedEventArgs e)
    {
        var dialogo = new SaveFileDialog
        {
            Title = "Guardar recibo como PDF",
            FileName = $"Recibo_{_recibo.NumeroReciboPrincipal}.pdf",
            Filter = "Documento PDF (*.pdf)|*.pdf"
        };
        if (dialogo.ShowDialog(this) != true)
            return;

        try
        {
            var visualPdf = ReciboVisualFactory.Crear(_recibo);
            ImpresoraRecibos.GuardarPdf(visualPdf, dialogo.FileName,
                $"Recibo {_recibo.NumeroReciboPrincipal} — FAControl");
            MessageBox.Show(this, $"Recibo guardado en:\n{dialogo.FileName}",
                "Guardar PDF", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error exportando el recibo {Recibo} a PDF", _recibo.NumeroReciboPrincipal);
            MessageBox.Show(this, $"No se pudo guardar el PDF.\n\n{ex.Message}",
                "Guardar PDF", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BotonCerrar_Click(object sender, RoutedEventArgs e) => Close();
}
