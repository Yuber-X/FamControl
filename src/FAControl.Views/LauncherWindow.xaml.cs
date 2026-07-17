using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FAControl.Common;

namespace FAControl.Views;

/// <summary>
/// Adapta IdentidadModo a lo que necesita el XAML (Brushes y Colors).
/// Vive en Views porque es mapeo de PRESENTACIÓN: Common no conoce WPF.
/// </summary>
public record ModoTarjeta(IdentidadModo Identidad)
{
    public ModoApp Modo => Identidad.Modo;
    public string Nombre => Identidad.Nombre;
    public string Etiqueta => Identidad.Etiqueta;
    public string Descripcion => Identidad.Descripcion;
    public bool Disponible => Identidad.Disponible;

    public Brush Acento => new SolidColorBrush(Convertir(Identidad.ColorHex));

    /// <summary>El acento al 18%: fondo del ícono y de la píldora de estado.</summary>
    public Brush AcentoSutil => new SolidColorBrush(Convertir(Identidad.ColorHex)) { Opacity = 0.18 };

    /// <summary>Color del glow al pasar el mouse: el propio, más oscuro.</summary>
    public Color ColorGlow => Convertir(Identidad.ColorBrilloHex);

    public string EstadoTexto => Identidad.Disponible ? "DISPONIBLE" : "EN DESARROLLO";

    public string Icono => Identidad.Modo switch
    {
        ModoApp.PrestControl => "",    // billete
        ModoApp.DealerControl => "",   // vehículo
        ModoApp.AutoControl => "",     // etiqueta de precio
        _ => ""
    };

    private static Color Convertir(string hex) => (Color)ColorConverter.ConvertFromString(hex);
}

/// <summary>
/// Puerta de entrada de la suite (pedido del cliente 2026-07-16): tres columnas,
/// una por modo. Al elegir una se abre el login de ESE modo.
///
/// No sigue el tema claro/oscuro a propósito: es la cara de la marca Familia
/// Almonte y siempre va en navy.
/// </summary>
public partial class LauncherWindow : Window
{
    /// <summary>El modo elegido. Null si el usuario cerró sin elegir.</summary>
    public ModoApp? ModoElegido { get; private set; }

    public LauncherWindow()
    {
        InitializeComponent();
        ListaModos.ItemsSource = IdentidadModo.Todos.Select(m => new ModoTarjeta(m)).ToList();
        ContenedorLogo.Content = new LogoFA { Width = 92, Height = 92 };
    }

    private void Modo_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not ModoTarjeta tarjeta)
            return;

        if (!tarjeta.Disponible)
        {
            // Honesto en vez de un login que no lleva a ningún lado
            MessageBox.Show(this,
                $"{tarjeta.Nombre} todavía está en desarrollo.\n\n{tarjeta.Descripcion}",
                "Módulo en desarrollo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ModoElegido = tarjeta.Modo;
        DialogResult = true;
    }

    private void BotonSalir_Click(object sender, RoutedEventArgs e)
    {
        // Cerrar el launcher cierra la app entera (pedido de Yuber 2026-07-17)
        ModoElegido = null;
        DialogResult = false;
    }
}
