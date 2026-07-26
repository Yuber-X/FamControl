using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FAControl.Models;

namespace FAControl.Printing;

/// <summary>
/// Factura de venta al contado en HOJA CARTA (pedido 2026-07-25): marca del
/// negocio + cliente + vehículo + precio + firmas (cliente/vendedor/gerencia),
/// siguiendo el documento real del expediente del dealer.
/// El mismo visual va a pantalla y a la impresora.
/// </summary>
public static class FacturaVentaVisualFactory
{
    public const double AnchoCarta = 816;

    private const double MargenHoja = 48;
    private static readonly CultureInfo CulturaRd = CultureInfo.GetCultureInfo("es-DO");
    private static readonly FontFamily Fuente = new("Segoe UI");
    private static readonly Brush Tinta = new SolidColorBrush(Color.FromRgb(0x0D, 0x1B, 0x2A));
    private static readonly Brush TintaSuave = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80));
    private static readonly Brush Linea = new SolidColorBrush(Color.FromRgb(0xE6, 0xE7, 0xEB));
    private static readonly Brush FondoSuave = new SolidColorBrush(Color.FromRgb(0xF4, 0xF5, 0xF7));

    public static FrameworkElement Crear(FacturaVentaImpresa f)
    {
        var raiz = new StackPanel { Width = AnchoCarta, Background = Brushes.White };
        var contenido = new StackPanel { Margin = new Thickness(MargenHoja, 40, MargenHoja, 40) };
        raiz.Children.Add(contenido);

        // --- Marca del negocio ---
        var marca = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        marca.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        marca.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        marca.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var badge = LogoFa.Badge(52);
        Grid.SetColumn(badge, 0);
        marca.Children.Add(badge);

        var textos = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
        textos.Children.Add(Texto(f.NegocioNombre, 16, FontWeights.Bold));
        var contacto = new[]
        {
            string.IsNullOrWhiteSpace(f.NegocioRnc) ? null : $"RNC {f.NegocioRnc}",
            string.IsNullOrWhiteSpace(f.NegocioTelefono) ? null : $"Tel. {f.NegocioTelefono}",
            string.IsNullOrWhiteSpace(f.NegocioCiudad) ? null : f.NegocioCiudad
        }.Where(s => s is not null);
        var lineaContacto = string.Join("  ·  ", contacto);
        if (lineaContacto.Length > 0)
            textos.Children.Add(Texto(lineaContacto, 10, FontWeights.Normal, TintaSuave));
        Grid.SetColumn(textos, 1);
        marca.Children.Add(textos);

        var numero = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        numero.Children.Add(Texto("FACTURA", 14, FontWeights.Bold, alineacion: TextAlignment.Right));
        numero.Children.Add(Texto($"No. {f.Codigo}", 11, FontWeights.SemiBold, TintaSuave, TextAlignment.Right));
        numero.Children.Add(Texto(f.FechaTexto, 10, FontWeights.Normal, TintaSuave, TextAlignment.Right));
        Grid.SetColumn(numero, 2);
        marca.Children.Add(numero);
        contenido.Children.Add(marca);
        contenido.Children.Add(Separador(0));

        // --- Cliente ---
        contenido.Children.Add(Texto("DATOS DEL CLIENTE", 8.5, FontWeights.SemiBold, TintaSuave,
            margen: new Thickness(0, 14, 0, 4)));
        var cliente = new Grid();
        cliente.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cliente.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        cliente.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var clienteIzq = new StackPanel();
        clienteIzq.Children.Add(FilaDato("Nombre", f.ClienteNombre));
        clienteIzq.Children.Add(FilaDato("Cédula / RNC", f.ClienteCedula));
        Grid.SetColumn(clienteIzq, 0);
        cliente.Children.Add(clienteIzq);
        var clienteDer = new StackPanel();
        clienteDer.Children.Add(FilaDato("Teléfono", f.ClienteTelefono));
        clienteDer.Children.Add(FilaDato("Dirección", f.ClienteDireccion));
        Grid.SetColumn(clienteDer, 2);
        cliente.Children.Add(clienteDer);
        contenido.Children.Add(cliente);

        // --- Vehículo ---
        contenido.Children.Add(Texto("VEHÍCULO", 8.5, FontWeights.SemiBold, TintaSuave,
            margen: new Thickness(0, 14, 0, 4)));
        var vehiculo = new Grid();
        vehiculo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        vehiculo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        vehiculo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var vehIzq = new StackPanel();
        vehIzq.Children.Add(FilaDato("Descripción", f.VehiculoDescripcion));
        vehIzq.Children.Add(FilaDato("Chasis (VIN)", f.Vin));
        vehIzq.Children.Add(FilaDato("Placa", f.Placa));
        Grid.SetColumn(vehIzq, 0);
        vehiculo.Children.Add(vehIzq);
        var vehDer = new StackPanel();
        vehDer.Children.Add(FilaDato("Año", f.AnioTexto));
        vehDer.Children.Add(FilaDato("Color", f.Color));
        vehDer.Children.Add(FilaDato("Matrícula", f.Matricula));
        Grid.SetColumn(vehDer, 2);
        vehiculo.Children.Add(vehDer);
        contenido.Children.Add(vehiculo);

        // --- Total ---
        var total = new Border
        {
            Background = FondoSuave,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 12, 16, 12),
            Margin = new Thickness(0, 18, 0, 0)
        };
        var totalGrid = new Grid();
        totalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        totalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var metodo = Texto($"Forma de pago: {f.MetodoTexto}", 11.5, FontWeights.Normal, TintaSuave);
        metodo.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(metodo, 0);
        totalGrid.Children.Add(metodo);
        var precio = Texto($"TOTAL  RD$ {f.Precio.ToString("N2", CulturaRd)}", 18, FontWeights.Bold,
            alineacion: TextAlignment.Right);
        Grid.SetColumn(precio, 1);
        totalGrid.Children.Add(precio);
        total.Child = totalGrid;
        contenido.Children.Add(total);

        // --- Notas / condiciones (ej. garantía) ---
        if (!string.IsNullOrWhiteSpace(f.Notas))
        {
            contenido.Children.Add(Texto("CONDICIONES / NOTAS", 8.5, FontWeights.SemiBold, TintaSuave,
                margen: new Thickness(0, 14, 0, 4)));
            contenido.Children.Add(Texto(f.Notas!, 10.5, envolver: true));
        }

        contenido.Children.Add(Texto("Al firmar usted acepta todas las condiciones de la venta.",
            10, FontWeights.Normal, TintaSuave, margen: new Thickness(0, 22, 0, 34)));

        // --- Firmas: cliente / vendedor / gerencia (documento real del dealer) ---
        var firmas = new Grid();
        for (var i = 0; i < 5; i++)
            firmas.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = i % 2 == 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(36)
            });
        AgregarFirma(firmas, 0, "CLIENTE", f.ClienteNombre);
        AgregarFirma(firmas, 2, "VENDEDOR", f.VendedorNombre);
        AgregarFirma(firmas, 4, "GERENCIA", string.Empty);
        contenido.Children.Add(firmas);

        raiz.Measure(new Size(AnchoCarta, double.PositiveInfinity));
        raiz.Arrange(new Rect(0, 0, AnchoCarta, raiz.DesiredSize.Height));
        raiz.UpdateLayout();
        return raiz;
    }

    private static void AgregarFirma(Grid destino, int columna, string rol, string nombre)
    {
        var panel = new StackPanel();
        panel.Children.Add(new Border { Height = 1, Background = Tinta, Margin = new Thickness(0, 26, 0, 4) });
        var etiqueta = Texto(rol, 9, FontWeights.SemiBold, TintaSuave, TextAlignment.Center);
        panel.Children.Add(etiqueta);
        if (!string.IsNullOrWhiteSpace(nombre))
            panel.Children.Add(Texto(nombre, 10, FontWeights.Normal, alineacion: TextAlignment.Center));
        Grid.SetColumn(panel, columna);
        destino.Children.Add(panel);
    }

    private static UIElement FilaDato(string etiqueta, string? valor)
    {
        var fila = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        fila.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        fila.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var e = Texto(etiqueta, 11, FontWeights.Normal, TintaSuave);
        var v = Texto(string.IsNullOrWhiteSpace(valor) ? "—" : valor!, 11, FontWeights.SemiBold,
            alineacion: TextAlignment.Right);
        Grid.SetColumn(e, 0);
        Grid.SetColumn(v, 1);
        fila.Children.Add(e);
        fila.Children.Add(v);
        return fila;
    }

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
