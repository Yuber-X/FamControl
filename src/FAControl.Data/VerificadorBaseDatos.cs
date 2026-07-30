using System.Text.RegularExpressions;
using MySqlConnector;

namespace FAControl.Data;

/// <summary>Resultado del diagnóstico de conexión al arrancar la app.</summary>
public enum EstadoBaseDatos
{
    /// <summary>Conecta y el esquema existe: se puede operar.</summary>
    Lista,

    /// <summary>El servidor responde pero la base de datos (o sus tablas) no existe.</summary>
    FaltaBaseDatos,

    /// <summary>Usuario/contraseña de la cadena de conexión rechazados.</summary>
    CredencialesInvalidas,

    /// <summary>MySQL no responde (servicio detenido, puerto o host incorrectos).</summary>
    SinServidor
}

/// <summary>
/// Diagnostica el estado de la base de datos al arrancar y permite crear el
/// esquema completo (auto-aprovisionamiento del primer arranque). El script
/// 001_create_schema.sql viaja embebido en este ensamblado: una sola fuente
/// de verdad con scripts/db/.
/// </summary>
public class VerificadorBaseDatos
{
    private readonly string _cadenaConexion;

    public VerificadorBaseDatos(ConexionFactory fabrica) => _cadenaConexion = fabrica.CadenaConexion;

    /// <summary>Permite inyectar la cadena directamente (tests de integración).</summary>
    public VerificadorBaseDatos(string cadenaConexion) => _cadenaConexion = cadenaConexion;

