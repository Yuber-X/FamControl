namespace FAControl.Common;

/// <summary>
/// Sesión del usuario autenticado (patrón SesionActual del POS-400, ahora
/// MULTIUSUARIO: incluye rol y permisos efectivos — pedido del cliente
/// 2026-07-16, porque van a tener personal). Se establece en el login y se
/// limpia en el logout. NUNCA cachear estos valores en variables que
/// sobrevivan al logout: otro empleado puede iniciar sesión después.
/// </summary>
public static class SesionActual
{
    public static long Id { get; private set; }
    public static string Username { get; private set; } = string.Empty;
    public static string Nombre { get; private set; } = string.Empty;
    public static string Rol { get; private set; } = string.Empty;
    public static DateTime LoginAtUtc { get; private set; }
    public static long SesionId { get; private set; }

    private static HashSet<string> _permisos = [];

    public static bool HaySesionActiva => Id > 0;
    public static bool EsAdmin => Rol == Roles.Admin;

    /// <summary>True si el usuario tiene el permiso (código de la tabla permiso).</summary>
    public static bool TienePermiso(string codigoPermiso) => _permisos.Contains(codigoPermiso);

    public static void Iniciar(long id, string username, string nombre, string rol,
        IEnumerable<string> permisos, DateTime loginAtUtc, long sesionId)
    {
        Id = id;
        Username = username;
        Nombre = nombre;
        Rol = rol;
        _permisos = [.. permisos];
        LoginAtUtc = loginAtUtc;
        SesionId = sesionId;
    }

    public static void Cerrar()
    {
        Id = 0;
        Username = string.Empty;
        Nombre = string.Empty;
        Rol = string.Empty;
        _permisos = [];
        LoginAtUtc = default;
        SesionId = 0;
    }
}

/// <summary>Nombres de rol tal como están en la tabla `rol`. Sin cadenas mágicas.</summary>
public static class Roles
{
    public const string Admin = "Admin";
    public const string Supervisor = "Supervisor";
    public const string Cobrador = "Cobrador";
}

/// <summary>
/// Códigos de la tabla `permiso`. Deben coincidir EXACTO con 005_multicuentas.sql;
/// hay un test que lo verifica contra la BD.
/// </summary>
public static class Permisos
{
    public const string Panel = "panel";
    public const string Clientes = "clientes";
    public const string ClientesEditar = "clientes_editar";
    public const string Prestamos = "prestamos";
    public const string PrestamosCrear = "prestamos_crear";
    public const string PrestamosAutorizar = "prestamos_autorizar";
    public const string PrestamosCancelar = "prestamos_cancelar";
    public const string Cobros = "cobros";
    public const string Reportes = "reportes";
    public const string Historial = "historial";
    public const string Usuarios = "usuarios";
    public const string Configuracion = "configuracion";

    /// <summary>Todos los códigos, para validar contra la BD.</summary>
    public static readonly string[] Todos =
    [
        Panel, Clientes, ClientesEditar, Prestamos, PrestamosCrear, PrestamosAutorizar,
        PrestamosCancelar, Cobros, Reportes, Historial, Usuarios, Configuracion
    ];
}
