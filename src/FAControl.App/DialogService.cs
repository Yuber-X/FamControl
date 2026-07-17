using System.Windows;
using FAControl.Common;

namespace FAControl.App;

/// <summary>
/// Implementación WPF de IDialogService. Los ViewModels dependen solo de la
/// interfaz (testeable); el MessageBox vive únicamente aquí, en la capa de UI.
/// </summary>
public class DialogService : IDialogService
{
    public bool Confirmar(string titulo, string mensaje) =>
        MessageBox.Show(Propietaria(), mensaje, titulo,
            MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void Informar(string titulo, string mensaje) =>
        MessageBox.Show(Propietaria(), mensaje, titulo,
            MessageBoxButton.OK, MessageBoxImage.Information);

    public void MostrarError(string titulo, string mensaje) =>
        MessageBox.Show(Propietaria(), mensaje, titulo,
            MessageBoxButton.OK, MessageBoxImage.Error);

    private static Window Propietaria() => Application.Current.MainWindow;
}
