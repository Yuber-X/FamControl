namespace FAControl.Models;

/// <summary>
/// Unidad del inventario del dealer (DealerControl). El vehículo como ACTIVO:
/// nace aquí y AutoControl lo consume por FK cuando se vende financiado.
/// Soft delete vía DeletedAtUtc.
/// </summary>
public class Vehiculo
{
    public long Id { get; set; }
    public string Codigo { get; set; } = string.Empty;   // V-0001
    public string? Vin { get; set; }                       // chasis / VIN
    public string Marca { get; set; } = string.Empty;
    public string Modelo { get; set; } = string.Empty;
    public int? Anio { get; set; }
    public string? Color { get; set; }
    public string? Placa { get; set; }
    public TipoVehiculo Tipo { get; set; } = TipoVehiculo.Otro;
    public int? Kilometraje { get; set; }
    /// <summary>Lo que costó comprar el vehículo.</summary>
    public decimal CostoAdquisicion { get; set; }
    /// <summary>Aduana, flete, preparación (gestión de importación).</summary>
    public decimal GastosImportacion { get; set; }
    /// <summary>Precio de lista para la venta.</summary>
    public decimal PrecioVenta { get; set; }
    public EstadoVehiculo Estado { get; set; } = EstadoVehiculo.Disponible;
    public string? Notas { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public DateTime? DeletedAtUtc { get; set; }

    /// <summary>Costo total invertido = adquisición + importación.</summary>
    public decimal CostoTotal => CostoAdquisicion + GastosImportacion;

    /// <summary>Ganancia estimada si se vende al precio de lista (puede ser negativa).</summary>
    public decimal GananciaEstimada => PrecioVenta - CostoTotal;

    public string Descripcion =>
        $"{Marca} {Modelo}{(Anio is { } a ? $" {a}" : string.Empty)}".Trim();
}
