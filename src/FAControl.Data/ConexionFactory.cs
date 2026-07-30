using System.Configuration;
using MySqlConnector;

namespace FAControl.Data;

/// <summary>
/// Adaptación del patrón CConexion del POS-400 a WPF/.NET 8:
///  - cadena de conexión leída de App.config (nunca hardcodeada)
///  - conexiones async y desechables (using) — sin estado compartido
///  - los errores se propagan al llamador (Serilog los registra arriba);
///    aquí NUNCA se muestra UI (nada de MessageBox en capa de datos)
/// </summary>
public class ConexionFactory
{
    /// <summary>Nombre de la cadena principal en App.config.</summary>
    public const string NombreCadenaPrincipal = "FAControlDb";

    private readonly string _cadenaConexion;

    /// <summary>Lee la cadena "FAControlDb" desde App.config.</summary>
    public ConexionFactory() : this(DeConfig(NombreCadenaPrincipal)) { }

    /// <summary>Permite inyectar la cadena directamente (tests de integración).</summary>
    public ConexionFactory(string cadenaConexion) => _cadenaConexion = cadenaConexion;

    /// <summary>Expuesta para servicios que invocan herramientas externas (mysqldump).</summary>
    public string CadenaConexion => _cadenaConexion;

    /// <summary>Abre una conexión nueva. El llamador es dueño de su ciclo de vida (using).</summary>
    public async Task<MySqlConnection> AbrirAsync(CancellationToken ct = default)
    {
        var conexion = new MySqlConnection(_cadenaConexion);
        await conexion.OpenAsync(ct);
        return conexion;
    }

    /// <summary>Lee una cadena con nombre de App.config.</summary>
    protected static string DeConfig(string nombre) =>
        ConfigurationManager.ConnectionStrings[nombre]?.ConnectionString
        ?? throw new InvalidOperationException(
            $"No se encontró la cadena de conexión '{nombre}' en App.config.");
}

/// <summary>
/// La SEGUNDA base: el punto de venta (POS-500) guarda sus datos en `pos500_db`,
/// aparte de facontrol_db (decisión con Yuber 2026-07-30 — el POS se vende por
/// separado, así que sus datos tienen que poder irse solos).
///
/// Es un tipo propio y no una cadena suelta para que el contenedor de
/// dependencias no las confunda: un repositorio del POS pide ConexionPos500 y
/// no hay forma de que le llegue por error la de préstamos.
///
/// Si App.config todavía no trae la cadena 'POS500Db' —el caso de una
/// instalación que se actualiza— se DERIVA de la principal cambiándole el
/// nombre de la base. Así actualizar no obliga a editar el archivo a mano.
/// </summary>
public class ConexionPos500 : ConexionFactory
{
    public const string NombreCadena = "POS500Db";
    public const string BasePorDefecto = "pos500_db";

    public ConexionPos500() : base(Resolver()) { }

    /// <summary>Permite inyectar la cadena directamente (tests de integración).</summary>
    public ConexionPos500(string cadenaConexion) : base(cadenaConexion) { }

    private static string Resolver()
    {
        if (ConfigurationManager.ConnectionStrings[NombreCadena]?.ConnectionString is { } propia
            && !string.IsNullOrWhiteSpace(propia))
            return propia;

        // Derivada de la principal: mismo servidor y credenciales, otra base
        return DerivarDe(DeConfig(NombreCadenaPrincipal));
    }

    /// <summary>Misma cadena que la principal pero apuntando a <c>pos500_db</c>.</summary>
    public static string DerivarDe(string cadenaPrincipal) =>
        new MySqlConnectionStringBuilder(cadenaPrincipal) { Database = BasePorDefecto }
            .ConnectionString;
}
