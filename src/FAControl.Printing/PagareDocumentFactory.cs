using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using FAControl.Models;

namespace FAControl.Printing;

/// <summary>
/// Construye el pagaré como FlowDocument.
///
/// FlowDocument y NO un Visual fijo a propósito: PAGINA SOLO. El pagaré real
/// del cliente tiene 48 cuotas y no cabe en una hoja; con PrintVisual se
/// recortaba (era el BLOCKER anotado el 2026-07-17). El DocumentPaginator del
/// FlowDocument reparte la tabla en las páginas que hagan falta.
/// </summary>
public static class PagareDocumentFactory
{
    private static readonly CultureInfo CulturaRd = CultureInfo.GetCultureInfo("es-DO");
    private static readonly FontFamily Fuente = new("Segoe UI");
    private static readonly Brush Tinta = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
    private static readonly Brush TintaSuave = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x53));
    private static readonly Brush Linea = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));

    /// <summary>Documento carta listo para imprimir o previsualizar.</summary>
    public static FlowDocument Crear(PagareImpreso p)
    {
        var doc = new FlowDocument
        {
            FontFamily = Fuente,
            Foreground = Tinta,
            PagePadding = new Thickness(64),   // margen de hoja
            ColumnWidth = double.PositiveInfinity,  // una sola columna
            FontSize = 12,
            Background = Brushes.White
        };
        // Tamaño carta (8.5 x 11 pulgadas a 96 DPI)
        doc.PageWidth = 816;
        doc.PageHeight = 1056;

        // --- Encabezado del negocio ---
        doc.Blocks.Add(Parrafo(p.NombreNegocio, 16, FontWeights.Bold, espacioDespues: 2));
        var contacto = new[] { p.Prestamista, p.Ciudad, p.Telefono, p.Email }
            .Where(s => !string.IsNullOrWhiteSpace(s));
        var subtitulo = string.Join("  ·  ", contacto);
        if (!string.IsNullOrWhiteSpace(p.Rnc))
            subtitulo = string.IsNullOrEmpty(subtitulo) ? $"RNC {p.Rnc}" : $"{subtitulo}  ·  RNC {p.Rnc}";
        if (!string.IsNullOrWhiteSpace(subtitulo))
            doc.Blocks.Add(Parrafo(subtitulo, 10, FontWeights.Normal, TintaSuave, espacioDespues: 20));

        // --- Título ---
        var titulo = Parrafo("Pagaré", 20, FontWeights.Bold, espacioDespues: 16);
        titulo.TextAlignment = TextAlignment.Center;
        doc.Blocks.Add(titulo);

        // --- Declaración de deuda ---
        var acreedor = string.IsNullOrWhiteSpace(p.Prestamista) ? p.NombreNegocio : p.Prestamista;
        var declaracion = new Paragraph { Margin = new Thickness(0, 0, 0, 16), LineHeight = 20 };
        declaracion.Inlines.Add(new Run("Yo, "));
        declaracion.Inlines.Add(new Bold(new Run(p.DeudorNombre)));
        declaracion.Inlines.Add(new Run($", cédula #{p.DeudorCedula} debo pagar a "));
        declaracion.Inlines.Add(new Bold(new Run(acreedor)));
        declaracion.Inlines.Add(new Run(" la suma de "));
        declaracion.Inlines.Add(new Bold(new Run($"$R.D {p.MontoPrestado.ToString("N2", CulturaRd)}")));
        declaracion.Inlines.Add(new Run(" como se detalla a continuación:"));
        doc.Blocks.Add(declaracion);

        // --- Tabla de cuotas (se pagina sola) ---
        doc.Blocks.Add(TablaCuotas(p));

        // --- Total ---
        var total = Parrafo($"Total a pagar: $R.D {p.TotalAPagar.ToString("N2", CulturaRd)}",
            12, FontWeights.Bold, espacioAntes: 12, espacioDespues: 20);
        total.TextAlignment = TextAlignment.Right;
        doc.Blocks.Add(total);

        // --- Cláusula legal (textual, del PDF del cliente) ---
        doc.Blocks.Add(Parrafo(
            "En caso de incumplimiento con el presente préstamo quedan afectados todos mis " +
            "bienes habidos y por haber para el pago inmediato de esta deuda sin ninguna " +
            "formalidad judicial.", 11, FontWeights.Normal, espacioDespues: 4));
        doc.Blocks.Add(Parrafo(
            "Al firmar acepto compartir esta información crediticia en Púrpura Datos.",
            11, FontWeights.Normal, espacioDespues: 40));

        // --- Firmas ---
        doc.Blocks.Add(Firmas(p.DeudorNombre, acreedor));

        // --- Fecha ---
        var fecha = Parrafo(DateTime.Now.ToString(@"dd'.'MM'.'yyyy hh':'mm tt", CulturaRd),
            10, FontWeights.Normal, TintaSuave, espacioAntes: 30);
        fecha.TextAlignment = TextAlignment.Center;
        doc.Blocks.Add(fecha);

        return doc;
    }

    private static Table TablaCuotas(PagareImpreso p)
    {
        // Anchos FIJOS que suman menos que el ancho útil de la hoja (688px con
        // márgenes de 64): con columna estrella la tabla se estiraba y la última
        // columna se salía de la página.
        var tabla = new Table { CellSpacing = 0, Margin = new Thickness(0) };
        tabla.Columns.Add(new TableColumn { Width = new GridLength(50) });
        tabla.Columns.Add(new TableColumn { Width = new GridLength(150) });
        tabla.Columns.Add(new TableColumn { Width = new GridLength(150) });

        var grupo = new TableRowGroup();

        // Encabezado — se repite en cada página gracias a KeepWithNext del flujo
        var cab = new TableRow { Background = new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2)) };
        cab.Cells.Add(Celda("#", FontWeights.SemiBold));
        cab.Cells.Add(Celda("Fecha", FontWeights.SemiBold));
        cab.Cells.Add(Celda("Cuota", FontWeights.SemiBold, TextAlignment.Right));
        grupo.Rows.Add(cab);

        foreach (var c in p.Cuotas)
        {
            var fila = new TableRow();
            fila.Cells.Add(Celda(c.Numero.ToString(CulturaRd), FontWeights.Normal));
            fila.Cells.Add(Celda(c.FechaTexto, FontWeights.Normal));
            fila.Cells.Add(Celda(c.Cuota.ToString("N2", CulturaRd), FontWeights.Normal, TextAlignment.Right));
            grupo.Rows.Add(fila);
        }

        tabla.RowGroups.Add(grupo);
        return tabla;
    }

    private static TableCell Celda(string texto, FontWeight peso,
        TextAlignment alineacion = TextAlignment.Left)
    {
        var parrafo = new Paragraph(new Run(texto))
        {
            FontWeight = peso,
            FontSize = 11,
            Margin = new Thickness(0),
            TextAlignment = alineacion
        };
        return new TableCell(parrafo)
        {
            Padding = new Thickness(6, 4, 6, 4),
            BorderBrush = Linea,
            BorderThickness = new Thickness(0, 0, 0, 0.5)
        };
    }

    private static Table Firmas(string deudor, string acreedor)
    {
        // Anchos FIJOS: con columnas estrella las celdas colapsaban y el nombre
        // salía en vertical (una letra por línea).
        var tabla = new Table { CellSpacing = 0 };
        tabla.Columns.Add(new TableColumn { Width = new GridLength(300) });
        tabla.Columns.Add(new TableColumn { Width = new GridLength(88) });
        tabla.Columns.Add(new TableColumn { Width = new GridLength(300) });

        var grupo = new TableRowGroup();
        var fila = new TableRow();
        fila.Cells.Add(CeldaFirma(deudor));
        fila.Cells.Add(new TableCell());
        fila.Cells.Add(CeldaFirma(acreedor));
        grupo.Rows.Add(fila);
        tabla.RowGroups.Add(grupo);
        return tabla;
    }

    private static TableCell CeldaFirma(string nombre)
    {
        var raya = new Paragraph(new Run("____________________________"))
        {
            Margin = new Thickness(0),
            TextAlignment = TextAlignment.Center,
            Foreground = TintaSuave
        };
        var texto = new Paragraph(new Run(nombre))
        {
            Margin = new Thickness(0, 2, 0, 0),
            FontSize = 11,
            TextAlignment = TextAlignment.Center
        };
        var celda = new TableCell();
        celda.Blocks.Add(raya);
        celda.Blocks.Add(texto);
        return celda;
    }

    private static Paragraph Parrafo(string texto, double tamano, FontWeight peso,
        Brush? color = null, double espacioAntes = 0, double espacioDespues = 0) =>
        new(new Run(texto))
        {
            FontSize = tamano,
            FontWeight = peso,
            Foreground = color ?? Tinta,
            Margin = new Thickness(0, espacioAntes, 0, espacioDespues)
        };
}
