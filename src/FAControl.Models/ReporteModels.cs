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
    /// <summary>null = todos los usuarios.</summary>
    long? UsuarioId = null,
    int Limite = 300);

/// <summary>
/// Actividad de un usuario en el rango consultado (cliente 2026-07-16:
/// "agregar los usuarios en el historial y su tiempo activo").
/// </summary>
public record ActividadUsuario(
    long UsuarioId,
    string Nombre,
    string RolNombre,
    int Sesiones,
    int TiempoActivoSegundos,
    int Operaciones,
    DateTime? UltimoAccesoUtc,
    /// <summary>True si tiene una sesión abierta ahora mismo.</summary>
    bool EnLinea)
{
    /// <summary>Tiempo activo legible: "3h 25m". Menos de un minuto se muestra en segundos.</summary>
    public string TiempoActivoTexto
    {
        get
        {
            var t = TimeSpan.FromSeconds(TiempoActivoSegundos);
            if (t.TotalMinutes < 1)
                return $"{t.Seconds}s";
            if (t.TotalHours < 1)
                return $"{t.Minutes}m";
            return $"{(int)t.TotalHours}h {t.Minutes:D2}m";
        }
    }
}
