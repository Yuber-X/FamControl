using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using FAControl.Common;
using FAControl.ViewModels;

namespace FAControl.Views;

/// <summary>
/// Configuración. El code-behind solo hace lógica de UI: diálogos de archivos,
/// lectura de PasswordBox (no bindeable por seguridad) y estado de los radios.
/// </summary>
public partial class ConfiguracionView : UserControl
{
    public ConfiguracionView() => InitializeComponent();

    private ConfiguracionViewModel? Vm => DataContext as ConfiguracionViewModel;

    // Contraseña de app de Gmail: PasswordBox no se bindea (seguridad), se
    // empuja al VM que la cifra con DPAPI antes de guardarla.
    private void CampoGmailPassword_PasswordChanged(object sender, RoutedEventArgs e) =>
        Vm?.GuardarPasswordCorreo(CampoGmailPassword.Password);

    // ---------- Tamaño de texto ----------

    private void RadiosTamano_Loaded(object sender, RoutedEventArgs e)
    {
        if (Vm is null)
            return;
        var radio = Vm.TamanoSeleccionado.Valor switch
        {
            TamanoTexto.Mediano => RadioMediano,
            TamanoTexto.Grande => RadioGrande,
            _ => RadioPequeno
        };
        radio.IsChecked = true;
    }

    private void TamanoPequeno_Checked(object sender, RoutedEventArgs e) => Seleccionar(TamanoTexto.Pequeno);
    private void TamanoMediano_Checked(object sender, RoutedEventArgs e) => Seleccionar(TamanoTexto.Mediano);
    private void TamanoGrande_Checked(object sender, RoutedEventArgs e) => Seleccionar(TamanoTexto.Grande);

    private void Seleccionar(TamanoTexto tamano)
    {
        if (Vm is { } vm && vm.TamanoSeleccionado.Valor != tamano)
            vm.TamanoSeleccionado = vm.Tamanos.First(t => t.Valor == tamano);
    }

    // ---------- Contraseña ----------

    private async void BotonCambiarPassword_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null)
            return;
        await Vm.CambiarPasswordAsync(CajaActual.Password, CajaNueva.Password, CajaConfirmacion.Password);
        if (Vm.PasswordCambiada)
        {
            CajaActual.Clear();
            CajaNueva.Clear();
            CajaConfirmacion.Clear();
        }
    }

    // ---------- Respaldo / restauración ----------

    private async void BotonRespaldar_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null)
            return;
        var dialogo = new SaveFileDialog
        {
            Title = "Guardar respaldo de la base de datos",
            FileName = $"FAControl_Respaldo_{FechaNegocio.Hoy:yyyy-MM-dd}.sql",
            Filter = "Respaldo SQL (*.sql)|*.sql"
        };
        if (dialogo.ShowDialog(Window.GetWindow(this)) == true)
            await Vm.RespaldarAsync(dialogo.FileName);
    }

    private async void BotonRestaurar_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null)
            return;
        var dialogo = new OpenFileDialog
        {
            Title = "Elegir archivo de respaldo",
            Filter = "Respaldo SQL (*.sql)|*.sql"
        };
        if (dialogo.ShowDialog(Window.GetWindow(this)) == true)
            await Vm.RestaurarAsync(dialogo.FileName);
    }

    // ---------- Exportación ----------

    private async void BotonExportar_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null)
            return;
        var dialogo = new SaveFileDialog
        {
            Title = "Exportar datos a Excel",
            FileName = $"FAControl_Export_{FechaNegocio.Hoy:yyyy-MM-dd}.xlsx",
            Filter = "Libro de Excel (*.xlsx)|*.xlsx"
        };
        if (dialogo.ShowDialog(Window.GetWindow(this)) == true)
            await Vm.ExportarAhoraAsync(dialogo.FileName);
    }

    private void BotonElegirCarpeta_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null)
            return;
        var dialogo = new OpenFolderDialog { Title = "Carpeta para las exportaciones automáticas" };
        if (dialogo.ShowDialog(Window.GetWindow(this)) == true)
            Vm.ExportCarpeta = dialogo.FolderName;
    }

    private void BotonElegirCarpetaRespaldo_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null)
            return;
        var dialogo = new OpenFolderDialog
        {
            Title = "Carpeta para los respaldos automáticos (podés elegir la de OneDrive/Google Drive)"
        };
        if (dialogo.ShowDialog(Window.GetWindow(this)) == true)
            Vm.RespaldoCarpeta = dialogo.FolderName;
    }
}
