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
/// Un mes del historial del dealer (gráfico del panel, 2026-07-27): cuánto
/// se vendió y cuánto entró por alquiler.
/// </summary>
public record MesDeal(int Anio, int Mes, decimal MontoVentas, decimal MontoAlquiler)
{
    private static readonly string[] Meses =
        ["ene", "feb", "mar", "abr", "may", "jun", "jul", "ago", "sep", "oct", "nov", "dic"];

    /// <summary>Etiqueta corta del eje X: "jul 26".</summary>
    public string Etiqueta => $"{Meses[Mes - 1]} {Anio % 100:00}";
}

/// <summary>Cuántos vehículos hay en cada estado (gráfico de torta del panel).</summary>
public record ConteoInventario(string Estado, int Cantidad);

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
    IReadOnlyList<MovimientoDeal> UltimosMovimientos,
    /// <summary>Últimos 6 meses de ventas y alquileres (gráfico de barras).</summary>
    IReadOnlyList<MesDeal> UltimosMeses,
    /// <summary>Composición del inventario por estado (gráfico de torta).</summary>
    IReadOnlyList<ConteoInventario> Inventario);
