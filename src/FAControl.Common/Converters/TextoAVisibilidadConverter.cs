using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FAControl.Common.Converters;

/// <summary>
/// Muestra el elemento solo si el texto tiene contenido; con cadena vacía o
/// nula lo colapsa.
///
/// POR QUÉ HACE FALTA: un TextBlock vacío igual ocupa su alto de línea y su
/// margen. Cuando el aviso aparece y desaparece, eso empuja lo que está
/// alrededor y la pantalla "salta" — que es justo lo que se pidió corregir el
/// 2026-08-01 con el botón de cobro. Colapsándolo, el espacio solo existe
/// cuando hay algo que decir.
/// </summary>
public class TextoAVisibilidadConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("TextoAVisibilidad es de una sola dirección.");
}
