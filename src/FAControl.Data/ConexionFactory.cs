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
