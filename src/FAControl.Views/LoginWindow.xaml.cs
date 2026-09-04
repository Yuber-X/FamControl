using System.Windows;
using System.Windows.Input;
using FAControl.ViewModels;

namespace FAControl.Views;

/// <summary>
/// Code-behind con SOLO lógica de UI: pasar el Password al ViewModel
/// (WPF no permite binding de PasswordBox) y ajustar textos del modo wizard.
/// </summary>
public partial class LoginWindow : Window
{
    private readonly LoginViewModel _vm;

    public LoginWindow(LoginViewModel vm)
    {
        InitializeComponent();
        VentanaAjustable.Ajustar(this);
        _vm = vm;
        DataContext = vm;
        Loaded += async (_, _) =>
        {
            // Defensa: una excepción sin capturar en un handler async void
            // tumba la app entera (pasaba si la BD desaparecía tras el arranque)
            try
            {
                await _vm.InicializarAsync();
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "No se pudo verificar la cuenta inicial al cargar el login");
                _vm.MensajeError = "No se pudo conectar con la base de datos.";
            }
            if (_vm.EsWizardInicial)
            {
                TituloContexto.Text = "Crea tu cuenta para empezar";
                BotonAccion.Content = "Crear cuenta";
            }
            CajaUsuario.Focus();
        };
    }

    private async void BotonAccion_Click(object sender, RoutedEventArgs e) => await EjecutarAccionAsync();

    private async void CajaPassword_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !_vm.EsWizardInicial)
            await EjecutarAccionAsync();
    }

    private async Task EjecutarAccionAsync()
    {
        if (_vm.EsWizardInicial)
            await _vm.CrearCuentaInicialAsync(CajaPassword.Password, CajaConfirmacion.Password);
        else
            await _vm.IntentarLoginAsync(CajaPassword.Password);
    }

    /// <summary>
    /// Muestra a qué modo se está entrando (el elegido en el launcher).
    /// El acento de color evita que el usuario crea que entró al modo equivocado.
    /// </summary>
    public void MostrarModo(FAControl.Common.IdentidadModo modo)
    {
        Title = $"{modo.Nombre} — Iniciar sesión";
        NombreModo.Text = modo.Nombre;
        DescripcionModo.Text = modo.Descripcion;
        Logo.ColorA = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(modo.ColorHex));
        Logo.ColorF = (System.Windows.Media.Brush)FindResource("Brush.TextoPrincipal");
        Logo.ColorVentana = (System.Windows.Media.Brush)FindResource("Brush.FondoCard");
    }
}
