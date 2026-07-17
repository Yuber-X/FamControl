using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using FAControl.Models;

namespace FAControl.Printing;

/// <summary>
/// Reporte de clientes como FlowDocument (pagina solo si son muchos).
/// Sirve para el reporte INDIVIDUAL (una fila, el cliente filtrado) y el
/// GLOBAL (todos los clientes), pedido del cliente 2026-07-19.
/// </summary>
public static class ReporteClientesDocumentFactory
{
    private static readonly CultureInfo CulturaRd = CultureInfo.GetCultureInfo("es-DO");
    private static readonly FontFamily Fuente = new("Segoe UI");
    private static readonly Brush Tinta = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
    private static readonly Brush TintaSuave = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x53));
    private static readonly Brush Linea = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));
    private static readonly Brush FondoCab = new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2));

    public static FlowDocument Crear(ReporteClientesImpreso r)
    {
        var doc = new FlowDocument
        {
            FontFamily = Fuente,
            Foreground = Tinta,
            PagePadding = new Thickness(56),
            ColumnWidth = double.PositiveInfinity,
            FontSize = 11,
            Background = Brushes.White,
            PageWidth = 816,
            PageHeight = 1056
        };

        doc.Blocks.Add(Parrafo(r.Titulo, 18, FontWeights.Bold, espacioDespues: 2));
        doc.Blocks.Add(Parrafo(r.Periodo, 11, FontWeights.Normal, TintaSuave, espacioDespues: 16));

        doc.Blocks.Add(Tabla(r.Filas));

        // Totales
        doc.Blocks.Add(Parrafo(
            $"{r.Filas.Count} cliente(s)  ·  " +
            $"Total cobrado: $R.D {r.Filas.Sum(f => f.TotalCobrado).ToString("N2", CulturaRd)}  ·  " +
            $"Interés: $R.D {r.Filas.Sum(f => f.Interes).ToString("N2", CulturaRd)}",
            11, FontWeights.Bold, espacioAntes: 10, espacioDespues: 24));

        doc.Blocks.Add(Parrafo(
            $"Emitido por {r.EmitidoPor} el {DateTime.Now.ToString(@"dd'/'MM'/'yyyy hh':'mm tt", CulturaRd)}",
            9, FontWeights.Normal, TintaSuave));

        return doc;
    }

    private static Table Tabla(IReadOnlyList<ReporteCliente> filas)
    {
        var tabla = new Table { CellSpacing = 0 };
        // Anchos fijos que suman < ancho util (704): con estrella se salia
        double[] anchos = [204, 110, 110, 70, 110];
        foreach (var a in anchos)
            tabla.Columns.Add(new TableColumn { Width = new GridLength(a) });

        var grupo = new TableRowGroup();
        var cab = new TableRow { Background = FondoCab };
        foreach (var (txt, der) in new[] { ("Cliente", false), ("Cobrado", true),
                     ("Interés", true), ("Cuotas", true), ("Saldo pend.", true) })
            cab.Cells.Add(Celda(txt, FontWeights.SemiBold, der ? TextAlignment.Right : TextAlignment.Left));
        grupo.Rows.Add(cab);

        foreach (var f in filas)
        {
            var fila = new TableRow();
            fila.Cells.Add(Celda(f.Nombre, FontWeights.Normal));
            fila.Cells.Add(Celda(f.TotalCobrado.ToString("N2", CulturaRd), FontWeights.Normal, TextAlignment.Right));
            fila.Cells.Add(Celda(f.Interes.ToString("N2", CulturaRd), FontWeights.Normal, TextAlignment.Right));
            fila.Cells.Add(Celda(f.CuotasCobradas.ToString(CulturaRd), FontWeights.Normal, TextAlignment.Right));
            fila.Cells.Add(Celda(f.SaldoPendiente.ToString("N2", CulturaRd), FontWeights.Normal, TextAlignment.Right));
            grupo.Rows.Add(fila);
        }
        tabla.RowGroups.Add(grupo);
        return tabla;
    }

    private static TableCell Celda(string texto, FontWeight peso, TextAlignment alineacion = TextAlignment.Left)
    {
        var parrafo = new Paragraph(new Run(texto))
        {
            FontWeight = peso, FontSize = 10.5, Margin = new Thickness(0), TextAlignment = alineacion
        };
        return new TableCell(parrafo)
        {
            Padding = new Thickness(6, 5, 6, 5),
            BorderBrush = Linea,
            BorderThickness = new Thickness(0, 0, 0, 0.5)
        };
    }

    private static Paragraph Parrafo(string texto, double tamano, FontWeight peso,
        Brush? color = null, double espacioAntes = 0, double espacioDespues = 0) =>
        new(new Run(texto))
        {
            FontSize = tamano, FontWeight = peso, Foreground = color ?? Tinta,
            Margin = new Thickness(0, espacioAntes, 0, espacioDespues)
        };
}
