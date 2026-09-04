using System.Reflection;
using System.Text.RegularExpressions;
using MySqlConnector;

namespace FAControl.Data;

/// <summary>
/// Pone la base al día sola, al arrancar la aplicación.
///
/// POR QUÉ EXISTE (pedido del cliente 2026-08-06)
/// ----------------------------------------------
/// El cliente ya tiene FAControl trabajando y teme que actualizar signifique
/// reinstalar todo desde cero. No lo es — pero hasta ahora las migraciones se
/// aplicaban a mano con scripts\db\aplicar.ps1, que pide la contraseña de MySQL
/// y hay que correr desde PowerShell. Eso no lo puede hacer el cliente, así que
/// cada actualización necesitaba una visita o una sesión de AnyDesk.
///
/// Con esto, el actualizador solo reemplaza los ejecutables y la aplicación
/// acomoda el esquema en el primer arranque. Los DATOS no se tocan nunca: las
/// migraciones agregan columnas, valores de ENUM y filas de catálogo — ninguna
/// borra ni reescribe lo que el cliente cargó.
///
/// REGISTRO
/// --------
/// Se usa la MISMA tabla `esquema_migracion` que aplicar.ps1, así una base que
/// se venía migrando a mano sigue funcionando y las dos vías no se pisan.
///
/// LAS HISTÓRICAS NUNCA SE EJECUTAN
/// --------------------------------
/// Los scripts hasta <see cref="UltimaMigracionHistorica"/> se ANOTAN pero no
/// se corren nunca, pase lo que pase. Dos razones:
///  - ya están dentro de 001_create_schema.sql, así que toda base creada por la
///    app los tiene;
///  - varios NO son repetibles (005 vuelve a insertar los roles y choca contra
///    la clave única), así que intentarlos rompería la base en vez de arreglarla.
/// De la <see cref="UltimaMigracionHistorica"/> en adelante sí se ejecutan: son
/// las que se escriben ya sabiendo que las aplica la app, y por eso van escritas
/// para poder repetirse sin daño.
/// </summary>
public class MigradorEsquema
{
    /// <summary>
    /// Última migración que existía cuando la aplicación aprendió a migrarse
    /// sola (versión 2.0.0). NO tocar: mover este valor hacia adelante haría
    /// que una migración de verdad se dé por aplicada sin haberse ejecutado.
    /// </summary>
    internal const string UltimaMigracionHistorica = "039_alquiler_renovacion.sql";

    private const string Prefijo = "FAControl.Data.migraciones.";

    private readonly string _cadenaConexion;
    private readonly SortedDictionary<string, string> _scripts;

    public MigradorEsquema(ConexionFactory fabrica) : this(fabrica.CadenaConexion) { }

    public MigradorEsquema(string cadenaConexion)
        : this(cadenaConexion, ObtenerMigracionesEmbebidas()) { }

    /// <summary>Permite inyectar los scripts (pruebas del camino de ejecución).</summary>
    internal MigradorEsquema(string cadenaConexion, SortedDictionary<string, string> scripts)
    {
        _scripts = scripts;
        // Varias migraciones usan variables de usuario (@tiene := ...) para ser
        // repetibles. Sin esto MySqlConnector las rechaza. Se activa solo aquí:
        // las conexiones normales de la app no lo necesitan.
        var constructor = new MySqlConnectionStringBuilder(cadenaConexion)
        {
            AllowUserVariables = true
        };
        _cadenaConexion = constructor.ConnectionString;
    }

