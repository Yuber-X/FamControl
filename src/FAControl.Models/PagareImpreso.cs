namespace FAControl.Models;

/// <summary>Una fila de la tabla de cuotas del pagaré.</summary>
public record PagareCuota(int Numero, string FechaTexto, decimal Cuota);

/// <summary>
/// Pagaré / contrato a firmar (cliente 2026-07-17: "imprimir un contrato a
/// firmar para el cliente"). Estructura tomada del PDF real del cliente:
/// encabezado del negocio, deuda, tabla de cuotas, cláusula de incumplimiento,
/// consentimiento a Púrpura Datos y dos firmas.
///
/// Es un DTO plano: la capa Printing no conoce ViewModels ni entidades de BD.
/// </summary>
public record PagareImpreso(
    // Encabezado del negocio (acreedor)
    string NombreNegocio,
    string Prestamista,
    string Ciudad,
    string Telefono,
    string Email,
    string Rnc,
    // Deudor
    string DeudorNombre,
    string DeudorCedula,
    // Deuda
    string CodigoPrestamo,
    decimal MontoPrestado,
    /// <summary>Tasa ya formateada para el texto, ej. "10% mensual" (pedido 2026-07-25).</summary>
    string TasaTexto,
    decimal TotalAPagar,
    IReadOnlyList<PagareCuota> Cuotas)
{
    /// <summary>
    /// La tasa guardada es mensual para todas las modalidades, EXCEPTO pago
    /// único donde se aplica una sola vez (no por período).
    /// </summary>
    public static string FormatearTasa(decimal tasaMensual, Modalidad modalidad) =>
        modalidad == Modalidad.PagoUnico
            ? $"{tasaMensual:0.##}% (pago único)"
            : $"{tasaMensual:0.##}% mensual";
}
