namespace FAControl.Models;

// ===================== Venta al contado =====================

/// <summary>Venta al contado de un vehículo (DealerControl).</summary>
public class VentaVehiculo
{
    public long Id { get; set; }
    public string Codigo { get; set; } = string.Empty;   // VC-0001
    public long VehiculoId { get; set; }
    public long ClienteId { get; set; }
    public DateTime FechaVentaUtc { get; set; }
    public decimal Precio { get; set; }
    /// <summary>Cómo se pactó: contado, por plazos o separación (016).</summary>
    public TipoVenta TipoVenta { get; set; } = TipoVenta.Contado;
    /// <summary>Inicial/anticipo recibido al firmar (016).</summary>
    public decimal Inicial { get; set; }
    /// <summary>Separación: fecha en que vence el derecho del cliente (016).</summary>
    public DateOnly? FechaLimite { get; set; }
    public MetodoPago MetodoPago { get; set; } = MetodoPago.Efectivo;
    public string? Notas { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>Datos que captura la pantalla de venta (contado, plazos o separación).</summary>
public record VentaVehiculoDatos(
    long VehiculoId,
    long ClienteId,
    decimal Precio,
    MetodoPago MetodoPago,
    string? Notas,
    // Financiamiento del dealer (016). Null = venta al contado de toda la vida.
    TipoVenta TipoVenta = TipoVenta.Contado,
    /// <summary>Plan de plazos cuando <see cref="TipoVenta"/> es Plazos.</summary>
    PlanPlazos? Plan = null,
    /// <summary>Separación: días de derecho del cliente (el dealer usa 15).</summary>
    int DiasSeparacion = 15,
    /// <summary>Separación: adelanto recibido al apartar el vehículo.</summary>
    decimal AdelantoSeparacion = 0m);

/// <summary>Fila de la lista de ventas al contado (con datos del vehículo y cliente vía JOIN).</summary>
public record VentaResumen(
    long Id,
    string Codigo,
    string VehiculoDescripcion,
    string ClienteNombre,
    DateTime FechaVentaUtc,
    decimal Precio,
    MetodoPago MetodoPago,
    /// <summary>Cómo se pactó la venta (016): contado, plazos o separación.</summary>
    TipoVenta TipoVenta = TipoVenta.Contado);

// ===================== Rent a car =====================

/// <summary>Estado del contrato de alquiler. Coincide con ENUM alquiler.estado.</summary>
public enum EstadoAlquiler
{
    Activo,
    Finalizado,
    Cancelado
}

/// <summary>Contrato de alquiler (rent a car — DealerControl).</summary>
public class Alquiler
{
    public long Id { get; set; }
    public string Codigo { get; set; } = string.Empty;   // AL-0001
    public long VehiculoId { get; set; }
    public long ClienteId { get; set; }
    public DateOnly FechaInicio { get; set; }
    public DateOnly FechaFin { get; set; }
    public DateOnly? FechaDevolucion { get; set; }
    public decimal TarifaDia { get; set; }
    public int Dias { get; set; }
    public decimal MontoTotal { get; set; }
    public EstadoAlquiler Estado { get; set; } = EstadoAlquiler.Activo;
    public string? Notas { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>Datos que captura la pantalla de nuevo alquiler.</summary>
public record AlquilerDatos(
    long VehiculoId,
    long ClienteId,
    DateOnly FechaInicio,
    DateOnly FechaFin,
    decimal TarifaDia,
    string? Notas);

/// <summary>Fila de la lista de alquileres (con datos del vehículo y cliente).</summary>
public record AlquilerResumen(
    long Id,
    string Codigo,
    string VehiculoDescripcion,
    string ClienteNombre,
    DateOnly FechaInicio,
    DateOnly FechaFin,
    int Dias,
    decimal MontoTotal,
    EstadoAlquiler Estado);

// ===================== Gastos de importación =====================

/// <summary>Gasto/costo asociado a un vehículo (gestión de importación).</summary>
public class VehiculoGasto
{
    public long Id { get; set; }
    public long VehiculoId { get; set; }
    public string Concepto { get; set; } = string.Empty;
    public decimal Monto { get; set; }
    public DateOnly Fecha { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>Datos que captura el alta de un gasto.</summary>
public record VehiculoGastoDatos(
    long VehiculoId,
    string Concepto,
    decimal Monto,
    DateOnly Fecha);
