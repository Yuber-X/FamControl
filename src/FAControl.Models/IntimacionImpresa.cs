namespace FAControl.Models;

/// <summary>Cuota vencida en la intimación de pago.</summary>
public record IntimacionCuota(int Numero, string FechaVencimiento, decimal MontoPendiente);

/// <summary>
/// Intimación de pago (cliente 2026-07-19): requerimiento formal PREVIO a lo
/// judicial que emite el acreedor. NO es el "mandamiento de pago" (ese lo
/// notifica un alguacil). Ver docs/INTIMACION-Y-MANDAMIENTO.md.
///
/// DTO plano: la capa Printing no conoce ViewModels.
/// </summary>
public record IntimacionImpresa(
    // Acreedor
    string NombreNegocio,
    string Prestamista,
    string Ciudad,
    string Telefono,
    string Rnc,
    // Deudor
    string DeudorNombre,
    string DeudorCedula,
    // Deuda
    string CodigoPrestamo,
    decimal MontoOriginal,
    decimal SaldoPendiente,
    IReadOnlyList<IntimacionCuota> CuotasVencidas,
    int PlazoDias);
