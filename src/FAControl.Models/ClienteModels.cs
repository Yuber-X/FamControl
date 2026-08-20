namespace FAControl.Models;

/// <summary>Fila de la lista de clientes con agregados de sus préstamos (una sola consulta).</summary>
public record ClienteResumen(
    long Id,
    string Cedula,
    string Nombre,
    string Apellido,
    string? Telefono,
    /// <summary>
    /// Contratos abiertos del cliente. QUE cuenta depende de la estancia: en
    /// PrestControl y AutoControl son préstamos activos; en DealControl son
    /// vehículos que compró (2026-07-31). Es la misma columna con distinto
    /// significado, y el encabezado lo dice.
    /// </summary>
    int ContratosAbiertos,
    /// <summary>
    /// Lo que el cliente todavía debe. En crédito son las cuotas impagas; en
    /// el dealer, los plazos pendientes de sus ventas financiadas.
    /// </summary>
    decimal SaldoPendiente,
    /// <summary>Alquileres del cliente. Solo aplica a DealControl; 0 en el resto.</summary>
    int Alquileres = 0)
{
    public string NombreCompleto => $"{Nombre} {Apellido}".Trim();
}

/// <summary>Métricas de la ficha de cliente (mockup 3: cinco cards resumen).</summary>
public record ClienteMetricas(
    decimal TotalPrestado,
    decimal TotalCobrado,
    decimal SaldoPendiente,
    int PrestamosActivos,
    int CuotasVencidas);

/// <summary>Calificación de conducta de pago. Se calcula, nunca se guarda.</summary>
public enum ConductaCliente
{
    /// <summary>Todavía no saldó ninguna cuota: no hay con qué juzgarlo.</summary>
    SinHistorial,
    Excelente,
    Buena,
    Regular,
    Riesgosa
}

/// <summary>
/// Historial de buena conducta del cliente (pedido 2026-08-06): cómo pagó lo
/// que ya pagó. Todo sale de préstamos, cuotas y pagos que ya están en la base
/// — no hay tabla nueva ni nada que el usuario tenga que cargar a mano.
///
/// "A tiempo" se mide contra la fecha en que la cuota quedó SALDADA (el último
/// abono que la cubrió), no contra el primer abono: una cuota que se pagó en
/// tres partes se juzga por cuándo terminó de pagarse.
/// </summary>
public record ClienteConducta(
    int PrestamosTotales,
    int PrestamosSaldados,
    int PrestamosActivos,
    int PrestamosCancelados,
    /// <summary>Cuotas que el cliente terminó de pagar (las únicas que se pueden juzgar).</summary>
    int CuotasSaldadas,
    int CuotasATiempo,
    int CuotasTarde,
    /// <summary>Promedio de días de atraso, contando SOLO las que se pagaron tarde.</summary>
    int DiasPromedioAtraso,
    int PeorAtrasoDias,
    /// <summary>Cuotas que hoy están vencidas o en mora sin cubrir.</summary>
    int CuotasVencidasHoy,
    DateOnly? PrimerPrestamo,
    DateOnly? UltimoPago)
{
    public bool EsClienteConocido => PrestamosTotales > 0;

    /// <summary>Porcentaje de cuotas saldadas que se pagaron en fecha o antes.</summary>
    public int PorcentajeATiempo => CuotasSaldadas == 0
        ? 0
        : (int)Math.Round(CuotasATiempo * 100m / CuotasSaldadas, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Los cortes son una PROPUESTA (2026-08-06) y se ajustan con el cliente:
    /// cada prestamista tiene su propio umbral de lo que considera "buen pagador".
    ///
    /// Riesgosa  → hoy debe cuotas, o alguna vez se atrasó más de 30 días
    /// Regular   → menos del 70% a tiempo, o promedio de atraso de más de 7 días
    /// Excelente → 95% o más a tiempo y nada vencido hoy
    /// Buena     → el resto de los que ya pagaron algo
    /// </summary>
    public ConductaCliente Calificacion
    {
        get
        {
            if (CuotasSaldadas == 0)
                return ConductaCliente.SinHistorial;
            if (CuotasVencidasHoy > 0 || PeorAtrasoDias > 30)
                return ConductaCliente.Riesgosa;
            if (PorcentajeATiempo < 70 || DiasPromedioAtraso > 7)
                return ConductaCliente.Regular;
            if (PorcentajeATiempo >= 95)
                return ConductaCliente.Excelente;
            return ConductaCliente.Buena;
        }
    }
}

/// <summary>
/// Cliente con cuotas vencidas (notificador de vencimientos al iniciar).
/// PrimerVencimiento = la fecha vencida más antigua sin cubrir.
/// </summary>
public record ClienteVencido(
    long ClienteId,
    string NombreCompleto,
    int CuotasVencidas,
    decimal MontoVencido,
    DateOnly PrimerVencimiento);

/// <summary>Datos que captura el formulario de cliente (nuevo o edición).</summary>
public record ClienteDatos(
    string Cedula,
    string Nombre,
    string Apellido,
    string? Telefono,
    string? Direccion,
    string? Email,
    string? Notas);

/// <summary>
/// Cliente con cuota próxima a vencer o vencida, para el recordatorio por
/// correo (cliente 2026-07-19). Incluye el email del cliente (puede ser null).
/// </summary>
public record RecordatorioCliente(
    long ClienteId,
    string NombreCompleto,
    string? Email,
    DateOnly ProximoVencimiento,
    decimal MontoPendiente,
    int CuotasEnVentana,
    bool HayVencidas);
