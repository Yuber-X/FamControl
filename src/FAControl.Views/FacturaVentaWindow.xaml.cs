using System.Windows;
using Microsoft.Win32;
using FAControl.Models;
using FAControl.Printing;
using Serilog;

namespace FAControl.Views;

/// <summary>
/// Vista previa, impresión y PDF de la factura de una venta al contado
/// (pedido 2026-07-25). El visual mostrado es EXACTAMENTE el que se imprime.
/// </summary>
public partial class FacturaVentaWindow : Window
{
    private readonly FacturaVentaImpresa _factura;

    public FacturaVentaWindow(FacturaVentaImpresa factura)
    {
        InitializeComponent();
        ChromeVentana.OcultarBotones(this);
        _factura = factura;
        ContenedorFactura.Content = FacturaVentaVisualFactory.Crear(factura);
    }

    private void BotonImprimir_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var visualImpresion = FacturaVentaVisualFactory.Crear(_factura);
            ImpresoraRecibos.Imprimir(visualImpresion, $"Factura {_factura.Codigo}");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error imprimiendo la factura {Codigo}", _factura.Codigo);
            MessageBox.Show(this, $"No se pudo imprimir la factura.\n\n{ex.Message}",
                "Imprimir factura", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BotonPdf_Click(object sender, RoutedEventArgs e)
    {
        var dialogo = new SaveFileDialog
        {
            Title = "Guardar factura como PDF",
            FileName = $"Factura_{_factura.Codigo}.pdf",
            Filter = "Documento PDF (*.pdf)|*.pdf"
        };
        if (dialogo.ShowDialog(this) != true)
            return;

        try
        {
            var visualPdf = FacturaVentaVisualFactory.Crear(_factura);
            ImpresoraRecibos.GuardarPdf(visualPdf, dialogo.FileName);
            MessageBox.Show(this, $"Factura guardada en:\n{dialogo.FileName}",
                "Guardar PDF", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error exportando la factura {Codigo} a PDF", _factura.Codigo);
            MessageBox.Show(this, $"No se pudo guardar el PDF.\n\n{ex.Message}",
                "Guardar PDF", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BotonCerrar_Click(object sender, RoutedEventArgs e) => Close();
}
