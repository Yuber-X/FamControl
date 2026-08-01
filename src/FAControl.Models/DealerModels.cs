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

// ===================== Renovación del alquiler (039) =====================

/// <summary>
/// Un tramo de renovación: el cliente sigue con el auto más allá de la fecha
/// pactada (039).
///
/// Guarda SU tarifa porque puede no ser la misma. Si la renovación solo
/// corriera la fecha, el monto del contrato (tarifa × días) se recalcularía
/// entero a la tarifa nueva y le cambiaría el precio a días que el cliente ya
/// usó, y quizás ya pagó.
/// </summary>
public class AlquilerRenovacion
{
    public long Id { get; set; }
    public long AlquilerId { get; set; }
    /// <summary>Hasta cuándo iba el contrato antes de esta renovación.</summary>
    public DateOnly FechaFinAnterior { get; set; }
    public DateOnly FechaFinNueva { get; set; }
    /// <summary>Tarifa de ESTE tramo. Puede ser la misma de antes o una nueva.</summary>
    public decimal TarifaDia { get; set; }
    public int Dias { get; set; }
    public decimal Monto { get; set; }
    public string? Notas { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string? CreadoPorNombre { get; set; }
}

/// <summary>
/// Lo que pide la pantalla para renovar: hasta cuándo sigue y a qué precio.
/// </summary>
public record RenovacionAlquiler(
    long AlquilerId,
    /// <summary>Nueva fecha de devolución. Tiene que ser posterior a la actual.</summary>
    DateOnly FechaFinNueva,
    /// <summary>Tarifa del tramo nuevo. El diálogo la propone igual a la vigente.</summary>
    decimal TarifaDia,
    string? Notas = null);

/// <summary>Cómo quedó el contrato después de renovar, para contárselo al usuario.</summary>
public record ResultadoRenovacion(
    string Codigo,
    DateOnly FechaFinAnterior,
    DateOnly FechaFinNueva,
    int DiasAgregados,
    decimal TarifaDia,
    decimal MontoAgregado,
    int DiasTotales,
    decimal MontoTotal)
{
    /// <summary>True si el tramo nuevo va a otro precio que el anterior.</summary>
    public bool CambioLaTarifa { get; init; }
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
/// Una cuota MENSUAL del alquiler (037). No se guarda en la base: se calcula
/// del periodo pactado y la tarifa, igual que el semaforo de las cuotas de un
/// prestamo.
///
/// POR QUE MENSUAL: "la idea del grid es que almacene la cantidad de cobros
/// mensuales hasta el dia pactado (como si fueran plazos); usualmente estos
/// cobros son mensuales" (Yuber 2026-08-01). Un alquiler largo no se paga de
/// una: se cobra mes a mes.
/// </summary>
public record CuotaAlquiler(
    int Numero,
    DateOnly Desde,
    DateOnly Hasta,
    int Dias,
    decimal Monto,
    decimal Pagado)
{
    public decimal Pendiente => Math.Max(0m, Monto - Pagado);
    public bool EstaPagada => Pagado >= Monto;
    /// <summary>Vencida y sin cubrir: el mes ya paso y sigue debiendo.</summary>
    public bool EstaAtrasada(DateOnly hoy) => !EstaPagada && Hasta < hoy;
}

/// <summary>
/// Como va el cobro de un alquiler (034). Mientras el contrato esta abierto se
/// mide contra lo PACTADO; una vez cerrado, contra lo que realmente
/// correspondio (031), que puede ser mas si devolvio tarde.
/// </summary>
public record EstadoCobroAlquiler(
    decimal MontoACobrar,
    decimal Cobrado,
    IReadOnlyList<AlquilerPago> Pagos,
    /// <summary>Las cuotas mensuales del periodo, con lo que se cubrio de cada una (037).</summary>
    IReadOnlyList<CuotaAlquiler> Calendario)
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