    /// <summary>
    /// Aplica lo que falte y devuelve los nombres de los scripts ejecutados
    /// (lista vacía = la base ya estaba al día). Si uno falla, corta ahí y
    /// propaga: seguir con el siguiente sobre un esquema a medio migrar
    /// empeora las cosas.
    /// </summary>
    public async Task<IReadOnlyList<string>> AplicarPendientesAsync(CancellationToken ct = default)
    {
        if (_scripts.Count == 0)
            return [];

        await using var conexion = new MySqlConnection(_cadenaConexion);
        await conexion.OpenAsync(ct);

        await CrearRegistroAsync(conexion, ct);
        // Las históricas se anotan siempre, exista o no el registro: nunca hay
        // que correrlas, y dejarlas sin anotar las volvería "pendientes" para
        // siempre. Es idempotente (INSERT IGNORE).
        await MarcarHistoricasAsync(conexion, _scripts.Keys, ct);

        var aplicados = await LeerAplicadosAsync(conexion, ct);
        var ejecutados = new List<string>();

        foreach (var (nombre, sql) in _scripts)
        {
            if (aplicados.Contains(nombre) || EsHistorica(nombre))
                continue;

            // Se trocea igual que el esquema: DELIMITER es un comando del cliente
            // mysql.exe, no SQL, y 005_multicuentas.sql crea triggers con él.
            // Sin esto, una base con registro parcial revienta al arrancar.
            foreach (var bloque in VerificadorBaseDatos.TrocearParaProtocolo(LimpiarParaProtocolo(sql)))
            {
                await using var cmd = conexion.CreateCommand();
                cmd.CommandText = bloque;
                cmd.CommandTimeout = 120;
                await cmd.ExecuteNonQueryAsync(ct);
            }

            await AnotarAsync(conexion, nombre, ct);
            ejecutados.Add(nombre);
        }

        return ejecutados;
    }

    /// <summary>
    /// ¿Es de las que ya venían dentro de 001 y NO se pueden repetir?
    /// El nombre empieza con el número, así que ordenar como texto ordena por
    /// número (todos usan tres dígitos).
    /// </summary>
    internal static bool EsHistorica(string nombre) =>
        string.CompareOrdinal(nombre, UltimaMigracionHistorica) <= 0;

    // ---------- Registro ----------

    private static async Task CrearRegistroAsync(MySqlConnection conexion, CancellationToken ct)
    {
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS esquema_migracion (
              script      VARCHAR(120) NOT NULL PRIMARY KEY,
              aplicado_at DATETIME     NOT NULL DEFAULT (UTC_TIMESTAMP())
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task MarcarHistoricasAsync(MySqlConnection conexion,
        IEnumerable<string> nombres, CancellationToken ct)
    {
        var historicas = nombres.Where(EsHistorica).ToList();
        if (historicas.Count == 0)
            return;

        await using var cmd = conexion.CreateCommand();
        var valores = new List<string>(historicas.Count);
        for (var i = 0; i < historicas.Count; i++)
        {
            valores.Add($"(@s{i})");
            cmd.Parameters.AddWithValue($"@s{i}", historicas[i]);
        }
        cmd.CommandText =
            $"INSERT IGNORE INTO esquema_migracion (script) VALUES {string.Join(",", valores)};";
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<HashSet<string>> LeerAplicadosAsync(MySqlConnection conexion,
        CancellationToken ct)
    {
        var aplicados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = "SELECT script FROM esquema_migracion;";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            aplicados.Add(reader.GetString(0));
        return aplicados;
    }

    private static async Task AnotarAsync(MySqlConnection conexion, string nombre, CancellationToken ct)
    {
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = "INSERT IGNORE INTO esquema_migracion (script) VALUES (@script);";
        cmd.Parameters.AddWithValue("@script", nombre);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ---------- Lectura de los scripts embebidos ----------

    /// <summary>Migraciones embebidas, ordenadas por nombre (que es el orden de aplicación).</summary>
    internal static SortedDictionary<string, string> ObtenerMigracionesEmbebidas()
    {
        var ensamblado = typeof(MigradorEsquema).Assembly;
        var scripts = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (var recurso in ensamblado.GetManifestResourceNames())
        {
            if (!recurso.StartsWith(Prefijo, StringComparison.Ordinal))
                continue;

            using var flujo = ensamblado.GetManifestResourceStream(recurso)!;
            using var lector = new StreamReader(flujo);
            scripts[recurso[Prefijo.Length..]] = lector.ReadToEnd();
        }

        return scripts;
    }

    /// <summary>
    /// Quita lo que solo entiende el cliente mysql.exe y no el protocolo:
    ///  - USE facontrol_db; → la conexión ya viene apuntando a la base correcta,
    ///    y en una instalación con otro nombre de base saltaría a la equivocada.
    ///  - CREATE DATABASE / DROP DATABASE → una migración jamás debe hacer eso.
    /// </summary>
    internal static string LimpiarParaProtocolo(string sql)
    {
        sql = Regex.Replace(sql, @"^\s*USE\s+[0-9A-Za-z$_]+\s*;", string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.Multiline);
        sql = Regex.Replace(sql, @"^\s*(CREATE|DROP)\s+DATABASE[^;]*;", string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.Multiline);
        return sql;
    }
}
