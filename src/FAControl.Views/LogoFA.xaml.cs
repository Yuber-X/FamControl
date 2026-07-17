using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FAControl.Views;

/// <summary>
/// Logo vectorial de Familia Almonte Auto Import SRL.
///
/// Los colores son DependencyProperty para que el mismo control sirva sobre
/// fondo oscuro (launcher: la F clara) y sobre papel blanco (facturas y
/// contratos: la F en navy), sin duplicar la geometría.
/// </summary>
public partial class LogoFA : UserControl
{
    /// <summary>Navy profundo de la marca.</summary>
    public static readonly Brush Navy = new SolidColorBrush(Color.FromRgb(0x0D, 0x1B, 0x2A));
    /// <summary>Dorado de la marca.</summary>
    public static readonly Brush Dorado = new SolidColorBrush(Color.FromRgb(0xC9, 0xA1, 0x5A));
    private static readonly Brush Claro = new SolidColorBrush(Color.FromRgb(0xF2, 0xF4, 0xF7));

    public static readonly DependencyProperty ColorFProperty = DependencyProperty.Register(
        nameof(ColorF), typeof(Brush), typeof(LogoFA), new PropertyMetadata(Claro));

    public static readonly DependencyProperty ColorAProperty = DependencyProperty.Register(
        nameof(ColorA), typeof(Brush), typeof(LogoFA), new PropertyMetadata(Dorado));

    public static readonly DependencyProperty ColorVentanaProperty = DependencyProperty.Register(
        nameof(ColorVentana), typeof(Brush), typeof(LogoFA), new PropertyMetadata(Navy));

    /// <summary>Color de la F. Claro sobre navy, navy sobre papel.</summary>
    public Brush ColorF
    {
        get => (Brush)GetValue(ColorFProperty);
        set => SetValue(ColorFProperty, value);
    }

    /// <summary>Color de la A / techo. Siempre dorado.</summary>
    public Brush ColorA
    {
        get => (Brush)GetValue(ColorAProperty);
        set => SetValue(ColorAProperty, value);
    }

    /// <summary>Color de los paños de la ventana: debe contrastar con el techo.</summary>
    public Brush ColorVentana
    {
        get => (Brush)GetValue(ColorVentanaProperty);
        set => SetValue(ColorVentanaProperty, value);
    }

    public LogoFA() => InitializeComponent();

    /// <summary>Variante para papel blanco (facturas, contratos): la F en navy.</summary>
    public static LogoFA ParaPapel() => new() { ColorF = Navy, ColorA = Dorado, ColorVentana = Brushes.White };
}
