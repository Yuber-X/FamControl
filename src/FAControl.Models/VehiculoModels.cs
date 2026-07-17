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
    string? Notas);

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
    EstadoVehiculo Estado)
{
    /// <summary>Ganancia estimada al precio de lista (puede ser negativa).</summary>
    public decimal GananciaEstimada => PrecioVenta - CostoTotal;
}

/// <summary>Métricas del inventario para el panel del dealer.</summary>
public record InventarioMetricas(
    int TotalVehiculos,
    int Disponibles,
    decimal CapitalInvertido,
    decimal ValorInventario);
