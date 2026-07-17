namespace FAControl.Models;

/// <summary>Una fila de la tabla de amortización tal como se imprime.</summary>
public record CuotaImpresa(
    int Numero,
    string FechaTexto,
    decimal Capital,
    decimal Interes,
    decimal MontoTotal,
    decimal SaldoDespues,
    string EstadoTexto);

/// <summary>
/// Préstamo listo para imprimir en hoja carta (pedido del cliente 2026-07-16:
/// "Prestamo > Imprimir"). Es un DTO plano: la capa Printing no conoce
/// ViewModels ni entidades de BD.
/// </summary>
public record PrestamoImpreso(
    string Codigo,
    string ClienteNombre,
    string ClienteCedula,
    decimal MontoCapital,
    string TasaTexto,
    string ModalidadTexto,
    string MetodoTexto,
    string FechaPrimerPagoTexto,
    string GarantiaTexto,
    decimal TotalAPagar,
    decimal TotalPagado,
    decimal SaldoPendiente,
    string EstadoTexto,
    string ProgresoTexto,
    string EmitidoPor,
    IReadOnlyList<CuotaImpresa> Cuotas);
