// Portado de POS-500 el 2026-07-30 al integrar el punto de venta a la suite.
// Cambios respecto del original: usa ConexionPos500 (base pos500_db, aparte de
// facontrol_db) y el SesionActual / la auditoria compartidos de FAControl.
using MySqlConnector;
using FAControl.Common;
using FAControl.Models.Pos;

namespace FAControl.Data.Pos;

/// <summary>CRUD de clientes. Soft delete siempre; lecturas filtran deleted_at.</summary>
public class ClienteRepository
{
    private readonly ConexionPos500 _factory;

    public ClienteRepository(ConexionPos500 factory) => _factory = factory;

    public async Task<List<Cliente>> ObtenerTodosAsync(CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT id, cedula, nombre, telefono, direccion, notas, created_at, updated_at
            FROM {DbNamesPos.Cliente}
            WHERE deleted_at IS NULL
            ORDER BY nombre;
            """;
        var lista = new List<Cliente>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            lista.Add(Mapear(reader));
        return lista;
    }

    public async Task<Cliente?> ObtenerPorIdAsync(long id, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT id, cedula, nombre, telefono, direccion, notas, created_at, updated_at
            FROM {DbNamesPos.Cliente}
            WHERE id = @id AND deleted_at IS NULL;
            """;
        cmd.Parameters.AddWithValue("@id", id);
        using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Mapear(reader) : null;
    }

    public async Task<bool> ExisteCedulaAsync(string cedula, long? exceptoId, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT COUNT(*) FROM {DbNamesPos.Cliente}
            WHERE cedula = @cedula AND deleted_at IS NULL
              AND (@exceptoId IS NULL OR id <> @exceptoId);
            """;
        cmd.Parameters.AddWithValue("@cedula", cedula);
        cmd.Parameters.AddWithValue("@exceptoId", (object?)exceptoId ?? DBNull.Value);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct)) > 0;
    }

    public async Task<long> InsertarAsync(ClienteDatos datos, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {DbNamesPos.Cliente} (cedula, nombre, telefono, direccion, notas)
            VALUES (@cedula, @nombre, @telefono, @direccion, @notas);
            SELECT LAST_INSERT_ID();
            """;
        AgregarParametros(cmd, datos);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task ActualizarAsync(long id, ClienteDatos datos, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {DbNamesPos.Cliente}
            SET cedula = @cedula, nombre = @nombre, telefono = @telefono,
                direccion = @direccion, notas = @notas, updated_at = UTC_TIMESTAMP()
            WHERE id = @id AND deleted_at IS NULL;
            """;
        AgregarParametros(cmd, datos);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task EliminarAsync(long id, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {DbNamesPos.Cliente} SET deleted_at = UTC_TIMESTAMP()
            WHERE id = @id AND deleted_at IS NULL;
            """;
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static void AgregarParametros(MySqlCommand cmd, ClienteDatos datos)
    {
        cmd.Parameters.AddWithValue("@cedula", (object?)datos.Cedula ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@nombre", datos.Nombre);
        cmd.Parameters.AddWithValue("@telefono", (object?)datos.Telefono ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@direccion", (object?)datos.Direccion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@notas", (object?)datos.Notas ?? DBNull.Value);
    }

    private static Cliente Mapear(MySqlDataReader reader) => new()
    {
        Id = reader.GetInt64("id"),
        Cedula = reader.IsDBNull(reader.GetOrdinal("cedula")) ? null : reader.GetString("cedula"),
        Nombre = reader.GetString("nombre"),
        Telefono = reader.IsDBNull(reader.GetOrdinal("telefono")) ? null : reader.GetString("telefono"),
        Direccion = reader.IsDBNull(reader.GetOrdinal("direccion")) ? null : reader.GetString("direccion"),
        Notas = reader.IsDBNull(reader.GetOrdinal("notas")) ? null : reader.GetString("notas"),
        CreatedAtUtc = DateTime.SpecifyKind(reader.GetDateTime("created_at"), DateTimeKind.Utc),
        UpdatedAtUtc = reader.IsDBNull(reader.GetOrdinal("updated_at"))
            ? null : DateTime.SpecifyKind(reader.GetDateTime("updated_at"), DateTimeKind.Utc)
    };
}
