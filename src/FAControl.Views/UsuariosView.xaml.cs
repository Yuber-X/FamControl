using System.Windows;
using System.Windows.Controls;
using FAControl.ViewModels;

namespace FAControl.Views;

public partial class UsuariosView : UserControl
{
    private UsuariosViewModel? _vm;

    public UsuariosView() => InitializeComponent();

    // Lógica de UI: PasswordBox.Password no es DependencyProperty (WPF lo
    // impide a propósito para no dejar la contraseña en el árbol de bindings),
    // así que el puente se hace a mano en las dos direcciones.
    private void CampoPassword_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_vm is not null)
            _vm.PasswordNueva = CampoPassword.Password;
    }

    private void UsuariosView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
            _vm.PasswordDebeLimpiarse -= LimpiarPassword;

        _vm = e.NewValue as UsuariosViewModel;
        if (_vm is not null)
            _vm.PasswordDebeLimpiarse += LimpiarPassword;

        LimpiarPassword();
    }

    private void LimpiarPassword() => CampoPassword.Clear();
}
