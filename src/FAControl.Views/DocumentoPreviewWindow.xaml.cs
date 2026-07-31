using System;
using System.Windows;
using System.Windows.Documents;
using Microsoft.Win32;
using FAControl.Printing;
using Serilog;

namespace FAControl.Views;

/// <summary>
/// Vista previa + impresión genérica de un FlowDocument (reporte de clientes,
/// etc.). El documento se pasa como una fábrica para poder recrearlo: uno para
/// el visor y otro nuevo para la impresora (un FlowDocument tiene un solo padre).
/// </summary>
public partial class DocumentoPreviewWindow : Window
{
    private readonly Func<FlowDocument> _fabrica;
    private readonly string _descripcion;
    private readonly Func<Task>? _archivar;

    /// <param name="archivar">
    /// Qué hacer con el documento después de imprimirlo. Lo usa la intimación de
    /// pago para quedar guardada sola en el expediente del cliente (026).
    /// </param>
    public DocumentoPreviewWindow(string titulo, string descripcion, Func<FlowDocument> fabrica,
        Func<Task>? archivar = null)
    {
        InitializeComponent();
        ChromeVentana.OcultarBotones(this);
        Title = titulo;
        _descripcion = descripcion;
        _fabrica = fabrica;
        _archivar = archivar;
        Visor.Document = fabrica();
    }

    private void BotonImprimir_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ImpresoraRecibos.ImprimirDocumento(_fabrica(), _descripcion);
            // Lo impreso queda archivado; si falla no se molesta al usuario,
            // que ya tiene su papel (ver ArchivarImpresoAsync).
            if (_archivar is not null)
                _ = _archivar();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error imprimiendo el documento {Desc}", _descripcion);
            MessageBox.Show(this, $"No se pudo imprimir.\n\n{ex.Message}",
                "Imprimir", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ---------- Zoom ----------
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
        var nombre = string.Concat(_descripcion.Split(System.IO.Path.GetInvalidFileNameChars()));
        var dialogo = new SaveFileDialog
        {
            Title = "Guardar como PDF",
            Filter = "PDF (*.pdf)|*.pdf",
            FileName = $"{nombre}.pdf"
        };
        if (dialogo.ShowDialog(this) != true)
            return;

        try
        {
            ImpresoraRecibos.GuardarDocumentoPdf(_fabrica(), dialogo.FileName, _descripcion);
            MessageBox.Show(this, $"Guardado en:\n{dialogo.FileName}",
                "Guardar PDF", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error guardando el documento {Desc} en PDF", _descripcion);
            MessageBox.Show(this, $"No se pudo guardar el PDF.\n\n{ex.Message}",
                "Guardar PDF", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BotonCerrar_Click(object sender, RoutedEventArgs e) => Close();
}
