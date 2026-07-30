namespace FAControl.Models;

/// <summary>Frecuencia de pago del préstamo. Coincide con ENUM prestamo.modalidad.</summary>
public enum Modalidad
{
    Diaria,
    Semanal,
    Quincenal,
    Mensual,
    /// <summary>
    /// Pago único: UNA sola cuota (capital + interés) en la fecha acordada.
    /// El interés se aplica una vez, no por período. Pedido del cliente 2026-07-17.
    /// </summary>
    PagoUnico
}

/// <summary>
/// Método de cálculo de la tabla de amortización.
/// CuotaFija = interés simple dominicano (interés fijo sobre capital original).
/// Frances = sistema francés (interés sobre saldo insoluto, cuota constante).
/// </summary>
public enum MetodoAmortizacion
{
    Frances,
    CuotaFija,
    /// <summary>
    /// Préstamo ABIERTO ("a sola firma"): el cliente paga SOLO el interés cada
    /// período y el capital queda abierto hasta que decida saldarlo.
    ///
    /// Es como trabaja de verdad buena parte de la cartera de Familia Almonte
    /// (7 de los 10 préstamos del listado del 29-07-2026): "Cuotas: abierto,
    /// Abono a capital: abierto, Total a pagar: solo el interés".
    ///
    /// En la tabla se representa con N cuotas de puro interés y el CAPITAL
    /// COMPLETO en la última. Ese plazo N es el horizonte acordado (o el que se
    /// use para proyectar), no una obligación de saldar: si el cliente sigue
    /// pagando interés, el préstamo se renueva.
    /// </summary>
    SoloInteres
}

/// <summary>Estado del contrato de préstamo. Coincide con ENUM prestamo.estado.</summary>
public enum EstadoPrestamo
{
    Activo,
    Pagado,
    Cancelado
}

/// <summary>Estado persistido de una cuota. Coincide con ENUM cuota.estado.</summary>
public enum EstadoCuota
{
    Pendiente,
    Pagada,
    Vencida,
    EnMora,
    Cancelada
}

/// <summary>
/// Estado calculado en tiempo real para el semáforo de cobros (no se persiste).
/// </summary>
public enum SemaforoCuota
{
    AlDia,       // vence después de hoy, sin pagar
    PorVencer,   // vence en los próximos 7 días
    Vencida,     // venció hace 1 a 15 días sin pagar
    EnMora,      // venció hace más de 15 días sin pagar
    Pagada,      // cubierta por pagos
    Cancelada    // préstamo cancelado
}

/// <summary>Método de pago de un abono. Coincide con ENUM pago.metodo_pago.</summary>
public enum MetodoPago
{
    Efectivo,
    Transferencia,
    Cheque,
    Otro
}

/// <summary>Tipo/carrocería del vehículo (DealerControl). Coincide con ENUM vehiculo.tipo.</summary>
public enum TipoVehiculo
{
    Sedan,
    Suv,
    Jeepeta,
    Camioneta,
    Camion,
    Motor,
    Otro
}

/// <summary>
/// Estado del vehículo en el inventario del dealer. Coincide con ENUM vehiculo.estado.
/// El vehículo NACE 'disponible'; AutoControl lo pasa a 'vendido' al financiarlo.
/// </summary>
public enum EstadoVehiculo
{
    Disponible,
    Reservado,
    Vendido,
    Alquilado,
    Baja
}

/// <summary>Acción registrada en auditoría. Coincide con ENUM auditoria.accion.</summary>
public enum AccionAuditoria
{
    Crear,
    Modificar,
    Eliminar,
    Consultar,
    Login,
    Logout
}
