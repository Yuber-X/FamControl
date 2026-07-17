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

        await using var esquema = conexion.CreateCommand();
        esquema.CommandText = LeerEsquemaSinEncabezado();
        esquema.CommandTimeout = 120;
        await esquema.ExecuteNonQueryAsync(ct);
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
}
