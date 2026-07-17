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
