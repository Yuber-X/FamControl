namespace FAControl.Models;

/// <summary>Frecuencia de pago del préstamo. Coincide con ENUM prestamo.modalidad.</summary>
public enum Modalidad
{
    Diaria,
    Semanal,
    Quincenal,
    Mensual
}

/// <summary>
/// Método de cálculo de la tabla de amortización.
/// CuotaFija = interés simple dominicano (interés fijo sobre capital original).
/// Frances = sistema francés (interés sobre saldo insoluto, cuota constante).
/// </summary>
public enum MetodoAmortizacion
{
    Frances,
    CuotaFija
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
