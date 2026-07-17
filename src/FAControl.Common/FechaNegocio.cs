namespace FAControl.Common;

/// <summary>
/// Fecha de negocio en República Dominicana (America/Santo_Domingo, UTC-4 sin DST).
/// La BD guarda UTC; los vencimientos y el semáforo se evalúan con esta fecha local.
/// </summary>
public static class FechaNegocio
{
    private static readonly TimeZoneInfo ZonaRd = ObtenerZona();

    public static DateOnly Hoy => DateOnly.FromDateTime(AhoraLocal());

    public static DateTime AhoraLocal() => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ZonaRd);

    public static DateTime AUtcLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), ZonaRd);

    private static TimeZoneInfo ObtenerZona()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("America/Santo_Domingo");
        }
        catch (TimeZoneNotFoundException)
        {
            // Id equivalente en Windows sin datos ICU
            return TimeZoneInfo.FindSystemTimeZoneById("SA Western Standard Time");
        }
    }
}
