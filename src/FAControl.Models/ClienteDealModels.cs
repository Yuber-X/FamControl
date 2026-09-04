namespace FAControl.Models;

/// <summary>
/// Métricas de un cliente DEL DEALER (pedido 2026-07-27). Reemplazan a las de
/// PrestControl (Total prestado / Préstamos activos), que no significan nada
/// en un dealer: aquí el cliente COMPRA o ALQUILA vehículos, no pide crédito.
/// </summary>
/// <param name="TotalTransferido">Lo que el cliente negoció: precio de sus compras + sus alquileres.</param>
/// <param name="TotalCobrado">Lo efectivamente recibido: contado completo, inicial + plazos pagados, alquileres finalizados.</param>
/// <param name="SaldoPendiente">Lo que todavía debe (transferido − cobrado). Nunca negativo.</param>
/// <param name="VehiculosComprados">Cantidad de vehículos que compró.</param>
/// <param name="VehiculosAlquilados">Cantidad de alquileres vigentes o cumplidos.</param>
/// <param name="PlazosAtrasados">Plazos vencidos sin saldar (0 = está al día).</param>
public record MetricasClienteDeal(
    decimal TotalTransferido,
    decimal TotalCobrado,
    decimal SaldoPendiente,
    int VehiculosComprados,
    int VehiculosAlquilados,
    int PlazosAtrasados);

/// <summary>Vehículo que el cliente compró o alquiló (fila de su ficha en DealControl).</summary>
/// <param name="VehiculoId">Para abrir la ficha completa del vehículo desde la ficha del cliente.</param>
public record VehiculoDeCliente(
    long VehiculoId,
    string Tipo,               // "Compra" | "Alquiler"
    string Codigo,             // VC-0001 / AL-0001
    DateTime FechaUtc,
    string Descripcion,        // marca modelo año
    string? Matricula,
    string? Chasis,
    string? Color,
    string EstadoTexto,
    decimal Monto,
    decimal Pendiente);
