using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FAControl.Models;

namespace FAControl.Printing;

/// <summary>
/// Construye el visual imprimible de un préstamo en HOJA CARTA.
/// A diferencia del recibo (80mm, monoespaciado), esto es un documento
/// formal con la tabla de amortización completa.
/// El mismo visual va a pantalla y a la impresora (patrón del recibo).
/// </summary>
public static class PrestamoVisualFactory
{
    /// <summary>Ancho de hoja carta a 96 DPI (8.5" × 96).</summary>
    public const double AnchoCarta = 816;

    private const double MargenHoja = 48;
    private static readonly CultureInfo CulturaRd = CultureInfo.GetCultureInfo("es-DO");
    private static readonly FontFamily Fuente = new("Segoe UI");
    private static readonly Brush Tinta = new SolidColorBrush(Color.FromRgb(0x0D, 0x1B, 0x2A));
    private static readonly Brush TintaSuave = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80));
    private static readonly Brush Linea = new SolidColorBrush(Color.FromRgb(0xE6, 0xE7, 0xEB));
    private static readonly Brush FondoEncabezado = new SolidColorBrush(Color.FromRgb(0xF4, 0xF5, 0xF7));

    public static FrameworkElement Crear(PrestamoImpreso p)
    {
        var raiz = new StackPanel
        {
            Width = AnchoCarta,
            Background = Brushes.White,
            Margin = new Thickness(0)
        };
        var contenido = new StackPanel { Margin = new Thickness(MargenHoja, 40, MargenHoja, 40) };
        raiz.Children.Add(contenido);

        // --- Encabezado ---
        contenido.Children.Add(Texto("ESTADO DE PRÉSTAMO", 20, FontWeights.Bold));
        contenido.Children.Add(Texto($"Préstamo {p.Codigo} · {p.EstadoTexto}", 12, FontWeights.Normal, TintaSuave));
        contenido.Children.Add(Separador(16));

        // --- Datos del cliente y del contrato, en dos columnas ---
        var datos = new Grid { Margin = new Thickness(0, 0, 0, 18) };
        datos.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        datos.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var izquierda = new StackPanel();
        izquierda.Children.Add(Etiqueta("CLIENTE"));
        izquierda.Children.Add(Texto(p.ClienteNombre, 13, FontWeights.SemiBold));
        izquierda.Children.Add(Texto($"Cédula: {p.ClienteCedula}", 11, FontWeights.Normal, TintaSuave));
        izquierda.Children.Add(new Border { Height = 10 });
        izquierda.Children.Add(Etiqueta("GARANTÍA"));
        izquierda.Children.Add(Texto(p.GarantiaTexto, 11));
        Grid.SetColumn(izquierda, 0);
        datos.Children.Add(izquierda);

        var derecha = new StackPanel();
        derecha.Children.Add(FilaDato("Capital prestado", Moneda(p.MontoCapital)));
        derecha.Children.Add(FilaDato("Tasa", p.TasaTexto));
        derecha.Children.Add(FilaDato("Modalidad", p.ModalidadTexto));
        derecha.Children.Add(FilaDato("Método de cálculo", p.MetodoTexto));
        derecha.Children.Add(FilaDato("Primer pago", p.FechaPrimerPagoTexto));
        Grid.SetColumn(derecha, 1);
        datos.Children.Add(derecha);
        contenido.Children.Add(datos);

        // --- Resumen económico ---
        contenido.Children.Add(Separador(0));
        var resumen = new Grid { Margin = new Thickness(0, 12, 0, 12) };
        for (var i = 0; i < 4; i++)
            resumen.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AgregarMetrica(resumen, 0, "TOTAL A PAGAR", Moneda(p.TotalAPagar));
        AgregarMetrica(resumen, 1, "PAGADO", Moneda(p.TotalPagado));
        AgregarMetrica(resumen, 2, "SALDO PENDIENTE", Moneda(p.SaldoPendiente));
        AgregarMetrica(resumen, 3, "PROGRESO", p.ProgresoTexto);
        contenido.Children.Add(resumen);
        contenido.Children.Add(Separador(0));

        // --- Tabla de amortización ---
        contenido.Children.Add(Texto("Tabla de amortización", 13, FontWeights.SemiBold,
            margen: new Thickness(0, 18, 0, 8)));
        contenido.Children.Add(TablaCuotas(p.Cuotas));

        // --- Pie ---
        contenido.Children.Add(Separador(18));
        contenido.Children.Add(Texto(
            $"Emitido por {p.EmitidoPor} el {DateTime.Now.ToString("dd/MM/yyyy 'a las' hh:mm tt", CulturaRd)}",
            9, FontWeights.Normal, TintaSuave));

        // Layout explícito: el visual debe estar medido ANTES de imprimirse
        raiz.Measure(new Size(AnchoCarta, double.PositiveInfinity));
        raiz.Arrange(new Rect(0, 0, AnchoCarta, raiz.DesiredSize.Height));
        raiz.UpdateLayout();
        return raiz;
    }

    private static UIElement TablaCuotas(IReadOnlyList<CuotaImpresa> cuotas)
    {
        var tabla = new Grid();
        double[] anchos = [40, 92, 108, 100, 108, 116, 76];
        foreach (var a in anchos)
            tabla.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(a) });
        for (var i = 0; i <= cuotas.Count; i++)
            tabla.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        string[] encabezados = ["N°", "Vencimiento", "Capital", "Interés", "Cuota", "Saldo restante", "Estado"];
        for (var c = 0; c < encabezados.Length; c++)
        {
            var celda = new Border
            {
                Background = FondoEncabezado,
                BorderBrush = Linea,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(6, 6, 6, 6),
                Child = Texto(encabezados[c], 9, FontWeights.SemiBold, TintaSuave,
                    alineacion: c >= 2 && c <= 5 ? TextAlignment.Right : TextAlignment.Left)
            };
            Grid.SetRow(celda, 0);
            Grid.SetColumn(celda, c);
            tabla.Children.Add(celda);
        }

        for (var f = 0; f < cuotas.Count; f++)
        {
            var q = cuotas[f];
            string[] valores =
            [
                q.Numero.ToString(CulturaRd), q.FechaTexto, Moneda(q.Capital), Moneda(q.Interes),
                Moneda(q.MontoTotal), Moneda(q.SaldoDespues), q.EstadoTexto
            ];
            for (var c = 0; c < valores.Length; c++)
            {
                var celda = new Border
                {
                    BorderBrush = Linea,
                    BorderThickness = new Thickness(0, 0, 0, 0.5),
                    Padding = new Thickness(6, 5, 6, 5),
                    Child = Texto(valores[c], 9.5,
                        c == 4 ? FontWeights.SemiBold : FontWeights.Normal,
                        alineacion: c >= 2 && c <= 5 ? TextAlignment.Right : TextAlignment.Left)
                };
                Grid.SetRow(celda, f + 1);
                Grid.SetColumn(celda, c);
                tabla.Children.Add(celda);
            }
        }
        return tabla;
    }

    private static void AgregarMetrica(Grid destino, int columna, string etiqueta, string valor)
    {
        var panel = new StackPanel();
        panel.Children.Add(Etiqueta(etiqueta));
        panel.Children.Add(Texto(valor, 13, FontWeights.SemiBold));
        Grid.SetColumn(panel, columna);
        destino.Children.Add(panel);
    }

    private static UIElement FilaDato(string etiqueta, string valor)
    {
        var fila = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        fila.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        fila.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var e = Texto(etiqueta, 11, FontWeights.Normal, TintaSuave);
        var v = Texto(valor, 11, FontWeights.SemiBold, alineacion: TextAlignment.Right);
        Grid.SetColumn(e, 0);
        Grid.SetColumn(v, 1);
        fila.Children.Add(e);
        fila.Children.Add(v);
        return fila;
    }

    private static TextBlock Etiqueta(string texto) =>
        Texto(texto, 8.5, FontWeights.SemiBold, TintaSuave);

    private static TextBlock Texto(string contenido, double tamano,
        FontWeight? peso = null, Brush? color = null,
        TextAlignment alineacion = TextAlignment.Left, Thickness? margen = null) =>
        new()
        {
            Text = contenido,
            FontFamily = Fuente,
            FontSize = tamano,
            FontWeight = peso ?? FontWeights.Normal,
            Foreground = color ?? Tinta,
            TextAlignment = alineacion,
            Margin = margen ?? new Thickness(0),
            TextWrapping = TextWrapping.NoWrap
        };

    private static Border Separador(double margenSuperior) => new()
    {
        Height = 1,
        Background = Linea,
        Margin = new Thickness(0, margenSuperior, 0, 0)
    };

    private static string Moneda(decimal valor) => $"RD$ {valor.ToString("N2", CulturaRd)}";
}
