// Portado de POS500.Models el 2026-07-30. AccionAuditoria no se porto: se usa
// la de FAControl.Models, que es la misma tabla de auditoria compartida.
namespace FAControl.Models.Pos;

/// <summary>Método de pago de una factura. Coincide con ENUM factura.metodo_pago.</summary>
public enum MetodoPagoFactura
{
    Efectivo,
    Tarjeta,
    Transferencia,
    Mixto
}

/// <summary>Estado de una factura: nunca se elimina, solo se anula.</summary>
public enum EstadoFactura
{
    Emitida,
    Anulada
}

/// <summary>Redondeo del TOTAL de la venta. Coincide con ENUM configuracion_negocio.redondeo.</summary>
public enum ModoRedondeo
{
    Centavo,   // al centavo más cercano (default)
    Peso,      // al peso más cercano
    Arriba     // al peso, siempre hacia arriba
}

/// <summary>Formato del número de factura. Coincide con ENUM configuracion_negocio.factura_formato.</summary>
public enum FormatoFactura
{
    Simple,    // F-0001
    ConAnio    // F-2026-0001
}

/// <summary>
/// Semáforo de caducidad calculado en tiempo real (no se persiste).
/// Umbrales por definir con el cliente en Fase 2 (defaults del POS-400).
/// </summary>
public enum SemaforoCaducidad
{
    Verde,      // lejos de caducar
    Amarillo,   // se acerca
    Naranja,    // muy cerca
    Rojo        // caducado o al límite
}
