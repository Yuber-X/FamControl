using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;

namespace FAControl.Printing;

/// <summary>
/// Parte un visual alto en varias páginas para poder imprimirlo entero.
///
/// EL PROBLEMA. <c>PrintVisual</c> manda el visual tal cual: lo que no entra en
/// la hoja se RECORTA, sin error ni aviso. Con el pagaré y con la hoja del
/// préstamo se resolvió pasándolos a FlowDocument, que pagina solo. Pero el
/// cierre de caja se arma como un StackPanel de filas (lo comparte con el
/// ticket de 80mm, donde el rollo es continuo y paginar sería un error), así
/// que reescribirlo entero para el caso de Carta era desproporcionado.
///
/// CÓMO. Cada página muestra una franja del mismo visual a través de un
/// VisualBrush con Viewbox absoluto. El VisualBrush no reparenta nada — cosa
/// importante, porque el visual del cierre ya está colgado de la ventana de
/// vista previa y un elemento de WPF tiene un solo padre.
///
/// El ancho se escala para que entre en la hoja; el alto se corta en franjas.
/// Nunca se agranda: un cierre angosto impreso en Carta se ve como es, no
/// estirado.
/// </summary>
public static class VisualPaginado
{
    /// <summary>
    /// El visual repartido en páginas de <paramref name="anchoPagina"/> ×
    /// <paramref name="altoPagina"/> (en unidades de WPF, 96 por pulgada).
    /// </summary>
    public static FixedDocument Paginar(FrameworkElement visual,
        double anchoPagina, double altoPagina, double margen = 40)
    {
        // El visual puede venir sin medir (recién creado) o ya medido por la
        // ventana de vista previa. Medir de nuevo es barato y deja el tamaño
        // real, que es de donde sale la cantidad de páginas.
        visual.Measure(new Size(visual.Width > 0 ? visual.Width : double.PositiveInfinity,
            double.PositiveInfinity));
        var anchoVisual = visual.Width > 0 ? visual.Width : visual.DesiredSize.Width;
        var altoVisual = Math.Max(visual.DesiredSize.Height, visual.ActualHeight);
        visual.Arrange(new Rect(0, 0, anchoVisual, altoVisual));
        visual.UpdateLayout();

        var util = new Size(Math.Max(1, anchoPagina - margen * 2),
                            Math.Max(1, altoPagina - margen * 2));

        // Solo se ACHICA. Agrandar un cierre angosto lo dejaría pixelado y con
        // una tipografía enorme que nadie pidió.
        var escala = anchoVisual > util.Width ? util.Width / anchoVisual : 1d;

        // Alto de la franja MEDIDO EN COORDENADAS DEL VISUAL: es lo que hay que
        // recortar del original para que, ya escalado, llene el alto útil.
        var altoFranja = util.Height / escala;

        var documento = new FixedDocument();
        documento.DocumentPaginator.PageSize = new Size(anchoPagina, altoPagina);

        var paginas = Math.Max(1, (int)Math.Ceiling(altoVisual / altoFranja));
        for (var i = 0; i < paginas; i++)
        {
            var desde = i * altoFranja;
            var alto = Math.Min(altoFranja, altoVisual - desde);
            if (alto <= 0)
                break;

            documento.Pages.Add(Pagina(visual, anchoVisual, desde, alto, escala,
                anchoPagina, altoPagina, margen));
        }
        return documento;
    }

    private static PageContent Pagina(FrameworkElement visual, double anchoVisual,
        double desde, double alto, double escala,
        double anchoPagina, double altoPagina, double margen)
    {
        var brocha = new VisualBrush(visual)
        {
            // Absoluto: el Viewbox se expresa en píxeles del visual, no en
            // fracciones. Es lo que permite pedir "de este alto a este otro".
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, desde, anchoVisual, alto),
            // Fill mapea esa franja al rectángulo completo. Como el rectángulo
            // conserva la proporción (mismo factor en ancho y alto), no deforma.
            Stretch = Stretch.Fill
        };

        var recorte = new Rectangle
        {
            Width = anchoVisual * escala,
            Height = alto * escala,
            Fill = brocha
        };

        var lienzo = new Canvas { Width = anchoPagina, Height = altoPagina };
        Canvas.SetLeft(recorte, margen);
        Canvas.SetTop(recorte, margen);
        lienzo.Children.Add(recorte);

        var hoja = new FixedPage { Width = anchoPagina, Height = altoPagina };
        hoja.Children.Add(lienzo);
        hoja.Measure(new Size(anchoPagina, altoPagina));
        hoja.Arrange(new Rect(0, 0, anchoPagina, altoPagina));
        hoja.UpdateLayout();

        var contenido = new PageContent();
        ((IAddChild)contenido).AddChild(hoja);
        return contenido;
    }
}
