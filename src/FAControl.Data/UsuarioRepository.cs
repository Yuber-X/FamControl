using MySqlConnector;
using FAControl.Common;
using FAControl.Models;

namespace FAControl.Data;

/// <summary>Acceso a la tabla usuario (cuenta única del prestamista).</summary>
public class UsuarioRepository
{
    private readonly ConexionFactory _factory;

    public UsuarioRepository(ConexionFactory factory) => _factory = factory;

    /// <summary>True si ya existe al menos un usuario (decide wizard inicial vs login).</summary>
    public async Task<bool> ExisteAlgunUsuarioAsync(CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {DbNames.Usuario};";
        var total = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
        return total > 0;
    }

    public async Task<Usuario?> ObtenerPorUsernameAsync(string username, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT id, username, password_hash, nombre, activo, created_at, last_login_at
            FROM {DbNames.Usuario}
            WHERE username = @username AND activo = 1;
            """;
        cmd.Parameters.AddWithValue("@username", username);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        return new Usuario
        {
            Id = reader.GetInt64("id"),
            Username = reader.GetString("username"),
            PasswordHash = reader.GetString("password_hash"),
            Nombre = reader.GetString("nombre"),
            Activo = reader.GetBoolean("activo"),
            CreatedAtUtc = DateTime.SpecifyKind(reader.GetDateTime("created_at"), DateTimeKind.Utc),
            LastLoginAtUtc = reader.IsDBNull(reader.GetOrdinal("last_login_at"))
                ? null
                : DateTime.SpecifyKind(reader.GetDateTime("last_login_at"), DateTimeKind.Utc)
        };
    }

    public async Task<long> CrearAsync(string username, string passwordHash, string nombre, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {DbNames.Usuario} (username, password_hash, nombre)
            VALUES (@username, @passwordHash, @nombre);
            SELECT LAST_INSERT_ID();
            """;
        cmd.Parameters.AddWithValue("@username", username);
        cmd.Parameters.AddWithValue("@passwordHash", passwordHash);
        cmd.Parameters.AddWithValue("@nombre", nombre);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task ActualizarUltimoLoginAsync(long usuarioId, DateTime loginAtUtc, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"UPDATE {DbNames.Usuario} SET last_login_at = @loginAt WHERE id = @id;";
        cmd.Parameters.AddWithValue("@loginAt", loginAtUtc);
        cmd.Parameters.AddWithValue("@id", usuarioId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task CambiarPasswordAsync(long usuarioId, string nuevoHash, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"UPDATE {DbNames.Usuario} SET password_hash = @hash WHERE id = @id;";
        cmd.Parameters.AddWithValue("@hash", nuevoHash);
        cmd.Parameters.AddWithValue("@id", usuarioId);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
