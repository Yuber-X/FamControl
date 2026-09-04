using System.Windows;

namespace FAControl.Views;

/// <summary>
/// Texto de marcador ("placeholder") para las cajas de texto: se ve en gris
/// mientras el campo está vacío y desaparece al escribir.
///
/// WPF no lo trae de fábrica, así que se resuelve con una propiedad adjunta que
/// el template de <c>Input.Texto</c> pinta detrás del contenido. Se hizo así, y
/// no con un TextBlock suelto en cada pantalla, porque el marcador tiene que
/// alinearse EXACTO con el texto real (mismo padding, misma fuente) y eso solo
/// lo garantiza el propio template.
///
/// Nació para el próximo comprobante fiscal (pedido del cliente 2026-09-03),
/// pero no sabe nada de NCF: es una pieza de interfaz reusable.
///
/// Con la cadena vacía no se dibuja nada, que es justo lo que se necesita
/// cuando la estancia todavía no configuró secuencia de comprobantes.
/// </summary>
public static class Marcador
{
    public static readonly DependencyProperty TextoProperty =
        DependencyProperty.RegisterAttached(
            "Texto", typeof(string), typeof(Marcador),
            new FrameworkPropertyMetadata(string.Empty));

    public static string GetTexto(DependencyObject elemento) =>
        (string)elemento.GetValue(TextoProperty);

    public static void SetTexto(DependencyObject elemento, string valor) =>
        elemento.SetValue(TextoProperty, valor);
}
