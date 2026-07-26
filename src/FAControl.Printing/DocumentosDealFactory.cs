using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using FAControl.Models;

namespace FAControl.Printing;

/// <summary>
/// Documentos del financiamiento del dealer (016 — pedido 2026-07-25):
/// carta de compromiso de pago y recibo de separación.
///
/// FlowDocument y no un Visual fijo: la carta lleva la tabla de plazos, que
/// puede no caber en una hoja (mismo motivo que el pagaré). El DocumentPaginator
/// reparte la tabla en las páginas que hagan falta.
/// </summary>
public static class DocumentosDealFactory
{
    private static readonly CultureInfo CulturaRd = CultureInfo.GetCultureInfo("es-DO");
    private static readonly FontFamily Fuente = new("Segoe UI");
    private static readonly Brush Tinta = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
    private static readonly Brush TintaSuave = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x53));
    private static readonly Brush Linea = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));
    private static readonly Color Navy = Color.FromRgb(0x1B, 0x26, 0x3B);
    private static readonly Color Oro = Color.FromRgb(0xC9, 0xA1, 0x5A);
    private static readonly Brush BrushNavy = Congelar(Navy);
    private static readonly Brush BrushOro = Congelar(Oro);

    private static Brush Congelar(Color color)
    {
        var b = new SolidColorBrush(color);
        b.Freeze();
        return b;
    }

    // ============================================================
    // Carta de compromiso de pago
    // ============================================================

    public static FlowDocument CrearCartaCompromiso(CartaCompromisoImpresa c)
    {
        var doc = NuevoDocumento();

        doc.Blocks.Add(EncabezadoMarca(c.NegocioNombre, Contacto(c.NegocioRnc, c.NegocioTelefono, c.NegocioCiudad)));
        doc.Blocks.Add(ReglaDorada());

        var titulo = Parrafo("Carta de Compromiso de Pago", 20, FontWeights.Bold, espacioDespues: 4);
        titulo.TextAlignment = TextAlignment.Center;
        doc.Blocks.Add(titulo);
        var subtitulo = Parrafo($"Venta {c.Codigo}  ·  {c.FechaTexto}", 11, FontWeights.Normal,
            TintaSuave, espacioDespues: 16);
        subtitulo.TextAlignment = TextAlignment.Center;
        doc.Blocks.Add(subtitulo);

        // Declaración
        var declaracion = new Paragraph { Margin = new Thickness(0, 0, 0, 12), LineHeight = 20 };
        declaracion.Inlines.Add(new Run("Yo, "));
        declaracion.Inlines.Add(new Bold(new Run(c.ClienteNombre)));
        declaracion.Inlines.Add(new Run($", portador(a) de la cédula/pasaporte No. {c.ClienteCedula}"));
        if (!string.IsNullOrWhiteSpace(c.ClienteDireccion) && c.ClienteDireccion != "—")
            declaracion.Inlines.Add(new Run($", domiciliado(a) en {c.ClienteDireccion}"));
        declaracion.Inlines.Add(new Run(", declaro haber adquirido de "));
        declaracion.Inlines.Add(new Bold(new Run(c.NegocioNombre)));
        declaracion.Inlines.Add(new Run(" el vehículo que se describe a continuación, y "));
        declaracion.Inlines.Add(new Bold(new Run("me comprometo formalmente")));
        declaracion.Inlines.Add(new Run(" a pagar el saldo pendiente conforme al calendario de pagos aquí detallado:"));
        doc.Blocks.Add(declaracion);

        // Vehículo
        doc.Blocks.Add(Etiqueta("VEHÍCULO"));
        doc.Blocks.Add(TablaDatos(
        [
            ("Descripción", c.VehiculoDescripcion), ("Año", c.AnioTexto),
            ("Chasis (VIN)", c.Vin), ("Color", c.Color),
            ("Placa", c.Placa), ("Matrícula", c.Matricula)
        ]));

        // Montos
        doc.Blocks.Add(Etiqueta("CONDICIONES DE PAGO"));
        doc.Blocks.Add(TablaDatos(
        [
            ("Precio de venta", Moneda(c.Precio)), ("Inicial recibida", Moneda(c.Inicial)),
            ("Total a pagar en plazos", Moneda(c.TotalAPlazos)), ("Cantidad de plazos", c.Plazos.Count.ToString(CulturaRd))
        ]));

        // Calendario
        doc.Blocks.Add(Parrafo("Calendario de pagos", 13, FontWeights.SemiBold,
            espacioAntes: 12, espacioDespues: 8));
        doc.Blocks.Add(TablaPlazos(c.Plazos));

        var total = Parrafo($"Total comprometido: {Moneda(c.Plazos.Sum(p => p.Monto))}",
            12, FontWeights.Bold, espacioAntes: 10, espacioDespues: 18);
        total.TextAlignment = TextAlignment.Right;
        doc.Blocks.Add(total);

        // Cláusulas
        doc.Blocks.Add(Parrafo(
            "El incumplimiento de dos (2) o más plazos consecutivos faculta al vendedor a exigir " +
            "el pago total del saldo pendiente y a ejercer las acciones legales que correspondan, " +
            "quedando afectados todos mis bienes habidos y por haber.",
            10.5, FontWeights.Normal, espacioDespues: 6));
        doc.Blocks.Add(Parrafo(
            "El vehículo permanece bajo mi responsabilidad y guarda desde su entrega, incluyendo " +
            "el mantenimiento de la póliza de seguro obligatorio vigente conforme a la Ley 4117.",
            10.5, FontWeights.Normal, espacioDespues: 36));

        doc.Blocks.Add(Firmas(c.ClienteNombre, "EL COMPRADOR", c.NegocioNombre, "EL VENDEDOR"));

        var pie = Parrafo($"Emitido por {c.EmitidoPor} el {DateTime.Now.ToString(@"dd'/'MM'/'yyyy hh':'mm tt", CulturaRd)}",
            9, FontWeights.Normal, TintaSuave, espacioAntes: 26);
        pie.TextAlignment = TextAlignment.Center;
        doc.Blocks.Add(pie);

        return doc;
    }

    // ============================================================
    // Recibo de separación
    // ============================================================

    public static FlowDocument CrearReciboSeparacion(ReciboSeparacionImpreso r)
    {
        var doc = NuevoDocumento();

        doc.Blocks.Add(EncabezadoMarca(r.NegocioNombre, Contacto(r.NegocioRnc, r.NegocioTelefono, r.NegocioCiudad)));
        doc.Blocks.Add(ReglaDorada());

        var titulo = Parrafo("Recibo de Separación", 20, FontWeights.Bold, espacioDespues: 4);
        titulo.TextAlignment = TextAlignment.Center;
        doc.Blocks.Add(titulo);
        var subtitulo = Parrafo($"{r.Codigo}  ·  {r.FechaTexto}", 11, FontWeights.Normal,
            TintaSuave, espacioDespues: 16);
        subtitulo.TextAlignment = TextAlignment.Center;
        doc.Blocks.Add(subtitulo);

        var declaracion = new Paragraph { Margin = new Thickness(0, 0, 0, 14), LineHeight = 20 };
        declaracion.Inlines.Add(new Run("Recibimos de "));
        declaracion.Inlines.Add(new Bold(new Run(r.ClienteNombre)));
        declaracion.Inlines.Add(new Run($", portador(a) de la cédula/pasaporte No. {r.ClienteCedula}, la suma de "));
        declaracion.Inlines.Add(new Bold(new Run(Moneda(r.Adelanto))));
        declaracion.Inlines.Add(new Run(" como "));
        declaracion.Inlines.Add(new Bold(new Run("adelanto de separación")));
        declaracion.Inlines.Add(new Run(" del vehículo descrito a continuación."));
        doc.Blocks.Add(declaracion);

        doc.Blocks.Add(Etiqueta("VEHÍCULO SEPARADO"));
        doc.Blocks.Add(TablaDatos(
        [
            ("Descripción", r.VehiculoDescripcion), ("Año", r.AnioTexto),
            ("Chasis (VIN)", r.Vin), ("Color", r.Color),
            ("Placa", r.Placa), ("Teléfono del cliente", r.ClienteTelefono)
        ]));

        doc.Blocks.Add(Etiqueta("MONTOS"));
        doc.Blocks.Add(TablaDatos(
        [
            ("Precio de venta", Moneda(r.Precio)), ("Adelanto recibido", Moneda(r.Adelanto)),
            ("Pendiente por completar", Moneda(r.Pendiente)), ("Válido hasta", r.FechaLimiteTexto)
        ]));

        // El derecho de los N días es LA cláusula del recibo de separación
        var aviso = new Paragraph
        {
            Margin = new Thickness(0, 16, 0, 12),
            Padding = new Thickness(12),
            Background = new SolidColorBrush(Color.FromRgb(0xFD, 0xF6, 0xE3)),
            LineHeight = 18
        };
        aviso.Inlines.Add(new Bold(new Run($"El cliente tiene {r.DiasDerecho} días de derecho sobre esta separación. ")));
        aviso.Inlines.Add(new Run(
            $"El vehículo queda reservado a su nombre hasta el {r.FechaLimiteTexto}. " +
            "Si al vencer ese plazo no se ha completado el pago, la separación queda sin efecto " +
            "y el vehículo vuelve a estar disponible para la venta."));
        doc.Blocks.Add(aviso);

        doc.Blocks.Add(Parrafo(
            "Este recibo no constituye contrato de venta. La venta se formaliza al completar el " +
            "pago acordado o al firmar la carta de compromiso correspondiente.",
            10.5, FontWeights.Normal, TintaSuave, espacioDespues: 40));

        doc.Blocks.Add(Firmas(r.ClienteNombre, "EL CLIENTE", r.NegocioNombre, "RECIBIDO POR"));

        var pie = Parrafo($"Emitido por {r.EmitidoPor} el {DateTime.Now.ToString(@"dd'/'MM'/'yyyy hh':'mm tt", CulturaRd)}",
            9, FontWeights.Normal, TintaSuave, espacioAntes: 26);
        pie.TextAlignment = TextAlignment.Center;
        doc.Blocks.Add(pie);

        return doc;
    }

    // ============================================================
    // Bloques compartidos
    // ============================================================

    private static FlowDocument NuevoDocumento() => new()
    {
        FontFamily = Fuente,
        Foreground = Tinta,
        PagePadding = new Thickness(64),
        ColumnWidth = double.PositiveInfinity,
        FontSize = 12,
        Background = Brushes.White,
        PageWidth = 816,     // carta 8.5" a 96 DPI
        PageHeight = 1056
    };

    private static string Contacto(params string?[] partes) =>
        string.Join("  ·  ", partes.Where(p => !string.IsNullOrWhiteSpace(p)));

    private static BlockUIContainer EncabezadoMarca(string nombreNegocio, string subtitulo)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var badge = LogoFa.Badge(60);
        Grid.SetColumn(badge, 0);
        grid.Children.Add(badge);

        var textos = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
        textos.Children.Add(new TextBlock
        {
            Text = nombreNegocio,
            FontFamily = Fuente,
            FontSize = 18,
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
        new(new Border { Height = 2.5, Background = BrushOro, Margin = new Thickness(0, 0, 0, 18) })
        {
            Margin = new Thickness(0)
        };

    private static Paragraph Etiqueta(string texto) =>
        Parrafo(texto, 8.5, FontWeights.SemiBold, TintaSuave, espacioAntes: 10, espacioDespues: 4);

    /// <summary>Tabla de dos columnas de pares etiqueta/valor (dos pares por fila).</summary>
    private static Table TablaDatos(IReadOnlyList<(string Etiqueta, string Valor)> datos)
    {
        var tabla = new Table { CellSpacing = 0, Margin = new Thickness(0, 0, 0, 8) };
        tabla.Columns.Add(new TableColumn { Width = new GridLength(120) });
        tabla.Columns.Add(new TableColumn { Width = new GridLength(224) });
        tabla.Columns.Add(new TableColumn { Width = new GridLength(120) });
        tabla.Columns.Add(new TableColumn { Width = new GridLength(224) });

        var grupo = new TableRowGroup();
        for (var i = 0; i < datos.Count; i += 2)
        {
            var fila = new TableRow();
            fila.Cells.Add(CeldaEtiqueta(datos[i].Etiqueta));
            fila.Cells.Add(CeldaValor(datos[i].Valor));
            if (i + 1 < datos.Count)
            {
                fila.Cells.Add(CeldaEtiqueta(datos[i + 1].Etiqueta));
                fila.Cells.Add(CeldaValor(datos[i + 1].Valor));
            }
            else
            {
                fila.Cells.Add(new TableCell());
                fila.Cells.Add(new TableCell());
            }
            grupo.Rows.Add(fila);
        }
        tabla.RowGroups.Add(grupo);
        return tabla;
    }

    private static TableCell CeldaEtiqueta(string texto) =>
        new(new Paragraph(new Run(texto))
        {
            FontSize = 10,
            Foreground = TintaSuave,
            Margin = new Thickness(0)
        })
        { Padding = new Thickness(0, 3, 6, 3) };

    private static TableCell CeldaValor(string? texto) =>
        new(new Paragraph(new Run(string.IsNullOrWhiteSpace(texto) ? "—" : texto))
        {
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0)
        })
        { Padding = new Thickness(0, 3, 12, 3) };

    private static Table TablaPlazos(IReadOnlyList<PlazoImpreso> plazos)
    {
        // Anchos FIJOS que suman menos que el ancho útil de la hoja (688px):
        // con columna estrella la tabla se estira y la última columna se sale.
        var tabla = new Table { CellSpacing = 0, Margin = new Thickness(0) };
        tabla.Columns.Add(new TableColumn { Width = new GridLength(60) });
        tabla.Columns.Add(new TableColumn { Width = new GridLength(180) });
        tabla.Columns.Add(new TableColumn { Width = new GridLength(160) });
        tabla.Columns.Add(new TableColumn { Width = new GridLength(140) });

        var grupo = new TableRowGroup();
        var cab = new TableRow { Background = new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2)) };
        cab.Cells.Add(Celda("#", FontWeights.SemiBold));
        cab.Cells.Add(Celda("Vencimiento", FontWeights.SemiBold));
        cab.Cells.Add(Celda("Monto", FontWeights.SemiBold, TextAlignment.Right));
        cab.Cells.Add(Celda("Estado", FontWeights.SemiBold));
        grupo.Rows.Add(cab);

        foreach (var p in plazos)
        {
            var fila = new TableRow();
            fila.Cells.Add(Celda(p.Numero.ToString(CulturaRd), FontWeights.Normal));
            fila.Cells.Add(Celda(p.FechaTexto, FontWeights.Normal));
            fila.Cells.Add(Celda(Moneda(p.Monto), FontWeights.Normal, TextAlignment.Right));
            fila.Cells.Add(Celda(p.EstadoTexto, FontWeights.Normal));
            grupo.Rows.Add(fila);
        }

        tabla.RowGroups.Add(grupo);
        return tabla;
    }

    private static TableCell Celda(string texto, FontWeight peso,
        TextAlignment alineacion = TextAlignment.Left) =>
        new(new Paragraph(new Run(texto))
        {
            FontWeight = peso,
            FontSize = 11,
            Margin = new Thickness(0),
            TextAlignment = alineacion
        })
        {
            Padding = new Thickness(6, 4, 6, 4),
            BorderBrush = Linea,
            BorderThickness = new Thickness(0, 0, 0, 0.5)
        };

    private static Table Firmas(string nombreIzq, string rolIzq, string nombreDer, string rolDer)
    {
        // Anchos FIJOS: con columnas estrella las celdas colapsan y el nombre
        // sale en vertical (una letra por línea).
        var tabla = new Table { CellSpacing = 0 };
        tabla.Columns.Add(new TableColumn { Width = new GridLength(300) });
        tabla.Columns.Add(new TableColumn { Width = new GridLength(88) });
        tabla.Columns.Add(new TableColumn { Width = new GridLength(300) });

        var grupo = new TableRowGroup();
        var fila = new TableRow();
        fila.Cells.Add(CeldaFirma(nombreIzq, rolIzq));
        fila.Cells.Add(new TableCell());
        fila.Cells.Add(CeldaFirma(nombreDer, rolDer));
        grupo.Rows.Add(fila);
        tabla.RowGroups.Add(grupo);
        return tabla;
    }

    private static TableCell CeldaFirma(string nombre, string rol)
    {
        var celda = new TableCell();
        celda.Blocks.Add(new Paragraph(new Run("____________________________"))
        {
            Margin = new Thickness(0),
            TextAlignment = TextAlignment.Center,
            Foreground = TintaSuave
        });
        celda.Blocks.Add(new Paragraph(new Run(nombre))
        {
            Margin = new Thickness(0, 2, 0, 0),
            FontSize = 11,
            TextAlignment = TextAlignment.Center
        });
        celda.Blocks.Add(new Paragraph(new Run(rol))
        {
            Margin = new Thickness(0),
            FontSize = 9,
            Foreground = TintaSuave,
            TextAlignment = TextAlignment.Center
        });
        return celda;
    }

    private static string Moneda(decimal valor) => $"RD$ {valor.ToString("N2", CulturaRd)}";

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
