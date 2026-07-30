namespace FAControl.Common;

/// <summary>
/// Diálogos de la aplicación, inyectable para poder testear ViewModels
/// sin UI real (regla del proyecto: nunca MessageBox.Show directo en lógica).
/// </summary>
public interface IDialogService
{
    /// <summary>Pregunta Sí/No. True si el usuario confirma.</summary>
    bool Confirmar(string titulo, string mensaje);

    void Informar(string titulo, string mensaje);

    void MostrarError(string titulo, string mensaje);

    /// <summary>
    /// Pide un texto corto al usuario. Devuelve null si canceló.
    /// Viene del POS-500 (2026-07-30): lo usa el motivo de anulación de una
    /// factura, que es obligatorio y queda en el historial.
    /// </summary>
    string? PedirTexto(string titulo, string mensaje, string textoInicial = "");
}
