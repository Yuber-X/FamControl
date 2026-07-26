using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FAControl.Models;

namespace FAControl.Printing;

/// <summary>
/// Ficha imprimible del vehículo en HOJA CARTA (pedido 2026-07-25): marca del
/// negocio + datos completos + comprador + historial de reparaciones.
/// El mismo visual va a pantalla y a la impresora (patrón del recibo).
/// </summary>
public static class FichaVehiculoVisualFactory
{
    public const double AnchoCarta = 816;

    private const double MargenHoja = 48;
    private static readonly CultureInfo CulturaRd = CultureInfo.GetCultureInfo("es-DO");
    private static readonly FontFamily Fuente = new("Segoe UI");
    private static readonly Brush Tinta = new SolidColorBrush(Color.FromRgb(0x0D, 0x1B, 0x2A));
    private static readonly Brush TintaSuave = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80));
    private static readonly Brush Linea = new SolidColorBrush(Color.FromRgb(0xE6, 0xE7, 0xEB));
    private static readonly Brush FondoEncabezado = new SolidColorBrush(Color.FromRgb(0xF4, 0xF5, 0xF7));

    public static FrameworkElement Crear(FichaVehiculoImpresa f)
    {
        var raiz = new StackPanel { Width = AnchoCarta, Background = Brushes.White };
        var contenido = new StackPanel { Margin = new Thickness(MargenHoja, 40, MargenHoja, 40) };
        raiz.Children.Add(contenido);

        // --- Marca del negocio ---
        if (!string.IsNullOrWhiteSpace(f.NegocioNombre))
        {
            var marca = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            marca.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            marca.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var badge = LogoFa.Badge(52);
            Grid.SetColumn(badge, 0);
            marca.Children.Add(badge);
            var textos = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
            textos.Children.Add(Texto(f.NegocioNombre, 16, FontWeights.Bold));
            var contacto = new[]
            {
                string.IsNullOrWhiteSpace(f.NegocioRnc) ? null : $"RNC {f.NegocioRnc}",
                string.IsNullOrWhiteSpace(f.NegocioTelefono) ? null : $"Tel. {f.NegocioTelefono}"
            }.Where(s => s is not null);
            var lineaContacto = string.Join("  ·  ", contacto);
            if (lineaContacto.Length > 0)
                textos.Children.Add(Texto(lineaContacto, 10, FontWeights.Normal, TintaSuave));
            Grid.SetColumn(textos, 1);
            marca.Children.Add(textos);
            contenido.Children.Add(marca);
        }

        // --- Título ---
        contenido.Children.Add(Texto("FICHA DEL VEHÍCULO", 20, FontWeights.Bold));
        contenido.Children.Add(Texto($"{f.Codigo} · {f.Descripcion} · {f.EstadoTexto}", 12, FontWeights.Normal, TintaSuave));
        contenido.Children.Add(Separador(16));

        // --- Datos del vehículo en dos columnas ---
        var datos = new Grid { Margin = new Thickness(0, 12, 0, 12) };
        datos.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        datos.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        datos.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var izquierda = new StackPanel();
        izquierda.Children.Add(FilaDato("Chasis (VIN)", f.Vin));
        izquierda.Children.Add(FilaDato("Placa", f.Placa));
        izquierda.Children.Add(FilaDato("Matrícula", f.Matricula));
        izquierda.Children.Add(FilaDato("Color", f.Color));
        Grid.SetColumn(izquierda, 0);
        datos.Children.Add(izquierda);

        var derecha = new StackPanel();
        derecha.Children.Add(FilaDato("Año", f.AnioTexto));
        derecha.Children.Add(FilaDato("Tipo", f.TipoTexto));
        derecha.Children.Add(FilaDato("Kilometraje", f.KilometrajeTexto));
        derecha.Children.Add(FilaDato("Precio de venta", Moneda(f.PrecioVenta)));
        Grid.SetColumn(derecha, 2);
        datos.Children.Add(derecha);
        contenido.Children.Add(datos);

        if (!string.IsNullOrWhiteSpace(f.Notas))
        {
            contenido.Children.Add(Etiqueta("NOTA / CONDICIÓN DEL VEHÍCULO"));
            contenido.Children.Add(Texto(f.Notas!, 11, margen: new Thickness(0, 2, 0, 12), envolver: true));
        }

        // --- Costos (solo Encargado/Admin) ---
        if (f.MostrarCostos)
        {
            contenido.Children.Add(Separador(0));
            var costos = new Grid { Margin = new Thickness(0, 12, 0, 12) };
            for (var i = 0; i < 4; i++)
                costos.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            AgregarMetrica(costos, 0, "COSTO ADQUISICIÓN", Moneda(f.CostoAdquisicion));
            AgregarMetrica(costos, 1, "GASTOS IMPORTACIÓN", Moneda(f.GastosImportacion));
            AgregarMetrica(costos, 2, "COSTO TOTAL", Moneda(f.CostoTotal));
            AgregarMetrica(costos, 3, "GANANCIA ESTIMADA", Moneda(f.PrecioVenta - f.CostoTotal));
            contenido.Children.Add(costos);
        }

        // --- Comprador ---
        contenido.Children.Add(Separador(0));
        contenido.Children.Add(Etiqueta("COMPRADOR / CONTRATO"));
        contenido.Children.Add(Texto(
            string.IsNullOrWhiteSpace(f.CompradorTexto) ? "Sin venta registrada." : f.CompradorTexto!,
            11.5, FontWeights.SemiBold, margen: new Thickness(0, 2, 0, 12), envolver: true));

        // --- Reparaciones ---
        contenido.Children.Add(Texto("Historial de reparaciones", 13, FontWeights.SemiBold,
            margen: new Thickness(0, 6, 0, 8)));
        if (f.Reparaciones.Count == 0)
        {
            contenido.Children.Add(Texto("Sin reparaciones registradas.", 11, FontWeights.Normal, TintaSuave));
        }
        else
        {
            contenido.Children.Add(TablaReparaciones(f.Reparaciones));
            var total = Texto($"Total en reparaciones: {Moneda(f.CostoReparaciones)}", 11.5,
                FontWeights.SemiBold, margen: new Thickness(0, 8, 0, 0));
            total.TextAlignment = TextAlignment.Right;
            contenido.Children.Add(total);
        }

        // --- Pie ---
        contenido.Children.Add(Separador(18));
        contenido.Children.Add(Texto(
            $"Emitido por {f.EmitidoPor} el {DateTime.Now.ToString(@"dd'/'MM'/'yyyy 'a las' hh':'mm tt", CulturaRd)}",
            9, FontWeights.Normal, TintaSuave, margen: new Thickness(0, 8, 0, 0)));

        raiz.Measure(new Size(AnchoCarta, double.PositiveInfinity));
        raiz.Arrange(new Rect(0, 0, AnchoCarta, raiz.DesiredSize.Height));
        raiz.UpdateLayout();
        return raiz;
    }

    private static UIElement TablaReparaciones(IReadOnlyList<ReparacionImpresa> reparaciones)
    {
        var tabla = new Grid();
        double[] anchos = [110, 480, 130];
        foreach (var a in anchos)
            tabla.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(a) });
        for (var i = 0; i <= reparaciones.Count; i++)
            tabla.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        string[] encabezados = ["Fecha", "Detalle", "Costo"];
        for (var c = 0; c < encabezados.Length; c++)
        {
            var celda = new Border
            {
                Background = FondoEncabezado,
                BorderBrush = Linea,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(6),
                Child = Texto(encabezados[c], 9, FontWeights.SemiBold, TintaSuave,
                    alineacion: c == 2 ? TextAlignment.Right : TextAlignment.Left)
            };
            Grid.SetRow(celda, 0);
            Grid.SetColumn(celda, c);
            tabla.Children.Add(celda);
        }

        for (var fila = 0; fila < reparaciones.Count; fila++)
        {
            var r = reparaciones[fila];
            string[] valores = [r.FechaTexto, r.Detalle, Moneda(r.Costo)];
            for (var c = 0; c < valores.Length; c++)
            {
                var celda = new Border
                {
                    BorderBrush = Linea,
                    BorderThickness = new Thickness(0, 0, 0, 0.5),
                    Padding = new Thickness(6, 5, 6, 5),
                    Child = Texto(valores[c], 9.5, FontWeights.Normal,
                        alineacion: c == 2 ? TextAlignment.Right : TextAlignment.Left,
                        envolver: c == 1)
                };
                Grid.SetRow(celda, fila + 1);
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
        var v = Texto(string.IsNullOrWhiteSpace(valor) ? "—" : valor, 11, FontWeights.SemiBold,
            alineacion: TextAlignment.Right);
        Grid.SetColumn(e, 0);
        Grid.SetColumn(v, 1);
        fila.Children.Add(e);
        fila.Children.Add(v);
        return fila;
    }

    private static TextBlock Etiqueta(string texto) =>
        Texto(texto, 8.5, FontWeights.SemiBold, TintaSuave);

    private static string Moneda(decimal valor) => $"RD$ {valor.ToString("N2", CulturaRd)}";

    private static Border Separador(double margenSuperior) => new()
    {
        Height = 1,
        Background = Linea,
        Margin = new Thickness(0, margenSuperior, 0, 0)
    };

    private static TextBlock Texto(string contenido, double tamano,
        FontWeight? peso = null, Brush? color = null,
        TextAlignment alineacion = TextAlignment.Left, Thickness? margen = null,
        bool envolver = false) =>
        new()
        {
            Text = contenido,
            FontFamily = Fuente,
            FontSize = tamano,
            FontWeight = peso ?? FontWeights.Normal,
            Foreground = color ?? Tinta,
            TextAlignment = alineacion,
            Margin = margen ?? new Thickness(0),
            TextWrapping = envolver ? TextWrapping.Wrap : TextWrapping.NoWrap
        };
}
