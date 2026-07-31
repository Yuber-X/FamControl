using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FAControl.Common.Converters;

/// <summary>
/// Visibilidad INVERSA: visible cuando el valor es false.
///
/// Existe porque los ViewModels del proyecto exponen banderas en negativo
/// ("SinDocumentos", "SinMovimientos") para mostrar el aviso de vacío, y el
/// contenido real necesita justo lo contrario. Encadenar InversorBool con
/// BoolAVisibilidad no se puede en XAML sin un converter compuesto.
/// </summary>
public class BooleanToVisibilityInversoConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool bandera && bandera ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility visibilidad && visibilidad != Visibility.Visible;
}
