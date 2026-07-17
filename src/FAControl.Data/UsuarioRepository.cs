using MySqlConnector;
using FAControl.Common;
using FAControl.Models;

namespace FAControl.Data;

/// <summary>
/// Acceso a usuarios, roles y permisos (multicuentas — cliente 2026-07-16).
/// Los permisos EFECTIVOS viven en usuario_permiso; los triggers los siembran
/// desde rol_permiso al crear el usuario o cambiarle el rol, y el Admin puede
/// ajustarlos uno por uno sin tocar el rol.
/// </summary>
public class UsuarioRepository
{
    private readonly ConexionFactory _factory;

    public UsuarioRepository(ConexionFactory factory) => _factory = factory;

    private const string Columnas = """
        u.id, u.username, u.password_hash, u.nombre, u.apellido, u.rol_id,
        COALESCE(r.nombre, '') AS rol_nombre, u.activo, u.created_at,
        u.updated_at, u.last_login_at
        """;

    private const string Desde = $"""
        FROM {DbNames.Usuario} u
        LEFT JOIN {DbNames.Rol} r ON r.id = u.rol_id
        """;

    /// <summary>True si ya existe al menos un usuario (decide wizard inicial vs login).</summary>
    public async Task<bool> ExisteAlgunUsuarioAsync(CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {DbNames.Usuario};";
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct)) > 0;
    }

    public async Task<Usuario?> ObtenerPorUsernameAsync(string username, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"SELECT {Columnas} {Desde} WHERE u.username = @username AND u.activo = 1;";
        cmd.Parameters.AddWithValue("@username", username);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Mapear(reader) : null;
    }

    public async Task<Usuario?> ObtenerPorIdAsync(long id, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"SELECT {Columnas} {Desde} WHERE u.id = @id;";
        cmd.Parameters.AddWithValue("@id", id);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Mapear(reader) : null;
    }

    /// <summary>Todos los usuarios, activos e inactivos (la pantalla de Admin los muestra todos).</summary>
    public async Task<IReadOnlyList<Usuario>> ObtenerTodosAsync(CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"SELECT {Columnas} {Desde} ORDER BY u.activo DESC, u.nombre;";

        var lista = new List<Usuario>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            lista.Add(Mapear(reader));
        return lista;
    }

    /// <summary>Permisos EFECTIVOS del usuario (códigos), ya con los overrides aplicados.</summary>
    public async Task<IReadOnlyList<string>> ObtenerPermisosAsync(long usuarioId, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT p.codigo
            FROM {DbNames.UsuarioPermiso} up
            JOIN {DbNames.Permiso} p ON p.id = up.permiso_id
            WHERE up.usuario_id = @usuarioId;
            """;
        cmd.Parameters.AddWithValue("@usuarioId", usuarioId);

        var lista = new List<string>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            lista.Add(reader.GetString("codigo"));
        return lista;
    }

    public async Task<IReadOnlyList<Rol>> ObtenerRolesAsync(CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"SELECT id, nombre, descripcion FROM {DbNames.Rol} ORDER BY id;";

        var lista = new List<Rol>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            lista.Add(new Rol
            {
                Id = reader.GetInt32("id"),
                Nombre = reader.GetString("nombre"),
                Descripcion = reader.IsDBNull(reader.GetOrdinal("descripcion")) ? null : reader.GetString("descripcion")
            });
        return lista;
    }

    public async Task<IReadOnlyList<Permiso>> ObtenerCatalogoPermisosAsync(CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"SELECT id, codigo, nombre, descripcion FROM {DbNames.Permiso} ORDER BY id;";

        var lista = new List<Permiso>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            lista.Add(new Permiso
            {
                Id = reader.GetInt32("id"),
                Codigo = reader.GetString("codigo"),
                Nombre = reader.GetString("nombre"),
                Descripcion = reader.IsDBNull(reader.GetOrdinal("descripcion")) ? null : reader.GetString("descripcion")
            });
        return lista;
    }

    /// <summary>Crea el usuario. El trigger le siembra los permisos del rol.</summary>
    public async Task<long> CrearAsync(string username, string passwordHash, string nombre,
        string? apellido, int? rolId, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {DbNames.Usuario} (username, password_hash, nombre, apellido, rol_id)
            VALUES (@username, @passwordHash, @nombre, @apellido, @rolId);
            SELECT LAST_INSERT_ID();
            """;
        cmd.Parameters.AddWithValue("@username", username);
        cmd.Parameters.AddWithValue("@passwordHash", passwordHash);
        cmd.Parameters.AddWithValue("@nombre", nombre);
        cmd.Parameters.AddWithValue("@apellido", (object?)apellido ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@rolId", (object?)rolId ?? DBNull.Value);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
    }

    /// <summary>
    /// Actualiza datos y rol. Si el rol cambia, el trigger RESIEMBRA los permisos
    /// (los overrides previos se pierden a propósito: el rol manda).
    /// </summary>
    public async Task ActualizarAsync(long id, string nombre, string? apellido, int? rolId,
        bool activo, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {DbNames.Usuario}
            SET nombre = @nombre, apellido = @apellido, rol_id = @rolId,
                activo = @activo, updated_at = UTC_TIMESTAMP()
            WHERE id = @id;
            """;
        cmd.Parameters.AddWithValue("@nombre", nombre);
        cmd.Parameters.AddWithValue("@apellido", (object?)apellido ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@rolId", (object?)rolId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@activo", activo);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
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
        cmd.CommandText = $"""
            UPDATE {DbNames.Usuario}
            SET password_hash = @hash, updated_at = UTC_TIMESTAMP()
            WHERE id = @id;
            """;
        cmd.Parameters.AddWithValue("@hash", nuevoHash);
        cmd.Parameters.AddWithValue("@id", usuarioId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Reemplaza los permisos efectivos del usuario (overrides del Admin).
    /// Atómico: si falla a media lista, el usuario no queda sin permisos.
    /// </summary>
    public async Task ReemplazarPermisosAsync(long usuarioId, IEnumerable<string> codigos,
        CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var transaccion = await conexion.BeginTransactionAsync(ct);
        try
        {
            using (var borrar = conexion.CreateCommand())
            {
                borrar.Transaction = transaccion;
                borrar.CommandText = $"DELETE FROM {DbNames.UsuarioPermiso} WHERE usuario_id = @id;";
                borrar.Parameters.AddWithValue("@id", usuarioId);
                await borrar.ExecuteNonQueryAsync(ct);
            }

            foreach (var codigo in codigos.Distinct())
            {
                using var insertar = conexion.CreateCommand();
                insertar.Transaction = transaccion;
                insertar.CommandText = $"""
                    INSERT IGNORE INTO {DbNames.UsuarioPermiso} (usuario_id, permiso_id)
                    SELECT @id, p.id FROM {DbNames.Permiso} p WHERE p.codigo = @codigo;
                    """;
                insertar.Parameters.AddWithValue("@id", usuarioId);
                insertar.Parameters.AddWithValue("@codigo", codigo);
                await insertar.ExecuteNonQueryAsync(ct);
            }

            await transaccion.CommitAsync(ct);
        }
        catch
        {
            await transaccion.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>Cuántos Admin ACTIVOS quedan. Evita que el negocio se quede sin Admin.</summary>
    public async Task<int> ContarAdminsActivosAsync(CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT COUNT(*)
            FROM {DbNames.Usuario} u
            JOIN {DbNames.Rol} r ON r.id = u.rol_id
            WHERE r.nombre = @admin AND u.activo = 1;
            """;
        cmd.Parameters.AddWithValue("@admin", Roles.Admin);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    private static Usuario Mapear(MySqlDataReader reader) => new()
    {
        Id = reader.GetInt64("id"),
        Username = reader.GetString("username"),
        PasswordHash = reader.GetString("password_hash"),
        Nombre = reader.GetString("nombre"),
        Apellido = reader.IsDBNull(reader.GetOrdinal("apellido")) ? null : reader.GetString("apellido"),
        RolId = reader.IsDBNull(reader.GetOrdinal("rol_id")) ? null : reader.GetInt32("rol_id"),
        RolNombre = reader.GetString("rol_nombre"),
        Activo = reader.GetBoolean("activo"),
        CreatedAtUtc = DateTime.SpecifyKind(reader.GetDateTime("created_at"), DateTimeKind.Utc),
        UpdatedAtUtc = reader.IsDBNull(reader.GetOrdinal("updated_at"))
            ? null : DateTime.SpecifyKind(reader.GetDateTime("updated_at"), DateTimeKind.Utc),
        LastLoginAtUtc = reader.IsDBNull(reader.GetOrdinal("last_login_at"))
            ? null : DateTime.SpecifyKind(reader.GetDateTime("last_login_at"), DateTimeKind.Utc)
    };
}
