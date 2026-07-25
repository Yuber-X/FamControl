using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace FAControl.Printing;

/// <summary>
/// Monograma FA vectorial de la marca (la A forma el techo de una casa) para
/// documentos impresos. Compartido por el pagaré, el recibo y las facturas:
/// una sola geometría, nítida a cualquier tamaño (no es una imagen pegada).
/// </summary>
public static class LogoFa
{
    private static readonly Color Navy = Color.FromRgb(0x1B, 0x26, 0x3B);
    private static readonly Color Oro = Color.FromRgb(0xC9, 0xA1, 0x5A);
    private static readonly Color Crema = Color.FromRgb(0xF3, 0xEE, 0xE2);

    /// <summary>Badge navy redondeado con el monograma FA (F crema, A dorada, ventana).</summary>
    public static Border Badge(double lado)
    {
        var canvas = new Canvas { Width = 140, Height = 120 };
        canvas.Children.Add(new Path { Fill = Congelar(Crema),
            Data = Geometry.Parse("M 10,8 L 62,8 L 62,24 L 28,24 L 28,52 L 56,52 L 56,68 L 28,68 L 28,112 L 10,112 Z") });
        canvas.Children.Add(new Path { Fill = Congelar(Oro),
            Data = Geometry.Parse("M 88,8 L 138,112 L 116,112 L 88,50 L 60,112 L 38,112 Z") });
        // Ventana de 4 paños (el detalle que convierte la A en casa)
        canvas.Children.Add(RectVentana(76, 72, 24, 24, Oro));
        canvas.Children.Add(RectVentana(86.5, 72, 3, 24, Navy));
        canvas.Children.Add(RectVentana(76, 82.5, 24, 3, Navy));

        var viewbox = new Viewbox
        {
            Stretch = Stretch.Uniform,
            Width = lado * 2 / 3,
            Height = lado * 2 / 3,
            Child = canvas
        };
        return new Border
        {
            Background = Congelar(Navy),
            CornerRadius = new CornerRadius(lado / 5),
            Width = lado,
            Height = lado,
            Child = viewbox
        };
    }

    private static Rectangle RectVentana(double left, double top, double w, double h, Color color)
    {
        var r = new Rectangle { Width = w, Height = h, Fill = Congelar(color) };
        Canvas.SetLeft(r, left);
        Canvas.SetTop(r, top);
        return r;
    }

    private static Brush Congelar(Color color)
    {
        var b = new SolidColorBrush(color);
        b.Freeze();
        return b;
    }
}
