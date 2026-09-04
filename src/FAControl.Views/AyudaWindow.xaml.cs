using System.Diagnostics;
using System.Reflection;
using System.Windows;
using FAControl.Common;

namespace FAControl.Views;

/// <summary>
/// Ayuda y soporte (pedido del cliente 2026-07-29): el número del desarrollador
/// a un clic, desde el launcher y desde cualquier modo.
///
/// No tiene ViewModel a propósito: no hay estado ni negocio, solo datos
/// constantes de <see cref="Soporte"/> y dos acciones de UI (abrir WhatsApp,
/// copiar al portapapeles), que es justo lo que sí va en code-behind.
/// </summary>
public partial class AyudaWindow : Window
{
    public AyudaWindow()
    {
        InitializeComponent();
        VentanaAjustable.Ajustar(this);

        TextoDesarrollador.Text = $"{Soporte.Desarrollador} · desarrollador de FAControl";
        TextoTelefono.Text = Soporte.Telefono;
        ListaQueContar.ItemsSource = Soporte.QueContar;

        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "—";
        TextoVersion.Text = $"FAControl versión {version}. Los registros técnicos quedan en la " +
                            "carpeta logs, junto al programa: sirven mucho si hay que revisar a fondo.";
    }

    private void WhatsApp_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // UseShellExecute: sin esto, .NET no sabe abrir una URL
            Process.Start(new ProcessStartInfo(Soporte.UrlWhatsApp) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "No se pudo abrir WhatsApp desde la ventana de ayuda");
            MessageBox.Show(this,
                $"No se pudo abrir WhatsApp en este equipo.\n\nEscribe o llama al {Soporte.Telefono}.",
                "Ayuda", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void Copiar_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(Soporte.Telefono);
            BotonCopiar.Content = "¡Copiado!";
        }
        catch (Exception ex)
        {
            // El portapapeles lo puede tener tomado otra aplicación
            Serilog.Log.Warning(ex, "No se pudo copiar el teléfono de soporte al portapapeles");
            BotonCopiar.Content = "No se pudo copiar";
        }
    }

    private void Cerrar_Click(object sender, RoutedEventArgs e) => Close();
}
