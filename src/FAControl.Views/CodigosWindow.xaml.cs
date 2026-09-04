using System.Windows;
using System.Windows.Input;
using FAControl.ViewModels;
using Microsoft.Win32;

namespace FAControl.Views;

/// <summary>
/// Ventana de los códigos del producto, que se abre desde el launcher (cliente
/// 2026-07-27, ampliada el 2026-07-29 a siete códigos con activación por modo).
///
/// El code-behind solo hace cosas de UI: abrir el selector de carpeta y atajar
/// el Enter. La lógica vive en CodigosViewModel.
/// </summary>
public partial class CodigosWindow : Window
{
    private readonly CodigosViewModel _vm;

    public CodigosWindow(CodigosViewModel vm)
    {
        InitializeComponent();
        VentanaAjustable.Ajustar(this);
        _vm = vm;
        DataContext = vm;
        vm.CerrarSolicitado += Close;
    }

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
