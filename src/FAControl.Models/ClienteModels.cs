namespace FAControl.Models;

/// <summary>Fila de la lista de clientes con agregados de sus préstamos (una sola consulta).</summary>
public record ClienteResumen(
    long Id,
    string Cedula,
    string Nombre,
    string Apellido,
    string? Telefono,
    int PrestamosActivos,
    decimal SaldoPendiente)
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
