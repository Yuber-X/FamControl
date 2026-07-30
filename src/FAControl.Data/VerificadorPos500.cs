using System.Reflection;
using System.Text.RegularExpressions;
using MySqlConnector;

namespace FAControl.Data;

/// <summary>
/// Aprovisiona la base del punto de venta (`pos500_db`) la primera vez que se
/// entra al modo POS-500.
///
/// Va aparte de <see cref="VerificadorBaseDatos"/> a propósito: la base
/// principal se prepara al arrancar la app, pase lo que pase, mientras que esta
/// solo se toca si el cliente compró el POS y entra a esa estancia. Un cliente
/// que nunca use el punto de venta no termina con una base vacía dando vueltas.
///
/// El esquema viaja embebido en este ensamblado (misma fuente de verdad que
/// scripts/db/pos500_001_create_schema.sql), igual que el de FAControl.
/// </summary>
public class VerificadorPos500
{
    private readonly string _cadenaConexion;

    public VerificadorPos500(ConexionPos500 conexion) => _cadenaConexion = conexion.CadenaConexion;

    /// <summary>Cadena inyectable para los tests de integración.</summary>
    public VerificadorPos500(string cadenaConexion) => _cadenaConexion = cadenaConexion;

    /// <summary>
    /// Se asegura de que la base exista y tenga sus tablas. Es idempotente: si
    /// ya está, no hace nada (el script usa CREATE ... IF NOT EXISTS).
    /// </summary>
    public async Task PrepararAsync(CancellationToken ct = default)
    {
        var constructor = new MySqlConnectionStringBuilder(_cadenaConexion);
        var nombreBd = constructor.Database;
        if (string.IsNullOrEmpty(nombreBd) || !Regex.IsMatch(nombreBd, @"^[0-9A-Za-z$_]+$"))
            throw new InvalidOperationException(
                $"Nombre de base de datos no válido para el punto de venta: '{nombreBd}'.");

        // Primero sin base: puede que todavía no exista
        constructor.Database = string.Empty;
        await using var conexion = new MySqlConnection(constructor.ConnectionString);
        await conexion.OpenAsync(ct);

        await using (var crear = conexion.CreateCommand())
        {
            // DDL: el nombre no se puede parametrizar. Viene del App.config local
            // y ya pasó la validación de arriba.
            crear.CommandText =
                $"CREATE DATABASE IF NOT EXISTS `{nombreBd}` " +
                "CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;";
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

    /// <summary>True si la base del POS ya tiene sus tablas.</summary>
    public async Task<bool> EstaListaAsync(CancellationToken ct = default)
    {
        try
        {
            await using var conexion = new MySqlConnection(_cadenaConexion);
            await conexion.OpenAsync(ct);
            await using var cmd = conexion.CreateCommand();
            cmd.CommandText = """
                SELECT COUNT(*) FROM information_schema.TABLES
                WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'factura';
                """;
            return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) == 1;
        }
        catch (MySqlException)
        {
            // No existe la base, o no se puede llegar: en los dos casos "no lista"
            return false;
        }
    }

    /// <summary>
    /// El script versionado crea y usa pos500_db fijo; acá el nombre lo decide la
    /// cadena de conexión, así que se retiran CREATE DATABASE y USE. Cada
    /// sentencia va por separado porque el protocolo de MySQL no acepta lotes.
    /// </summary>
    internal static List<string> ObtenerBloquesEjecutables()
    {
        // Los comentarios se quitan ANTES de partir. Si no, un comentario con
        // punto y coma —"cierre por usuario y día de negocio; inmutable tras
        // crearse"— corta la sentencia por la mitad y MySQL tira error de
        // sintaxis. Pasó en la primera corrida de este verificador.
        var sql = Regex.Replace(LeerEsquemaSinEncabezado(), @"^\s*--.*$", string.Empty,
            RegexOptions.Multiline);

        // El esquema del POS no tiene triggers ni DELIMITER, así que ya alcanza
        // con partir por ';'.
        return [.. sql.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(bloque => bloque.Trim())
            .Where(bloque => !string.IsNullOrWhiteSpace(bloque))];
    }

    internal static string LeerEsquemaSinEncabezado()
    {
        var ensamblado = typeof(VerificadorPos500).Assembly;
        using var flujo = ensamblado.GetManifestResourceStream("FAControl.Data.pos500_001_create_schema.sql")
            ?? throw new InvalidOperationException(
                "Recurso embebido 'pos500_001_create_schema.sql' no encontrado en FAControl.Data.");
        using var lector = new StreamReader(flujo);
        var sql = lector.ReadToEnd();

        sql = Regex.Replace(sql, @"CREATE DATABASE[^;]+;", string.Empty, RegexOptions.IgnoreCase);
        sql = Regex.Replace(sql, @"^\s*USE\s+[0-9A-Za-z$_]+\s*;", string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.Multiline);
        return sql;
    }
}
