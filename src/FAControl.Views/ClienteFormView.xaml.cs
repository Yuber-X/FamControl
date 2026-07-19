using System.Linq;
using System.Windows.Controls;

namespace FAControl.Views;

public partial class ClienteFormView : UserControl
{
    public ClienteFormView() => InitializeComponent();

    // Auto-formato estético al escribir (pedido de Yuber 2026-07-18): los guiones
    // se insertan solos, como los usan las empresas dominicanas.
    //   Cédula:   000-0000000-0  (3-7-1)
    //   Teléfono: 000-000-0000   (3-3-4)
    // La cédula NO se toca si el usuario escribe letras (pasaporte de extranjero).

    private void CampoCedula_TextChanged(object sender, TextChangedEventArgs e)
    {
        var caja = (TextBox)sender;
        if (caja.Text.Any(char.IsLetter))   // pasaporte: se deja tal cual
            return;
        AplicarMascara(caja, CampoCedula_TextChanged, FormatearCedula);
    }

    private void CampoTelefono_TextChanged(object sender, TextChangedEventArgs e) =>
        AplicarMascara((TextBox)sender, CampoTelefono_TextChanged, FormatearTelefono);

    private static void AplicarMascara(TextBox caja, TextChangedEventHandler handler,
        System.Func<string, string> formato)
    {
        var digitos = new string(caja.Text.Where(char.IsDigit).ToArray());
        var formateado = formato(digitos);
        if (caja.Text == formateado)
            return;
        // Se desengancha el handler para no re-entrar al setear Text
        caja.TextChanged -= handler;
        caja.Text = formateado;
        caja.CaretIndex = formateado.Length;   // el cursor queda al final
        caja.TextChanged += handler;
    }

    private static string FormatearCedula(string d)
    {
        if (d.Length > 11) d = d[..11];
        return d.Length switch
        {
            <= 3 => d,
            <= 10 => $"{d[..3]}-{d[3..]}",
            _ => $"{d[..3]}-{d[3..10]}-{d[10..]}"
        };
    }

    private static string FormatearTelefono(string d)
    {
        if (d.Length > 10) d = d[..10];
        return d.Length switch
        {
            <= 3 => d,
            <= 6 => $"{d[..3]}-{d[3..]}",
            _ => $"{d[..3]}-{d[3..6]}-{d[6..]}"
        };
    }
}
