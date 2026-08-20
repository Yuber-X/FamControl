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
        Mostrar(mensaje, titulo, MessageBoxButton.YesNo, MessageBoxImage.Question)
            == MessageBoxResult.Yes;

    public void Informar(string titulo, string mensaje) =>
        Mostrar(mensaje, titulo, MessageBoxButton.OK, MessageBoxImage.Information);

    public void MostrarError(string titulo, string mensaje) =>
        Mostrar(mensaje, titulo, MessageBoxButton.OK, MessageBoxImage.Error);

    /// <summary>
    /// El MessageBox con dueño solo si hay uno VIVO. En el hueco entre que se
    /// cierra un shell y se abre el siguiente (cerrar sesión, cambiar usuario)
    /// la ventana principal ya está cerrada, y pasársela como dueño revienta
    /// con "no se puede establecer Owner en un Window que se ha cerrado".
    /// </summary>
    private static MessageBoxResult Mostrar(string mensaje, string titulo,
        MessageBoxButton botones, MessageBoxImage icono) =>
        Propietaria() is { } duena
            ? MessageBox.Show(duena, mensaje, titulo, botones, icono)
            : MessageBox.Show(mensaje, titulo, botones, icono);

    /// <summary>
    /// Ventanita de una sola caja de texto (portada del POS-500). La usa el
    /// motivo de anulación de una factura, que es obligatorio.
    /// </summary>
    public string? PedirTexto(string titulo, string mensaje, string textoInicial = "")
    {
        var ventana = new FAControl.Views.Pos.PedirTextoWindow(titulo, mensaje, textoInicial);
        if (Propietaria() is { } duena)
            ventana.Owner = duena;
        return ventana.ShowDialog() == true ? ventana.Resultado : null;
    }

    /// <summary>La ventana principal, o null si en este instante no hay una viva.</summary>
    private static Window? Propietaria() => FAControl.Views.VentanaDuena.Principal();
}
