using System.Windows;
using System.Windows.Documents;
using Microsoft.Win32;
using FAControl.Printing;
using Serilog;

namespace FAControl.Views;

/// <summary>
/// Visor genérico de los documentos del dealer en FlowDocument (carta de
/// compromiso, recibo de separación — 2026-07-25). Recibe una FÁBRICA y no un
/// documento ya construido: un FlowDocument tiene un solo padre lógico, así que
/// el visor, la impresión y el PDF necesitan cada uno el suyo.
/// </summary>
public partial class DocumentoDealWindow : Window
{
    private readonly Func<FlowDocument> _fabrica;
    private readonly string _titulo;
    private readonly string _nombreArchivo;

    public DocumentoDealWindow(string titulo, string nombreArchivo, Func<FlowDocument> fabrica)
    {
        InitializeComponent();
        ChromeVentana.OcultarBotones(this);
        _fabrica = fabrica;
        _titulo = titulo;
        _nombreArchivo = nombreArchivo;
        Title = titulo;
        Visor.Document = fabrica();
    }

    private void BotonAcercar_Click(object sender, RoutedEventArgs e) => AjustarZoom(+20);
    private void BotonAlejar_Click(object sender, RoutedEventArgs e) => AjustarZoom(-20);

    private void AjustarZoom(double delta)
    {
        var nuevo = Math.Clamp(Visor.Zoom + delta, 50, 300);
        Visor.Zoom = nuevo;
        EtiquetaZoom.Text = $"{nuevo:0}%";
    }

    private void BotonGuardarPdf_Click(object sender, RoutedEventArgs e)
    {
        var dialogo = new SaveFileDialog
        {
            Title = $"Guardar {_titulo.ToLowerInvariant()} como PDF",
            Filter = "PDF (*.pdf)|*.pdf",
            FileName = $"{_nombreArchivo}.pdf"
        };
        if (dialogo.ShowDialog(this) != true)
            return;

        try
        {
            ImpresoraRecibos.GuardarDocumentoPdf(_fabrica(), dialogo.FileName, _titulo);
            MessageBox.Show(this, $"Documento guardado en:\n{dialogo.FileName}",
                "Guardar PDF", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error guardando {Titulo} en PDF", _titulo);
            MessageBox.Show(this, $"No se pudo guardar el PDF.\n\n{ex.Message}",
                "Guardar PDF", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BotonImprimir_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ImpresoraRecibos.ImprimirDocumento(_fabrica(), _titulo);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error imprimiendo {Titulo}", _titulo);
            MessageBox.Show(this, $"No se pudo imprimir el documento.\n\n{ex.Message}",
                "Imprimir", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BotonCerrar_Click(object sender, RoutedEventArgs e) => Close();
}