    public async Task<EstadoBaseDatos> VerificarAsync(CancellationToken ct = default)
    {
        try
        {
            await using var conexion = new MySqlConnection(_cadenaConexion);
            await conexion.OpenAsync(ct);

            // La BD existe; confirmar que el esquema esté creado (tabla usuario)
            await using var cmd = conexion.CreateCommand();
            cmd.CommandText = "SHOW TABLES LIKE 'usuario';";
            var tabla = await cmd.ExecuteScalarAsync(ct);
            return tabla is null ? EstadoBaseDatos.FaltaBaseDatos : EstadoBaseDatos.Lista;
        }
        catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.UnknownDatabase)
        {
            return EstadoBaseDatos.FaltaBaseDatos;
        }
        catch (MySqlException ex) when (
            ex.ErrorCode == MySqlErrorCode.AccessDenied ||
            ex.ErrorCode == MySqlErrorCode.DatabaseAccessDenied)
        {
            return EstadoBaseDatos.CredencialesInvalidas;
        }
        catch (MySqlException)
        {
            return EstadoBaseDatos.SinServidor;
        }
    }

    /// <summary>
    /// Crea la base de datos (nombre tomado de la cadena de conexión) y ejecuta
    /// el esquema embebido. Requiere que el usuario de la cadena tenga permiso
    /// CREATE: root sí; el usuario dedicado 'facontrol' no (por diseño).
    /// </summary>
    public async Task CrearEsquemaAsync(CancellationToken ct = default)
    {
        var constructor = new MySqlConnectionStringBuilder(_cadenaConexion);
        var nombreBd = constructor.Database;
        if (string.IsNullOrEmpty(nombreBd) || !Regex.IsMatch(nombreBd, @"^[0-9A-Za-z$_]+$"))
            throw new InvalidOperationException(
                $"Nombre de base de datos no válido en la cadena de conexión: '{nombreBd}'.");

        constructor.Database = string.Empty;
        await using var conexion = new MySqlConnection(constructor.ConnectionString);
        await conexion.OpenAsync(ct);

        await using (var crear = conexion.CreateCommand())
        {
            // El nombre no puede parametrizarse en DDL; viene del App.config
            // local (no de entrada del usuario) y ya fue validado arriba.
            crear.CommandText =
                $"CREATE DATABASE IF NOT EXISTS `{nombreBd}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;";
            await crear.ExecuteNonQueryAsync(ct);
        }

        await conexion.ChangeDatabaseAsync(nombreBd, ct);

        foreach (var bloque in ObtenerBloquesEjecutables())
        {
            await using var cmd = conexion.CreateCommand();
            cmd.CommandText = bloque;
            cmd.CommandTimeout = 120;
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    /// <summary>
    /// ⚠️ DESTRUCTIVO — borra la base entera y la vuelve a crear vacía.
    /// Lo usa el CÓDIGO 6 del launcher ("hacer respaldo y limpiar todo", pedido
    /// del cliente 2026-07-29). Quien lo llama es responsable de haber hecho el
    /// respaldo antes: acá ya no hay vuelta atrás.
    /// Requiere permiso DROP: root sí lo tiene, el usuario dedicado no.
    /// </summary>
    public async Task BorrarYRecrearAsync(CancellationToken ct = default)
    {
        await BorrarAsync(ct);
        await CrearEsquemaAsync(ct);
    }

    /// <summary>
    /// ⚠️ DESTRUCTIVO — borra la base y NO la recrea. Lo usa el CÓDIGO 7
    /// ("eliminar todo"): la instalación queda como si nunca se hubiera usado y
    /// el próximo arranque vuelve a ofrecer crear la base.
    /// </summary>
    public async Task BorrarAsync(CancellationToken ct = default)
    {
        var constructor = new MySqlConnectionStringBuilder(_cadenaConexion);
        var nombreBd = constructor.Database;
        if (string.IsNullOrEmpty(nombreBd) || !Regex.IsMatch(nombreBd, @"^[0-9A-Za-z$_]+$"))
            throw new InvalidOperationException(
                $"Nombre de base de datos no válido en la cadena de conexión: '{nombreBd}'.");

        constructor.Database = string.Empty;
        await using var conexion = new MySqlConnection(constructor.ConnectionString);
        await conexion.OpenAsync(ct);
        await using var borrar = conexion.CreateCommand();
        // DDL: el nombre no se puede parametrizar. Viene del App.config local
        // y ya pasó la validación de arriba.
        borrar.CommandText = $"DROP DATABASE IF EXISTS `{nombreBd}`;";
        borrar.CommandTimeout = 120;
        await borrar.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// El script versionado crea y usa facontrol_db fijo; aquí el nombre lo
    /// decide la cadena de conexión, así que se retiran CREATE DATABASE y USE.
    /// </summary>
    internal static string LeerEsquemaSinEncabezado()
    {
        var ensamblado = typeof(VerificadorBaseDatos).Assembly;
        using var flujo = ensamblado.GetManifestResourceStream("FAControl.Data.001_create_schema.sql")
            ?? throw new InvalidOperationException(
                "Recurso embebido '001_create_schema.sql' no encontrado en FAControl.Data.");
        using var lector = new StreamReader(flujo);
        var sql = lector.ReadToEnd();

        sql = Regex.Replace(sql, @"CREATE DATABASE[^;]+;", string.Empty, RegexOptions.IgnoreCase);
        sql = Regex.Replace(sql, @"^\s*USE\s+[0-9A-Za-z$_]+\s*;", string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.Multiline);
        return sql;
    }

    /// <summary>
    /// Prepara el esquema para ejecutarse POR PROTOCOLO (no por mysql.exe):
    ///  - la parte normal va en un solo batch multi-statement;
    ///  - cada trigger (entre DELIMITER $$ y DELIMITER ;) va como comando aparte.
    ///
    /// Hace falta porque DELIMITER es un comando del CLIENTE mysql.exe, no SQL:
    /// mandarlo por el protocolo revienta. Patrón heredado de POS-500.
    /// </summary>
    internal static List<string> ObtenerBloquesEjecutables()
    {
        var bloques = new List<string>();
        var sql = LeerEsquemaSinEncabezado();

        // Zona de triggers: DELIMITER $$ ... DELIMITER ;
        var partes = Regex.Split(sql, @"^\s*DELIMITER\s+\$\$\s*$", RegexOptions.Multiline);
        AgregarSiTieneContenido(bloques, partes[0]);

        for (var i = 1; i < partes.Length; i++)
        {
            var zona = Regex.Split(partes[i], @"^\s*DELIMITER\s+;\s*$", RegexOptions.Multiline);
            foreach (var trigger in zona[0].Split("$$", StringSplitOptions.RemoveEmptyEntries))
                AgregarSiTieneContenido(bloques, trigger);
            if (zona.Length > 1)
                AgregarSiTieneContenido(bloques, zona[1]);
        }
        return bloques;
    }

    private static void AgregarSiTieneContenido(List<string> bloques, string sql)
    {
        // Un bloque de solo comentarios/espacios no es ejecutable: MySQL
        // devuelve error de sintaxis si se le manda vacío.
        var sinComentarios = Regex.Replace(sql, @"^\s*--.*$", string.Empty, RegexOptions.Multiline);
        if (!string.IsNullOrWhiteSpace(sinComentarios.Replace(";", string.Empty)))
            bloques.Add(sql.Trim());
    }
}
