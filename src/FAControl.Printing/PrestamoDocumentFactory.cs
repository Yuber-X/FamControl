using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using FAControl.Models;

namespace FAControl.Printing;

/// <summary>
/// Estado de préstamo como FlowDocument, para IMPRIMIR y para PDF.
///
/// FlowDocument y no un Visual fijo: PAGINA SOLO. Es el mismo motivo que
/// empujó al pagaré fuera de PrintVisual el 2026-07-17 (era el BLOCKER de
/// ese día): la tabla de amortización de un préstamo largo no cabe en una
/// hoja, y PrintVisual no parte páginas — recorta y se pierde la cola de la
/// tabla sin avisar. <see cref="PrestamoVisualFactory"/> sigue existiendo
/// para la VISTA PREVIA en pantalla, donde el scroll resuelve el alto.
///
/// Los anchos de columna son FIJOS y suman 640, menos que el ancho útil de
/// la hoja con cualquiera de los dos márgenes que se usan (816 − 2×64 = 688
/// al exportar, 816 − 2×48 = 720 al imprimir). Con columnas estrella la
/// última columna se sale de la página, tal como pasó en el pagaré.
/// </summary>
public static class PrestamoDocumentFactory
{
    private static readonly CultureInfo CulturaRd = CultureInfo.GetCultureInfo("es-DO");
    private static readonly FontFamily Fuente = new("Segoe UI");
    private static readonly Brush Tinta = new SolidColorBrush(Color.FromRgb(0x0D, 0x1B, 0x2A));
    private static readonly Brush TintaSuave = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80));
    private static readonly Brush Linea = new SolidColorBrush(Color.FromRgb(0xE6, 0xE7, 0xEB));
    private static readonly Brush FondoEncabezado = new SolidColorBrush(Color.FromRgb(0xF4, 0xF5, 0xF7));
    // Marca Familia Almonte (navy + dorado), igual que el pagaré
    private static readonly Brush BrushNavy = Congelar(new SolidColorBrush(Color.FromRgb(0x1B, 0x26, 0x3B)));
    private static readonly Brush BrushOro = Congelar(new SolidColorBrush(Color.FromRgb(0xC9, 0xA1, 0x5A)));

    private static Brush Congelar(SolidColorBrush b) { b.Freeze(); return b; }

    /// <summary>Documento carta listo para imprimir o exportar a PDF.</summary>
    public static FlowDocument Crear(PrestamoImpreso p)
    {
        var doc = new FlowDocument
        {
            FontFamily = Fuente,
            Foreground = Tinta,
            PagePadding = new Thickness(64),
            ColumnWidth = double.PositiveInfinity,   // una sola columna
            FontSize = 11,
            Background = Brushes.White,
            PageWidth = 816,                          // carta a 96 DPI
            PageHeight = 1056
        };

        // --- Marca del negocio ---
        if (!string.IsNullOrWhiteSpace(p.NegocioNombre))
        {
            var contacto = new[]
            {
                string.IsNullOrWhiteSpace(p.NegocioRnc) ? null : $"RNC {p.NegocioRnc}",
                string.IsNullOrWhiteSpace(p.NegocioTelefono) ? null : $"Tel. {p.NegocioTelefono}"
            }.Where(s => s is not null);
            doc.Blocks.Add(EncabezadoMarca(p.NegocioNombre, string.Join("  ·  ", contacto)));
            doc.Blocks.Add(ReglaDorada());
        }

        // --- Título ---
        doc.Blocks.Add(Parrafo("ESTADO DE PRÉSTAMO", 20, FontWeights.Bold));
        doc.Blocks.Add(Parrafo($"Préstamo {p.Codigo} · {p.EstadoTexto}", 11,
            FontWeights.Normal, TintaSuave, espacioDespues: 14));

        // --- Cliente y contrato, en dos columnas ---
        doc.Blocks.Add(DatosDelContrato(p));

        // --- Resumen económico ---
        doc.Blocks.Add(Resumen(p));

        // --- Tabla de amortización ---
        doc.Blocks.Add(Parrafo("Tabla de amortización", 13, FontWeights.SemiBold,
            espacioAntes: 18, espacioDespues: 8));
        doc.Blocks.Add(TablaCuotas(p.Cuotas));

        // --- Pie ---
        doc.Blocks.Add(Parrafo(
            $"Emitido por {p.EmitidoPor} el " +
            DateTime.Now.ToString(@"dd'/'MM'/'yyyy 'a las' hh':'mm tt", CulturaRd),
            9, FontWeights.Normal, TintaSuave, espacioAntes: 18));

        return doc;
    }

    private static BlockUIContainer EncabezadoMarca(string nombreNegocio, string subtitulo)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var badge = LogoFa.Badge(56);
        Grid.SetColumn(badge, 0);
        grid.Children.Add(badge);

        var textos = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 0, 0)
        };
        textos.Children.Add(new TextBlock
        {
            Text = nombreNegocio,
            FontFamily = Fuente,
            FontSize = 17,
            FontWeight = FontWeights.Bold,
            Foreground = BrushNavy
        });
        if (!string.IsNullOrWhiteSpace(subtitulo))
            textos.Children.Add(new TextBlock
            {
                Text = subtitulo,
                FontFamily = Fuente,
                FontSize = 10.5,
                Foreground = TintaSuave,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            });
        Grid.SetColumn(textos, 1);
        grid.Children.Add(textos);

        return new BlockUIContainer(grid) { Margin = new Thickness(0, 0, 0, 8) };
    }

    private static BlockUIContainer ReglaDorada() =>
        new(new Border { Height = 2.5, Background = BrushOro, Margin = new Thickness(0, 0, 0, 16) })
        {
            Margin = new Thickness(0)
        };

    /// <summary>Cliente y garantía a la izquierda; condiciones del contrato a la derecha.</summary>
    private static Table DatosDelContrato(PrestamoImpreso p)
    {
        var tabla = new Table { CellSpacing = 0, Margin = new Thickness(0, 0, 0, 14) };
        tabla.Columns.Add(new TableColumn { Width = new GridLength(340) });
        tabla.Columns.Add(new TableColumn { Width = new GridLength(300) });

        var izquierda = new TableCell { Padding = new Thickness(0, 0, 12, 0) };
        izquierda.Blocks.Add(Etiqueta("CLIENTE"));
        izquierda.Blocks.Add(Parrafo(p.ClienteNombre, 12.5, FontWeights.SemiBold));
        izquierda.Blocks.Add(Parrafo($"Cédula: {p.ClienteCedula}", 10.5, FontWeights.Normal, TintaSuave));
        izquierda.Blocks.Add(Etiqueta("GARANTÍA", espacioAntes: 10));
        izquierda.Blocks.Add(Parrafo(p.GarantiaTexto, 10.5, FontWeights.Normal));

        var derecha = new TableCell();
        foreach (var (etiqueta, valor) in new[]
        {
            ("Capital prestado", Moneda(p.MontoCapital)),
            ("Tasa", p.TasaTexto),
            ("Modalidad", p.ModalidadTexto),
            ("Método de cálculo", p.MetodoTexto),
            ("Primer pago", p.FechaPrimerPagoTexto)
        })
            derecha.Blocks.Add(FilaDato(etiqueta, valor));

        var fila = new TableRow();
        fila.Cells.Add(izquierda);
        fila.Cells.Add(derecha);
        var grupo = new TableRowGroup();
        grupo.Rows.Add(fila);
        tabla.RowGroups.Add(grupo);
        return tabla;
    }

    /// <summary>Las 4 métricas del resumen, una por columna.</summary>
    private static Table Resumen(PrestamoImpreso p)
    {
        var tabla = new Table { CellSpacing = 0, Margin = new Thickness(0, 6, 0, 6) };
        for (var i = 0; i < 4; i++)
            tabla.Columns.Add(new TableColumn { Width = new GridLength(160) });

        var fila = new TableRow();
        foreach (var (etiqueta, valor) in new[]
        {
            ("TOTAL A PAGAR", Moneda(p.TotalAPagar)),
            ("PAGADO", Moneda(p.TotalPagado)),
            ("SALDO PENDIENTE", Moneda(p.SaldoPendiente)),
            ("PROGRESO", p.ProgresoTexto)
        })
        {
            var celda = new TableCell
            {
                Padding = new Thickness(0, 10, 8, 10),
                BorderBrush = Linea,
                BorderThickness = new Thickness(0, 1, 0, 1)
            };
            celda.Blocks.Add(Etiqueta(etiqueta));
            celda.Blocks.Add(Parrafo(valor, 12.5, FontWeights.SemiBold));
            fila.Cells.Add(celda);
        }

        var grupo = new TableRowGroup();
        grupo.Rows.Add(fila);
        tabla.RowGroups.Add(grupo);
        return tabla;
    }

    private static Table TablaCuotas(IReadOnlyList<CuotaImpresa> cuotas)
    {
        // Mismos anchos que la vista previa (suman 640 < ancho útil de la hoja)
        var tabla = new Table { CellSpacing = 0, Margin = new Thickness(0) };
        foreach (var ancho in new double[] { 40, 92, 108, 100, 108, 116, 76 })
            tabla.Columns.Add(new TableColumn { Width = new GridLength(ancho) });

        var grupo = new TableRowGroup();

        var cab = new TableRow { Background = FondoEncabezado };
        string[] encabezados = ["N°", "Vencimiento", "Capital", "Interés", "Cuota", "Saldo restante", "Estado"];
        for (var c = 0; c < encabezados.Length; c++)
            cab.Cells.Add(Celda(encabezados[c], 9, FontWeights.SemiBold,
                EsColumnaDeMonto(c) ? TextAlignment.Right : TextAlignment.Left, TintaSuave));
        grupo.Rows.Add(cab);

        foreach (var q in cuotas)
        {
            var fila = new TableRow();
            string[] valores =
            [
                q.Numero.ToString(CulturaRd), q.FechaTexto, Moneda(q.Capital), Moneda(q.Interes),
                Moneda(q.MontoTotal), Moneda(q.SaldoDespues), q.EstadoTexto
            ];
            for (var c = 0; c < valores.Length; c++)
                fila.Cells.Add(Celda(valores[c], 9.5,
                    c == 4 ? FontWeights.SemiBold : FontWeights.Normal,
                    EsColumnaDeMonto(c) ? TextAlignment.Right : TextAlignment.Left));
            grupo.Rows.Add(fila);
        }

        tabla.RowGroups.Add(grupo);
        return tabla;
    }

    /// <summary>Capital, interés, cuota y saldo van alineados a la derecha.</summary>
    private static bool EsColumnaDeMonto(int columna) => columna is >= 2 and <= 5;

    private static TableCell Celda(string texto, double tamano, FontWeight peso,
        TextAlignment alineacion, Brush? color = null)
    {
        var parrafo = new Paragraph(new Run(texto))
        {
            FontSize = tamano,
            FontWeight = peso,
            Foreground = color ?? Tinta,
            TextAlignment = alineacion,
            Margin = new Thickness(0)
        };
        return new TableCell(parrafo)
        {
            Padding = new Thickness(6, 5, 6, 5),
            BorderBrush = Linea,
            BorderThickness = new Thickness(0, 0, 0, 0.5)
        };
    }

    private static Paragraph FilaDato(string etiqueta, string valor)
    {
        // Una sola línea con la etiqueta suave y el valor en negrita: dentro de
        // una celda de tabla no hay dos columnas donde alinear a los extremos.
        var parrafo = new Paragraph { Margin = new Thickness(0, 0, 0, 4), FontSize = 10.5 };
        parrafo.Inlines.Add(new Run($"{etiqueta}: ") { Foreground = TintaSuave });
        parrafo.Inlines.Add(new Run(valor) { FontWeight = FontWeights.SemiBold });
        return parrafo;
    }

    private static Paragraph Etiqueta(string texto, double espacioAntes = 0) =>
        Parrafo(texto, 8.5, FontWeights.SemiBold, TintaSuave, espacioAntes: espacioAntes);

    private static Paragraph Parrafo(string texto, double tamano, FontWeight peso,
        Brush? color = null, double espacioAntes = 0, double espacioDespues = 0) =>
        new(new Run(texto))
        {
            FontSize = tamano,
            FontWeight = peso,
            Foreground = color ?? Tinta,
            Margin = new Thickness(0, espacioAntes, 0, espacioDespues)
        };

    private static string Moneda(decimal valor) => $"RD$ {valor.ToString("N2", CulturaRd)}";
}
