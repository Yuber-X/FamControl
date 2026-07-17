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
    decimal TotalAPagar,
    IReadOnlyList<PagareCuota> Cuotas);
