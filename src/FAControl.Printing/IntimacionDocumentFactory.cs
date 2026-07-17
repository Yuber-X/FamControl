using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using FAControl.Models;

namespace FAControl.Printing;

/// <summary>
/// Intimación de pago como FlowDocument (pagina solo). Documento formal que
/// emite el ACREEDOR requiriendo el pago, previo a lo judicial.
/// NO es el mandamiento (ese es acto de alguacil). Ver el doc del proyecto.
/// </summary>
public static class IntimacionDocumentFactory
{
    private static readonly CultureInfo CulturaRd = CultureInfo.GetCultureInfo("es-DO");
    private static readonly FontFamily Fuente = new("Segoe UI");
    private static readonly Brush Tinta = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
    private static readonly Brush TintaSuave = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x53));
    private static readonly Brush Linea = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));
    private static readonly Brush FondoCab = new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2));

    public static FlowDocument Crear(IntimacionImpresa m)
    {
        var doc = new FlowDocument
        {
            FontFamily = Fuente, Foreground = Tinta,
            PagePadding = new Thickness(64), ColumnWidth = double.PositiveInfinity,
            FontSize = 12, Background = Brushes.White, PageWidth = 816, PageHeight = 1056
        };

        // Encabezado del acreedor
        doc.Blocks.Add(Parrafo(m.NombreNegocio, 15, FontWeights.Bold, espacioDespues: 1));
        var contacto = new[] { m.Prestamista, m.Ciudad, m.Telefono, m.Rnc is "" ? "" : $"RNC {m.Rnc}" }
            .Where(s => !string.IsNullOrWhiteSpace(s));
        var sub = string.Join("  ·  ", contacto);
        if (!string.IsNullOrWhiteSpace(sub))
            doc.Blocks.Add(Parrafo(sub, 10, FontWeights.Normal, TintaSuave, espacioDespues: 8));

        var fecha = Parrafo($"{m.Ciudad}, {DateTime.Now.ToString(@"dd 'de' MMMM 'de' yyyy", CulturaRd)}",
            10, FontWeights.Normal, TintaSuave, espacioDespues: 18);
        fecha.TextAlignment = TextAlignment.Right;
        doc.Blocks.Add(fecha);

        var titulo = Parrafo("INTIMACIÓN DE PAGO", 16, FontWeights.Bold, espacioDespues: 16);
        titulo.TextAlignment = TextAlignment.Center;
        doc.Blocks.Add(titulo);

        // Destinatario
        var destinatario = new Paragraph { Margin = new Thickness(0, 0, 0, 14), LineHeight = 20 };
        destinatario.Inlines.Add(new Run("A: "));
        destinatario.Inlines.Add(new Bold(new Run(m.DeudorNombre)));
        destinatario.Inlines.Add(new Run($", portador(a) de la cédula #{m.DeudorCedula}."));
        doc.Blocks.Add(destinatario);

        // Cuerpo
        var cuerpo = new Paragraph { Margin = new Thickness(0, 0, 0, 12), LineHeight = 20 };
        cuerpo.Inlines.Add(new Run(
            $"Por medio de la presente le INTIMAMOS formalmente al pago de las sumas vencidas y no " +
            $"pagadas correspondientes al préstamo {m.CodigoPrestamo}, cuyo saldo pendiente asciende a "));
        cuerpo.Inlines.Add(new Bold(new Run($"RD$ {m.SaldoPendiente.ToString("N2", CulturaRd)}")));
        cuerpo.Inlines.Add(new Run(
            $" (monto original prestado: RD$ {m.MontoOriginal.ToString("N2", CulturaRd)})."));
        doc.Blocks.Add(cuerpo);

        // Tabla de cuotas vencidas
        if (m.CuotasVencidas.Count > 0)
        {
            doc.Blocks.Add(Parrafo("Cuotas vencidas:", 11, FontWeights.SemiBold, espacioDespues: 6));
            doc.Blocks.Add(Tabla(m.CuotasVencidas));
            doc.Blocks.Add(Parrafo(
                $"Total vencido: RD$ {m.CuotasVencidas.Sum(c => c.MontoPendiente).ToString("N2", CulturaRd)}",
                11, FontWeights.Bold, espacioAntes: 8, espacioDespues: 14));
        }

        // Requerimiento
        var req = new Paragraph { Margin = new Thickness(0, 0, 0, 12), LineHeight = 20 };
        req.Inlines.Add(new Run(
            $"Le concedemos un plazo de {m.PlazoDias} día(s) a partir del recibo de esta comunicación " +
            "para saldar la suma adeudada. De no cumplir, procederemos a ejercer las acciones legales " +
            "que correspondan conforme al pagaré firmado, en el cual usted aceptó que quedan afectados " +
            "todos sus bienes habidos y por haber para el pago inmediato de esta deuda."));
        doc.Blocks.Add(req);

        doc.Blocks.Add(Parrafo(
            "La presente intimación se emite sin renuncia de ningún derecho y como requerimiento previo " +
            "a la vía judicial.", 11, FontWeights.Normal, TintaSuave, espacioDespues: 40));

        // Firma
        var raya = Parrafo("____________________________", 11, FontWeights.Normal, TintaSuave);
        raya.TextAlignment = TextAlignment.Center;
        doc.Blocks.Add(raya);
        var firmante = string.IsNullOrWhiteSpace(m.Prestamista) ? m.NombreNegocio : m.Prestamista;
        var nombreFirma = Parrafo(firmante, 11, FontWeights.Normal, espacioAntes: 2);
        nombreFirma.TextAlignment = TextAlignment.Center;
        doc.Blocks.Add(nombreFirma);

        return doc;
    }

    private static Table Tabla(IReadOnlyList<IntimacionCuota> cuotas)
    {
        var tabla = new Table { CellSpacing = 0 };
        foreach (var a in new double[] { 60, 200, 160 })
            tabla.Columns.Add(new TableColumn { Width = new GridLength(a) });

        var grupo = new TableRowGroup();
        var cab = new TableRow { Background = FondoCab };
        cab.Cells.Add(Celda("N°", FontWeights.SemiBold));
        cab.Cells.Add(Celda("Vencimiento", FontWeights.SemiBold));
        cab.Cells.Add(Celda("Monto pendiente", FontWeights.SemiBold, TextAlignment.Right));
        grupo.Rows.Add(cab);

        foreach (var c in cuotas)
        {
            var fila = new TableRow();
            fila.Cells.Add(Celda(c.Numero.ToString(CulturaRd), FontWeights.Normal));
            fila.Cells.Add(Celda(c.FechaVencimiento, FontWeights.Normal));
            fila.Cells.Add(Celda(c.MontoPendiente.ToString("N2", CulturaRd), FontWeights.Normal, TextAlignment.Right));
            grupo.Rows.Add(fila);
        }
        tabla.RowGroups.Add(grupo);
        return tabla;
    }

    private static TableCell Celda(string texto, FontWeight peso, TextAlignment alineacion = TextAlignment.Left) =>
        new(new Paragraph(new Run(texto)) { FontWeight = peso, FontSize = 10.5, Margin = new Thickness(0), TextAlignment = alineacion })
        {
            Padding = new Thickness(6, 5, 6, 5), BorderBrush = Linea, BorderThickness = new Thickness(0, 0, 0, 0.5)
        };

    private static Paragraph Parrafo(string texto, double tamano, FontWeight peso,
        Brush? color = null, double espacioAntes = 0, double espacioDespues = 0) =>
        new(new Run(texto))
        {
            FontSize = tamano, FontWeight = peso, Foreground = color ?? Tinta,
            Margin = new Thickness(0, espacioAntes, 0, espacioDespues)
        };
}
