namespace FAControl.Common;

/// <summary>
/// Sesión del usuario autenticado (patrón SesionActual del POS-400, simplificado
/// para sistema mono-usuario). Se establece en el login y se limpia en el logout.
/// NUNCA cachear estos valores en variables que sobrevivan al logout.
/// </summary>
public static class SesionActual
{
    public static long Id { get; private set; }
    public static string Username { get; private set; } = string.Empty;
    public static string Nombre { get; private set; } = string.Empty;
    public static DateTime LoginAtUtc { get; private set; }
    public static long SesionId { get; private set; }

    public static bool HaySesionActiva => Id > 0;

    public static void Iniciar(long id, string username, string nombre, DateTime loginAtUtc, long sesionId)
    {
        Id = id;
        Username = username;
        Nombre = nombre;
        LoginAtUtc = loginAtUtc;
        SesionId = sesionId;
    }

    public static void Cerrar()
    {
        Id = 0;
        Username = string.Empty;
        Nombre = string.Empty;
        LoginAtUtc = default;
        SesionId = 0;
    }
}
