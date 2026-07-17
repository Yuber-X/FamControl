using System.Windows;
using System.Windows.Media;
using FAControl.Services;
using FAControl.ViewModels;

namespace FAControl.App;

public partial class MainWindow : Window
{
    private readonly AuthService _auth;

    public MainWindow(MainViewModel vm, AuthService auth)
    {
        InitializeComponent();
        DataContext = vm;
        _auth = auth;
    }

    /// <summary>Tamaño de texto Pequeño/Mediano/Grande (Configuración → Apariencia).</summary>
    public void AplicarEscala(double factor) =>
        Raiz.LayoutTransform = factor == 1.0 ? null : new ScaleTransform(factor, factor);

    private async void BotonSalir_Click(object sender, RoutedEventArgs e)
    {
        await _auth.LogoutAsync();
        Application.Current.Shutdown();
    }
}
