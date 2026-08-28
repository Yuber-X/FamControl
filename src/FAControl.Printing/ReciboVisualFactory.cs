using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FAControl.Common;
using FAControl.Models;

namespace FAControl.Printing;

/// <summary>
/// Construye el recibo de pago como visual WPF de 80mm (patrón del POS-400:
/// el ticket se arma como imagen y ese MISMO visual se imprime y se exporta a PDF,
/// garantizando que papel y archivo sean idénticos).
/// </summary>
public static class ReciboVisualFactory
{
    /// <summary>80mm a 96 DPI ≈ 302 px.</summary>
    public const double AnchoRecibo = 302;

    private static readonly CultureInfo CulturaRd = CultureInfo.GetCultureInfo("es-DO");
    private static readonly FontFamily Fuente = new("Consolas");

    public static FrameworkElement Crear(ReciboPago recibo)
    {
        var panel = new StackPanel { Margin = new Thickness(12) };

        // Encabezado con la marca (pedido 2026-07-25): logo FA + nombre de la
        // empresa + RNC + teléfono. Con fallback si aún no se configuraron.
        var logo = LogoFa.Badge(46);
        logo.HorizontalAlignment = HorizontalAlignment.Center;
        logo.Margin = new Thickness(0, 0, 0, 4);
        panel.Children.Add(logo);

        var nombreNegocio = string.IsNullOrWhiteSpace(recibo.NegocioNombre) ? "FAControl" : recibo.NegocioNombre;
        panel.Children.Add(Linea(nombreNegocio, 14, FontWeights.Bold, TextAlignment.Center));
        if (!string.IsNullOrWhiteSpace(recibo.NegocioRnc))
            panel.Children.Add(Linea($"RNC: {recibo.NegocioRnc}", 10, alineacion: TextAlignment.Center));
        if (!string.IsNullOrWhiteSpace(recibo.NegocioTelefono))
            panel.Children.Add(Linea($"Tel.: {recibo.NegocioTelefono}", 10, alineacion: TextAlignment.Center));

        panel.Children.Add(Linea("RECIBO DE PAGO", 12, FontWeights.Bold, TextAlignment.Center));
        panel.Children.Add(Separador());

        panel.Children.Add(Fila("Recibo:", recibo.NumeroReciboPrincipal));
        if (!string.IsNullOrWhiteSpace(recibo.Ncf))
            panel.Children.Add(Fila("Comprobante (NCF):", recibo.Ncf));
        panel.Children.Add(Fila("Fecha:", FechaNegocio.AUtcLocal(recibo.FechaPagoUtc).ToString(@"dd'/'MM'/'yyyy hh':'mm tt", CulturaRd)));
        panel.Children.Add(Fila("Cliente:", recibo.ClienteNombre));
        panel.Children.Add(Fila("Préstamo:", recibo.PrestamoCodigo));
        panel.Children.Add(Fila("Método:", TextoMetodo(recibo.MetodoPago)));
        panel.Children.Add(Separador());

        // Detalle por cuota
        foreach (var linea in recibo.Lineas)
        {
            panel.Children.Add(Linea($"Cuota #{linea.NumeroCuota}  ({linea.NumeroRecibo})", 11, FontWeights.Bold));
            if (linea.InteresAplicado > 0m)
                panel.Children.Add(Fila("  Interés:", Moneda(linea.InteresAplicado)));
            if (linea.CapitalAplicado > 0m)
                panel.Children.Add(Fila("  Capital:", Moneda(linea.CapitalAplicado)));
            // "Abonado" es un pago PARCIAL en el habla dominicana. Usarlo para
            // una cuota pagada completa hizo que el cliente lo reportara como
            // error (2026-08-27): "dice abonado y pone el total de interes y
            // capital pagado en una cuota normal". La palabra sigue al hecho.
            panel.Children.Add(Fila(
                linea.QuedaPagada ? "  Pagado:" : "  Abonado (parcial):",
                Moneda(linea.MontoAplicado)));
        }
        panel.Children.Add(Separador());

        // Totales
        panel.Children.Add(Fila("TOTAL PAGADO:", Moneda(recibo.TotalPagado), negrita: true));
        if (recibo.InteresExonerado > 0m)
            panel.Children.Add(Fila("Interés exonerado:", Moneda(recibo.InteresExonerado)));
        panel.Children.Add(Fila("Saldo restante:", Moneda(recibo.SaldoRestantePrestamo)));

        if (!string.IsNullOrWhiteSpace(recibo.Notas))
        {
            panel.Children.Add(Separador());
            panel.Children.Add(Linea($"Nota: {recibo.Notas}", 10));
        }

        panel.Children.Add(Separador());
        panel.Children.Add(Fila("Le atendió:", recibo.CobradoPor));
        panel.Children.Add(Linea("¡Gracias por su pago!", 11, FontWeights.Bold, TextAlignment.Center));

        var contenedor = new Border
        {
            Background = Brushes.White,
            Width = AnchoRecibo,
            Child = panel
        };
        // Medir/organizar para que la impresión y el PDF conozcan el alto real
        contenedor.Measure(new Size(AnchoRecibo, double.PositiveInfinity));
        contenedor.Arrange(new Rect(contenedor.DesiredSize));
        return contenedor;
    }

    private static string Moneda(decimal valor) => $"RD$ {valor.ToString("N2", CulturaRd)}";

    private static string TextoMetodo(MetodoPago metodo) => metodo switch
    {
        MetodoPago.Efectivo => "Efectivo",
        MetodoPago.Transferencia => "Transferencia",
        MetodoPago.Cheque => "Cheque",
        MetodoPago.Otro => "Otro",
        _ => metodo.ToString()
    };

    private static TextBlock Linea(string texto, double tamano,
        FontWeight? peso = null, TextAlignment alineacion = TextAlignment.Left) => new()
    {
        Text = texto,
        FontFamily = Fuente,
        FontSize = tamano,
        FontWeight = peso ?? FontWeights.Normal,
        TextAlignment = alineacion,
        TextWrapping = TextWrapping.Wrap,
        Foreground = Brushes.Black,
        Margin = new Thickness(0, 1, 0, 1)
    };

    private static Grid Fila(string etiqueta, string valor, bool negrita = false)
    {
        var fila = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        fila.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        fila.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var izquierda = Linea(etiqueta, 11, negrita ? FontWeights.Bold : FontWeights.Normal);
        var derecha = Linea(valor, 11, negrita ? FontWeights.Bold : FontWeights.Normal, TextAlignment.Right);
        Grid.SetColumn(izquierda, 0);
        Grid.SetColumn(derecha, 1);
        fila.Children.Add(izquierda);
        fila.Children.Add(derecha);
        return fila;
    }

    private static TextBlock Separador() =>
        Linea(new string('-', 38), 11, alineacion: TextAlignment.Center);
}
