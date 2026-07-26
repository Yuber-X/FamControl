namespace FAControl.Models;

/// <summary>Movimiento reciente del dealer (venta al contado o alquiler).</summary>
public record MovimientoDeal(
    string Tipo,               // "Venta" | "Alquiler"
    string Codigo,             // VC-0001 / AL-0001
    DateTime FechaUtc,
    string ClienteNombre,
    string VehiculoDescripcion,
    decimal Monto);

/// <summary>
/// KPIs del panel principal de DealControl (pedido 2026-07-25). SOLO datos del
/// dealer: inventario, ventas al contado y alquileres — nada de PrestControl.
/// </summary>
public record ResumenPanelDeal(
    int VehiculosDisponibles,
    int VehiculosAlquilados,
    /// <summary>Costo total invertido (adquisición + importación) del inventario sin vender.</summary>
    decimal CapitalInvertido,
    int VentasMes,
    decimal MontoVentasMes,
    /// <summary>Ganancia de las ventas del mes: precio − costo total del vehículo.</summary>
    decimal GananciaVentasMes,
    int AlquileresActivos,
    decimal IngresosAlquilerMes,
    IReadOnlyList<MovimientoDeal> UltimosMovimientos);
