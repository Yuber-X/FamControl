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
    TipoVenta TipoVenta = TipoVenta.Contado,
    /// <summary>Quién registró la venta (pedido de Yuber 2026-07-31).</summary>
    string Vendedor = "—");

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
    /// <summary>Días realmente usados (031). NULL mientras está activo.</summary>
    public int? DiasReales { get; set; }
    /// <summary>Lo que corresponde cobrar al cerrar (031). Difiere del pactado si devolvió tarde.</summary>
    public decimal? MontoFinal { get; set; }
    /// <summary>Por qué se cerró el contrato (031). NULL = no se indicó.</summary>
    public string? CerradoMotivo { get; set; }
    public DateTime? CerradoAtUtc { get; set; }
    public string? CerradoPorNombre { get; set; }
    public string? Notas { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>
/// Cómo termina un alquiler (031). Por dentro los dos cierran el contrato y
/// liberan el vehículo, pero NO significan lo mismo y por eso se preguntan:
/// devuelto es plata ganada, cancelado puede ser plata a devolver.
/// </summary>
public enum CierreAlquiler
{
    /// <summary>El cliente usó el auto y lo trajo: el alquiler se cumplió.</summary>
    Devuelto,
    /// <summary>El alquiler no llegó a pasar o se cortó.</summary>
    Cancelado
}

/// <summary>Datos del cierre de un alquiler (031).</summary>
public record CierreAlquilerDatos(
    long AlquilerId,
    CierreAlquiler Tipo,
    /// <summary>Obligatorio: queda en el historial y explica el cierre.</summary>
    string Motivo,
    /// <summary>
    /// Fecha real de devolución. NULL = hoy. Solo aplica a Devuelto; en una
    /// cancelación el auto nunca salió, o volvió sin que el contrato corriera.
    /// </summary>
    DateOnly? FechaDevolucion = null);

/// <summary>Resultado del cierre, para mostrarle al usuario qué pasó con la plata.</summary>
public record ResultadoCierreAlquiler(
    string Codigo,
    CierreAlquiler Tipo,
    int DiasPactados,
    int DiasReales,
    decimal MontoPactado,
    decimal MontoFinal)
{
    public decimal Diferencia => MontoFinal - MontoPactado;
    public bool DevolvioTarde => DiasReales > DiasPactados;
    public bool DevolvioAntes => DiasReales < DiasPactados;
}

/// <summary>
/// Corrección de un alquiler ya registrado (031), para arreglar errores de
/// digitación. Igual que en préstamos, lo que se puede tocar depende de si el
/// contrato sigue abierto — ver AlquilerService.EditarAsync.
/// </summary>
public record EdicionAlquiler(
    long AlquilerId,
    DateOnly FechaInicio,
    DateOnly FechaFin,
    decimal TarifaDia,
    string? Notas,
    /// <summary>Por qué se corrige. Va a la auditoría; obligatorio.</summary>
    string Motivo);

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
    EstadoAlquiler Estado,
    /// <summary>Quién registró el alquiler (pedido de Yuber 2026-07-31).</summary>
    string Registro = "—");

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

/// <summary>Cobro registrado contra un alquiler (034).</summary>
public class AlquilerPago
{
    public long Id { get; set; }
    public long AlquilerId { get; set; }
    /// <summary>Talonario propio del alquiler: RA-000001. Nunca se reusa.</summary>
    public string NumeroRecibo { get; set; } = string.Empty;
    public DateTime FechaPagoUtc { get; set; }
    public decimal Monto { get; set; }
    public MetodoPago MetodoPago { get; set; } = MetodoPago.Efectivo;
    public string? Notas { get; set; }
    public string? CobradoPor { get; set; }
}

/// <summary>
/// Como va el cobro de un alquiler (034). Mientras el contrato esta abierto se
/// mide contra lo PACTADO; una vez cerrado, contra lo que realmente
/// correspondio (031), que puede ser mas si devolvio tarde.
/// </summary>
public record EstadoCobroAlquiler(
    decimal MontoACobrar,
    decimal Cobrado,
    IReadOnlyList<AlquilerPago> Pagos)
{
    public decimal Pendiente => Math.Max(0m, MontoACobrar - Cobrado);
    public bool EstaSaldado => Pendiente <= 0m;
    /// <summary>Lo cobrado de mas: pasa cuando el contrato se cierra por menos dias.</summary>
    public decimal SaldoAFavor => Math.Max(0m, Cobrado - MontoACobrar);
}

/// <summary>Datos de un cobro de alquiler que llega desde la pantalla.</summary>
public record CobroAlquiler(
    long AlquilerId,
    decimal Monto,
    MetodoPago Metodo,
    string? Notas = null);
