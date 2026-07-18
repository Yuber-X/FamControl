using System.Windows;
using System.Windows.Media;
using FAControl.Common;
using FAControl.Services;
using FAControl.ViewModels;

namespace FAControl.App;

public partial class MainWindow : Window
{
    private readonly AuthService _auth;

    /// <summary>
    /// Qué hacer cuando este shell se cierre (lo lee App):
    ///   true  = volver al launcher (cerró sesión);
    ///   false = volver al login del mismo modo (cambio de usuario).
    /// </summary>
    public bool VolverAlLauncher { get; private set; } = true;

    public MainWindow(MainViewModel vm, AuthService auth)
    {
        InitializeComponent();
        DataContext = vm;
        _auth = auth;
    }

    /// <summary>Tamaño de texto Pequeño/Mediano/Grande (Configuración → Apariencia).</summary>
    public void AplicarEscala(double factor) =>
        Raiz.LayoutTransform = factor == 1.0 ? null : new ScaleTransform(factor, factor);

    /// <summary>Marca el shell con el modo activo: nombre y color en el sidebar.</summary>
    public void MostrarModo(IdentidadModo modo)
    {
        Title = $"{modo.Nombre} — Familia Almonte Auto Import SRL";
        NombreModo.Text = modo.Nombre;
        EtiquetaModo.Text = modo.Etiqueta;
        var color = (Color)ColorConverter.ConvertFromString(modo.ColorHex);
        var acento = new SolidColorBrush(color);
        MarcaModo.Background = acento;
        EtiquetaModo.Foreground = acento;

        // El ítem seleccionado del sidebar toma el acento del MODO (pedido de
        // Yuber 2026-07-18): dorado en Prest, verde en Auto, azul en DealControl.
        // DynamicResource resuelve estas claves en Window.Resources antes que en
        // App, así que sobreescribirlas acá repinta la selección en caliente.
        Resources["Brush.SidebarSel.Texto"] = new SolidColorBrush(color);
        Resources["Brush.SidebarSel.Fondo"] = new SolidColorBrush(color) { Opacity = 0.15 };
    }

    /// <summary>
    /// Cerrar sesión: termina la sesión y devuelve al LAUNCHER
    /// (pedido de Yuber 2026-07-17).
    /// </summary>
    private async void BotonSalir_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "¿Cerrar la sesión y volver a la pantalla de inicio?",
                "Cerrar sesión", MessageBoxButton.YesNo, MessageBoxImage.Question)
            != MessageBoxResult.Yes)
            return;

        await _auth.LogoutAsync();
        VolverAlLauncher = true;
        Close();
    }

    /// <summary>
    /// Cambiar usuario: cierra la sesión y vuelve al LOGIN del mismo modo,
    /// sin pasar por el launcher. Es el relevo de turno.
    /// </summary>
    private async void BotonCambiarUsuario_Click(object sender, RoutedEventArgs e)
    {
        await _auth.LogoutAsync();
        VolverAlLauncher = false;
        Close();
    }
}
