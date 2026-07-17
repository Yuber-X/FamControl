using System.Collections.ObjectModel;
using System.Windows;

namespace FAControl.Views;

/// <summary>
/// Cambia la paleta en caliente (pedido del cliente 2026-07-16: "Temas Oscuro
/// cambiable... como el modo noche").
///
/// Funciona reemplazando SOLO el diccionario de colores dentro de los
/// MergedDictionaries de la aplicación. Tipografía y Controles no se tocan:
/// referencian los brushes con DynamicResource, así que se repintan solos.
///
/// Si algún estilo usara StaticResource para un brush, NO cambiaría al
/// alternar (se resuelve una vez al cargar). Por eso Tipografia.xaml pasó a
/// DynamicResource: si no, el texto quedaba negro sobre fondo negro.
/// </summary>
public static class Tema
{
    private const string RutaClaro = "pack://application:,,,/FAControl.Views;component/Themes/Colores.xaml";
    private const string RutaOscuro = "pack://application:,,,/FAControl.Views;component/Themes/ColoresOscuro.xaml";

    /// <summary>Aplica el tema a la aplicación en curso.</summary>
    public static void Aplicar(bool oscuro) =>
        Aplicar(Application.Current?.Resources.MergedDictionaries, oscuro);

    /// <summary>
    /// Sobrecarga con los diccionarios explícitos: la usa el arnés de
    /// verificación, que no levanta la App completa.
    /// </summary>
    public static void Aplicar(Collection<ResourceDictionary>? diccionarios, bool oscuro)
    {
        if (diccionarios is null)
            return;

        var nuevo = new ResourceDictionary { Source = new Uri(oscuro ? RutaOscuro : RutaClaro) };

        // Se busca por nombre de archivo: el índice no es fiable (el arnés y la
        // App mergean en orden distinto).
        for (var i = 0; i < diccionarios.Count; i++)
        {
            var fuente = diccionarios[i].Source?.OriginalString ?? string.Empty;
            if (fuente.EndsWith("Colores.xaml", StringComparison.OrdinalIgnoreCase) ||
                fuente.EndsWith("ColoresOscuro.xaml", StringComparison.OrdinalIgnoreCase))
            {
                diccionarios[i] = nuevo;
                return;
            }
        }

        // No estaba: se agrega al principio para que Tipografia y Controles
        // encuentren los brushes al resolverse.
        diccionarios.Insert(0, nuevo);
    }
}
