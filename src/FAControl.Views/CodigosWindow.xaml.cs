using System.Windows;
using System.Windows.Input;
using FAControl.ViewModels;
using Microsoft.Win32;

namespace FAControl.Views;

/// <summary>
/// Ventana de los cuatro códigos, que se abre desde el launcher (pedido del
/// cliente 2026-07-27).
///
/// El code-behind solo hace cosas de UI: pasar la contraseña del PasswordBox al
/// ViewModel (WPF no permite bindear Password por seguridad) y abrir el
/// selector de carpeta. La lógica vive en CodigosViewModel.
/// </summary>
public partial class CodigosWindow : Window
{
    private readonly CodigosViewModel _vm;

    public CodigosWindow(CodigosViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        vm.PasswordDebeLimpiarse += () => CajaPassword.Password = string.Empty;
        vm.CerrarSolicitado += Close;
    }

    private void Password_Changed(object sender, RoutedEventArgs e) =>
        _vm.PasswordNueva = CajaPassword.Password;

    private void Codigo_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _vm.ValidarCommand.CanExecute(null))
            _vm.ValidarCommand.Execute(null);
    }

    private void ElegirCarpeta_Click(object sender, RoutedEventArgs e)
    {
        var dialogo = new OpenFolderDialog { Title = "¿Dónde guardo el respaldo?" };
        if (dialogo.ShowDialog(this) == true)
            _vm.CarpetaRespaldo = dialogo.FolderName;
    }

    private void Cerrar_Click(object sender, RoutedEventArgs e) => Close();
}
