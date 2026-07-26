namespace FAControl.Models;

/// <summary>
/// Datos que captura el formulario de vehículo (nuevo o edición).
/// El código y el estado los gestiona el service, no el formulario.
/// </summary>
public record VehiculoDatos(
    string? Vin,
    string Marca,
    string Modelo,
    int? Anio,
    string? Color,
    string? Placa,
    TipoVehiculo Tipo,
    int? Kilometraje,
    decimal CostoAdquisicion,
    decimal GastosImportacion,
    decimal PrecioVenta,
    string? Notas,
    /// <summary>Nro. del certificado de matrícula DGII (015). Distinto de la placa.</summary>
    string? Matricula = null);

/// <summary>Fila de la lista de inventario (pantalla Vehículos de DealerControl).</summary>
public record VehiculoResumen(
    long Id,
    string Codigo,
    string Descripcion,
    TipoVehiculo Tipo,
    int? Anio,
    string? Placa,
    decimal CostoTotal,
    decimal PrecioVenta,
    EstadoVehiculo Estado,
    // Ficha ampliada del inventario (pedido 2026-07-25)
    string? Vin = null,
    string? Color = null,
    string? Matricula = null)
{
    /// <summary>Ganancia estimada al precio de lista (puede ser negativa).</summary>
    public decimal GananciaEstimada => PrecioVenta - CostoTotal;
}

/// <summary>Reparación/mantenimiento del vehículo (015 — pedido 2026-07-25).</summary>
public class VehiculoReparacion
{
    public long Id { get; set; }
    public long VehiculoId { get; set; }
    public DateOnly Fecha { get; set; }
    public string Detalle { get; set; } = string.Empty;
    public decimal Costo { get; set; }
    public string? RegistradaPor { get; set; }
}

/// <summary>
/// Ficha completa del vehículo (pedido 2026-07-25): datos + comprador (si se
/// vendió al contado o financiado) + historial de reparaciones.
/// </summary>
public record FichaVehiculo(
    Vehiculo Vehiculo,
    /// <summary>Venta al contado, si existe (código, fecha, precio, cliente).</summary>
    VentaResumen? Venta,
    /// <summary>Crédito vehicular de AutoControl, si existe (código y cliente).</summary>
    string? CreditoCodigo,
    string? CreditoClienteNombre,
    IReadOnlyList<VehiculoReparacion> Reparaciones)
{
    public decimal CostoReparaciones => Reparaciones.Sum(r => r.Costo);
}

/// <summary>Métricas del inventario para el panel del dealer.</summary>
public record InventarioMetricas(
    int TotalVehiculos,
    int Disponibles,
    decimal CapitalInvertido,
    decimal ValorInventario);
