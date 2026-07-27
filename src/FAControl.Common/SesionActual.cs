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

    /// <summary>
    /// Modo/estancia activa de la sesión (se elige en el launcher y se fija al
    /// entrar). Gobierna el AISLAMIENTO de datos: cada modo solo ve lo suyo
    /// (clientes, préstamos…). Ver <see cref="PuedeAccederModo"/> para el acceso.
    /// </summary>
    public static ModoApp Modo { get; private set; }

    private static HashSet<string> _permisos = [];

    public static bool HaySesionActiva => Id > 0;

    /// <summary>
    /// Autoridad total del desarrollador (017). Un Programador ES admin para
    /// todo lo demás del sistema, pero además nadie puede tocarlo: ni verlo en
    /// la lista de usuarios, ni editarlo, ni crear otro. Solo otro Programador.
    /// </summary>
    public static bool EsProgramador => Rol == Roles.Programador;

    public static bool EsAdmin => Rol == Roles.Admin || EsProgramador;

    /// <summary>True si el usuario tiene el permiso (código de la tabla permiso).</summary>
    public static bool TienePermiso(string codigoPermiso) => _permisos.Contains(codigoPermiso);

    /// <summary>
    /// True si el usuario puede ENTRAR a ese modo. El Admin entra a todos; los
    /// demás solo a los que el Admin les habilitó vía el permiso acceso_&lt;modo&gt;
    /// (regla del cliente 2026-07-18: cada empleado atado a su estancia).
    /// </summary>
    public static bool PuedeAccederModo(ModoApp modo) =>
        EsAdmin || TienePermiso(Permisos.AccesoDe(modo));

    /// <summary>Fija el modo activo de la sesión (lo llama el login tras validar el acceso).</summary>
    public static void EstablecerModo(ModoApp modo) => Modo = modo;

    /// <summary>
    /// Filtro de crédito según el modo, para AISLAR PrestControl de AutoControl
    /// (ambos usan la tabla `prestamo`): PrestControl ve solo préstamos
    /// personales (vehiculo_id NULL), AutoControl solo créditos vehiculares
    /// (vehiculo_id NOT NULL). null = sin filtro (DealerControl no toca crédito).
    /// </summary>
    public static bool? SoloVehicularesDelModo => Modo switch
    {
        ModoApp.AutoControl => true,
        ModoApp.PrestControl => false,
        _ => null
    };

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
        Modo = ModoApp.PrestControl;
    }
}

/// <summary>Nombres de rol tal como están en la tabla `rol`. Sin cadenas mágicas.</summary>
public static class Roles
{
    public const string Admin = "Admin";
    /// <summary>Rol blindado del desarrollador (017): autoridad total, intocable por el Admin.</summary>
    public const string Programador = "Programador";
    public const string Supervisor = "Supervisor";
    public const string Cobrador = "Cobrador";
    // Roles de los modos de vehículos (011). 'Vendedor'/'Encargado' se repiten
    // entre DealControl y AutoControl (únicos por nombre+modo en la BD).
    public const string Encargado = "Encargado";
    public const string Vendedor = "Vendedor";
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
    // DealerControl (Tier 5)
    public const string Vehiculos = "vehiculos";
    public const string VehiculosEditar = "vehiculos_editar";
    // DealControl — permisos finos por operación (roles por modo, 011)
    public const string Inventario = "inventario";
    public const string InventarioEditar = "inventario_editar";
    public const string Ventas = "ventas";
    public const string Alquileres = "alquileres";
    public const string Gastos = "gastos";

    // Acceso por modo/estancia (aislamiento — cliente 2026-07-18).
    // El Admin entra a todos sin necesitarlos; estos gobiernan a los demás.
    public const string AccesoPrestControl = "acceso_prestcontrol";
    public const string AccesoDealerControl = "acceso_dealercontrol";
    public const string AccesoAutoControl = "acceso_autocontrol";

    /// <summary>Permiso de acceso correspondiente a un modo.</summary>
    public static string AccesoDe(ModoApp modo) => modo switch
    {
        ModoApp.PrestControl => AccesoPrestControl,
        ModoApp.DealerControl => AccesoDealerControl,
        ModoApp.AutoControl => AccesoAutoControl,
        _ => throw new ArgumentOutOfRangeException(nameof(modo), modo, "Modo desconocido")
    };

    /// <summary>Todos los códigos, para validar contra la BD.</summary>
    public static readonly string[] Todos =
    [
        Panel, Clientes, ClientesEditar, Prestamos, PrestamosCrear, PrestamosAutorizar,
        PrestamosCancelar, Cobros, Reportes, Historial, Usuarios, Configuracion,
        Vehiculos, VehiculosEditar,
        Inventario, InventarioEditar, Ventas, Alquileres, Gastos,
        AccesoPrestControl, AccesoDealerControl, AccesoAutoControl
    ];
}
