using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace FAControl.Printing;

/// <summary>
/// Impresión y exportación de recibos. El mismo visual de 80mm se manda a la
/// impresora (PrintVisual) o se rasteriza a 192 DPI dentro de un PDF de 80mm
/// de ancho (PdfSharp) — papel y archivo siempre idénticos.
/// </summary>
public static class ImpresoraRecibos
{
    /// <summary>Abre el diálogo de impresión del sistema. True si se envió a imprimir.</summary>
    public static bool Imprimir(FrameworkElement visual, string descripcion)
    {
        var dialogo = new PrintDialog();
        if (dialogo.ShowDialog() != true)
            return false;

        dialogo.PrintVisual(visual, descripcion);
        return true;
    }

    /// <summary>
    /// Imprime un FlowDocument, que PAGINA SOLO: sirve para documentos altos
    /// como el pagaré de 48 cuotas, que con PrintVisual se recortaba.
    /// El documento se ajusta al área imprimible real de la impresora elegida.
    /// </summary>
    public static bool ImprimirDocumento(System.Windows.Documents.FlowDocument documento, string descripcion)
    {
        var dialogo = new PrintDialog();
        if (dialogo.ShowDialog() != true)
            return false;

        documento.PageWidth = dialogo.PrintableAreaWidth;
        documento.PageHeight = dialogo.PrintableAreaHeight;
        documento.PagePadding = new Thickness(48);
        documento.ColumnWidth = double.PositiveInfinity;

        var paginador = ((System.Windows.Documents.IDocumentPaginatorSource)documento).DocumentPaginator;
        dialogo.PrintDocument(paginador, descripcion);
        return true;
    }

    /// <summary>
    /// Guarda un FlowDocument (p. ej. el pagaré) como PDF MULTIPÁGINA en hoja
    /// carta. Rasteriza cada página a 192 DPI sobre fondo blanco y la coloca en
    /// su hoja PDF — fiel a lo que se imprime, con el logo vectorial incluido.
    /// </summary>
    public static void GuardarDocumentoPdf(System.Windows.Documents.FlowDocument documento,
        string rutaDestino, string titulo)
    {
        const double escala = 2.0;                 // 96 → 192 DPI
        documento.PageWidth = 816;                 // carta a 96 DPI
        documento.PageHeight = 1056;
        documento.PagePadding = new Thickness(64);
        documento.ColumnWidth = double.PositiveInfinity;

        var paginador = ((System.Windows.Documents.IDocumentPaginatorSource)documento).DocumentPaginator;
        paginador.PageSize = new Size(816, 1056);
        paginador.ComputePageCount();

        using var pdf = new PdfDocument();
        pdf.Info.Title = titulo;

        var temporales = new List<string>();
        try
        {
            for (var i = 0; i < paginador.PageCount; i++)
            {
                using var pagina = paginador.GetPage(i);
                var w = pagina.Size.Width;
                var h = pagina.Size.Height;

                var bitmap = new RenderTargetBitmap(
                    (int)Math.Ceiling(w * escala), (int)Math.Ceiling(h * escala),
                    96 * escala, 96 * escala, PixelFormats.Pbgra32);

                // Fondo blanco + contenido de la página (compone en dos pasadas)
                var fondo = new DrawingVisual();
                using (var dc = fondo.RenderOpen())
                    dc.DrawRectangle(Brushes.White, null, new Rect(pagina.Size));
                bitmap.Render(fondo);
                bitmap.Render(pagina.Visual);

                var rutaPng = Path.Combine(Path.GetTempPath(), $"facontrol-pagare-{Guid.NewGuid():N}.png");
                temporales.Add(rutaPng);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var archivo = File.Create(rutaPng))
                    encoder.Save(archivo);

                var pagPdf = pdf.AddPage();
                pagPdf.Width = XUnit.FromPoint(w * 72.0 / 96.0);   // DIU (96) → puntos (72)
                pagPdf.Height = XUnit.FromPoint(h * 72.0 / 96.0);
                using var grafico = XGraphics.FromPdfPage(pagPdf);
                using var imagen = XImage.FromFile(rutaPng);
                grafico.DrawImage(imagen, 0, 0, pagPdf.Width.Point, pagPdf.Height.Point);
            }
            pdf.Save(rutaDestino);
        }
        finally
        {
            foreach (var t in temporales)
                if (File.Exists(t))
                    File.Delete(t);
        }
    }

    /// <summary>Guarda el recibo como PDF de 80mm de ancho y alto proporcional.</summary>
    public static void GuardarPdf(FrameworkElement visual, string rutaDestino)
    {
        const double escala = 2.0; // 192 DPI: nítido sin archivos gigantes
        var ancho = (int)Math.Ceiling(visual.ActualWidth * escala);
        var alto = (int)Math.Ceiling(visual.ActualHeight * escala);

        var bitmap = new RenderTargetBitmap(ancho, alto, 96 * escala, 96 * escala, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        var rutaPng = Path.Combine(Path.GetTempPath(), $"facontrol-recibo-{Guid.NewGuid():N}.png");
        try
        {
            using (var archivoPng = File.Create(rutaPng))
                encoder.Save(archivoPng);

            using var documento = new PdfDocument();
            documento.Info.Title = "Recibo de pago — FAControl";

            var pagina = documento.AddPage();
            pagina.Width = XUnit.FromMillimeter(80);
            pagina.Height = XUnit.FromMillimeter(80 * visual.ActualHeight / visual.ActualWidth);

            using (var grafico = XGraphics.FromPdfPage(pagina))
            using (var imagen = XImage.FromFile(rutaPng))
                grafico.DrawImage(imagen, 0, 0, pagina.Width.Point, pagina.Height.Point);

            documento.Save(rutaDestino);
        }
        finally
        {
            if (File.Exists(rutaPng))
                File.Delete(rutaPng);
        }
    }
}
