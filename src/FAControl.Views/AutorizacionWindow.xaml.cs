using System.Windows;
using System.Windows.Input;

namespace FAControl.Views;

/// <summary>
/// Pide las credenciales de un administrador para aprobar una operación
/// (pedido del cliente 2026-07-16: el cobrador crea el préstamo y "muestra el
/// login que permitira progresar si el admin coloca su contraseña").
///
/// NO abre sesión: el cobrador sigue siendo el usuario activo. La validación
/// la hace el delegado que inyecta la capa App — esta ventana no conoce
/// servicios, solo recoge texto y muestra el error.
/// </summary>
public partial class AutorizacionWindow : Window
{
    /// <summary>Devuelve el mensaje de error, o null si la autorización fue válida.</summary>
    private readonly Func<string, string, Task<string?>> _validar;

    public AutorizacionWindow(string motivo, Func<string, string, Task<string?>> validar)
    {
        InitializeComponent();
        VentanaAjustable.Ajustar(this);
        ChromeVentana.OcultarBotones(this);
        _validar = validar;
        TextoMotivo.Text = motivo;
        Loaded += (_, _) => CajaUsuario.Focus();
    }

    private async void BotonAutorizar_Click(object sender, RoutedEventArgs e) => await IntentarAsync();

    private async void Campo_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            await IntentarAsync();
    }

    private async Task IntentarAsync()
    {
        TextoError.Text = string.Empty;
        BotonAutorizar.IsEnabled = false;
        try
        {
            var error = await _validar(CajaUsuario.Text, CajaPassword.Password);
            if (error is null)
            {
                DialogResult = true;
                return;
            }

            // Reintento sin cerrar: teclear mal una contraseña no debería
            // costarle al cobrador rehacer el formulario del préstamo.
            TextoError.Text = error;
            CajaPassword.Clear();
            CajaPassword.Focus();
        }
        finally
        {
            BotonAutorizar.IsEnabled = true;
        }
    }

    private void BotonCancelar_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
