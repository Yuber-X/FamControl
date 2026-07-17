namespace FAControl.Models;

/// <summary>Cobros de un día de negocio, desglosados (reporte Ingresos por período).</summary>
public record IngresoDiario(DateOnly Fecha, decimal Interes, decimal Capital, decimal Total);

/// <summary>Bucket semanal del reporte (Sem. 1 (01–07 jun), capital, interés).</summary>
public record IngresoSemanal(int NumeroSemana, DateOnly Desde, DateOnly Hasta,
    decimal Capital, decimal Interes)
{
    public decimal Total => Capital + Interes;
}

/// <summary>Resultado completo del reporte Ingresos por período.</summary>
public record ReporteIngresos(
    DateOnly Desde,
    DateOnly Hasta,
    decimal InteresCobrado,
    decimal CapitalRecuperado,
    decimal TotalCobrado,
    int CuotasCobradas,
    int CuotasProgramadas,
    IReadOnlyList<IngresoDiario> PorDia,
    IReadOnlyList<IngresoSemanal> PorSemana);

/// <summary>Filtros del visor de auditoría (Historial). Nulos = sin filtrar.</summary>
public record FiltroAuditoria(
    DateOnly? Desde,
    DateOnly? Hasta,
    string? Entidad,
    AccionAuditoria? Accion,
    int Limite = 300);
