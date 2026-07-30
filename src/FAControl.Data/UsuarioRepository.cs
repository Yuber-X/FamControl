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

    /// <summary>
    /// True si ya existe al menos un usuario del NEGOCIO (decide wizard inicial
    /// vs login).
    ///
    /// La cuenta de respaldo del desarrollador (rol Programador, sembrada al
    /// crear el esquema — 020) NO cuenta: el pedido del cliente 2026-07-29 es
    /// que la instalación siga pidiendo las credenciales del primer Admin "como
    /// si el user programador no existiera, pero sí estará".
    /// </summary>
    public async Task<bool> ExisteAlgunUsuarioAsync(CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT COUNT(*)
            {Desde}
            WHERE COALESCE(r.nombre, '') <> @programador;
            """;
        cmd.Parameters.AddWithValue("@programador", Roles.Programador);
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

    /// <summary>
    /// Busca por username SIN filtrar por activo. Solo para la RECUPERACIÓN DE
    /// ACCESO (código 3 del launcher): si la cuenta quedó desactivada, igual hay
    /// que poder devolverle el acceso al dueño. El login normal sigue usando
    /// <see cref="ObtenerPorUsernameAsync"/>, que exige activo = 1.
    /// </summary>
    public async Task<Usuario?> ObtenerPorUsernameCualquierEstadoAsync(string username,
        CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"SELECT {Columnas} {Desde} WHERE u.username = @username;";
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

    /// <summary>
    /// Todos los usuarios, activos e inactivos (la pantalla de Admin los muestra
    /// todos). Con <paramref name="incluirProgramadores"/> en false se OCULTAN las
    /// cuentas con rol Programador (017): el Admin no puede tocar lo que no ve.
    /// </summary>
    public async Task<IReadOnlyList<Usuario>> ObtenerTodosAsync(bool incluirProgramadores,
        CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = incluirProgramadores
            ? $"SELECT {Columnas} {Desde} ORDER BY u.activo DESC, u.nombre;"
            : $"""
               SELECT {Columnas} {Desde}
               WHERE COALESCE(r.nombre, '') <> @programador
               ORDER BY u.activo DESC, u.nombre;
               """;
        if (!incluirProgramadores)
            cmd.Parameters.AddWithValue("@programador", Roles.Programador);

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

    /// <summary>
    /// Permisos que otorga un ROL por defecto. Se usa para premarcar las
    /// casillas del formulario y para mostrar qué es "del rol" vs "adicional".
    /// </summary>
    public async Task<IReadOnlyList<string>> ObtenerPermisosDeRolAsync(int rolId, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT p.codigo
            FROM {DbNames.RolPermiso} rp
            JOIN {DbNames.Permiso} p ON p.id = rp.permiso_id
            WHERE rp.rol_id = @rolId;
            """;
        cmd.Parameters.AddWithValue("@rolId", rolId);

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
        cmd.CommandText = $"SELECT id, nombre, modo, descripcion FROM {DbNames.Rol} ORDER BY id;";

        var lista = new List<Rol>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            lista.Add(new Rol
            {
                Id = reader.GetInt32("id"),
                Nombre = reader.GetString("nombre"),
                Modo = reader.IsDBNull(reader.GetOrdinal("modo")) ? null : reader.GetString("modo"),
                Descripcion = reader.IsDBNull(reader.GetOrdinal("descripcion")) ? null : reader.GetString("descripcion")
            });
        return lista;
    }

    /// <summary>Roles POR MODO de un usuario (para el formulario de Usuarios).</summary>
    public async Task<RolesUsuario> ObtenerRolesDeUsuarioAsync(long usuarioId, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        int? prest = null, dealer = null, auto = null, pos = null;
        using (var cmd = conexion.CreateCommand())
        {
            cmd.CommandText = $"SELECT modo, rol_id FROM {DbNames.UsuarioModoRol} WHERE usuario_id = @id;";
            cmd.Parameters.AddWithValue("@id", usuarioId);
            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var rolId = reader.GetInt32("rol_id");
                switch (reader.GetString("modo"))
                {
                    case "prestcontrol": prest = rolId; break;
                    case "dealercontrol": dealer = rolId; break;
                    case "autocontrol": auto = rolId; break;
                    case "pos500": pos = rolId; break;
                }
            }
        }
        string rolGlobal;
        using (var cmd = conexion.CreateCommand())
        {
            cmd.CommandText = $"""
                SELECT COALESCE(r.nombre, '') FROM {DbNames.Usuario} u
                LEFT JOIN {DbNames.Rol} r ON r.id = u.rol_id
                WHERE u.id = @id;
                """;
            cmd.Parameters.AddWithValue("@id", usuarioId);
            rolGlobal = (await cmd.ExecuteScalarAsync(ct)) as string ?? string.Empty;
        }
        return new RolesUsuario(rolGlobal == Roles.Admin, prest, dealer, auto, pos,
            EsProgramador: rolGlobal == Roles.Programador);
    }

    /// <summary>Id del rol Programador (017), o null si la base no lo tiene todavía.</summary>
    public async Task<int?> ObtenerRolProgramadorIdAsync(CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"SELECT id FROM {DbNames.Rol} WHERE nombre = @rol AND modo IS NULL LIMIT 1;";
        cmd.Parameters.AddWithValue("@rol", Roles.Programador);
        var r = await cmd.ExecuteScalarAsync(ct);
        return r is null or DBNull ? null : Convert.ToInt32(r);
    }

    /// <summary>True si esa cuenta tiene el rol Programador (blindaje 017).</summary>
    public async Task<bool> EsProgramadorAsync(long usuarioId, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT COUNT(*) FROM {DbNames.Usuario} u JOIN {DbNames.Rol} r ON r.id = u.rol_id
            WHERE u.id = @id AND r.nombre = @rol;
            """;
        cmd.Parameters.AddWithValue("@id", usuarioId);
        cmd.Parameters.AddWithValue("@rol", Roles.Programador);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct)) > 0;
    }

    /// <summary>Nombre del rol del usuario en un modo (para mostrarlo en la sesión). Null si no tiene.</summary>
    public async Task<string?> ObtenerRolDeModoAsync(long usuarioId, string modo, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT r.nombre FROM {DbNames.UsuarioModoRol} umr
            JOIN {DbNames.Rol} r ON r.id = umr.rol_id
            WHERE umr.usuario_id = @id AND umr.modo = @modo;
            """;
        cmd.Parameters.AddWithValue("@id", usuarioId);
        cmd.Parameters.AddWithValue("@modo", modo);
        return (await cmd.ExecuteScalarAsync(ct)) as string;
    }

    /// <summary>Id del rol Admin (global). Para marcar/quitar la administración.</summary>
    public async Task<int?> ObtenerRolAdminIdAsync(CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"SELECT id FROM {DbNames.Rol} WHERE nombre = @admin AND modo IS NULL LIMIT 1;";
        cmd.Parameters.AddWithValue("@admin", Roles.Admin);
        var r = await cmd.ExecuteScalarAsync(ct);
        return r is null or DBNull ? null : Convert.ToInt32(r);
    }

    /// <summary>
    /// Guarda los roles por modo de un usuario de forma ATÓMICA y recomputa su
    /// usuario_permiso (la UNIÓN de los roles elegidos, o TODO si es Admin).
    /// El login sigue leyendo usuario_permiso sin cambios.
    /// </summary>
    public async Task GuardarRolesPorModoAsync(long usuarioId, RolesUsuario roles,
        int? rolAdminId, int? rolProgramadorId = null, CancellationToken ct = default)
    {
        // Los roles GLOBALES (Admin, Programador) no usan roles por modo: entran
        // a todo y su set de permisos es el catálogo completo.
        var esGlobal = roles.EsAdmin || roles.EsProgramador;

        using var conexion = await _factory.AbrirAsync(ct);
        using var tx = await conexion.BeginTransactionAsync(ct);
        try
        {
            // 1. rol_id global: Programador, Admin o NULL. (El trigger tocará
            //    usuario_permiso; lo recomputamos en el paso 3, así que su
            //    efecto se sobrescribe.)
            using (var cmd = conexion.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = $"UPDATE {DbNames.Usuario} SET rol_id = @rol, updated_at = UTC_TIMESTAMP() WHERE id = @id;";
                cmd.Parameters.AddWithValue("@rol", roles.EsProgramador
                    ? (object?)rolProgramadorId ?? DBNull.Value
                    : roles.EsAdmin ? (object?)rolAdminId ?? DBNull.Value : DBNull.Value);
                cmd.Parameters.AddWithValue("@id", usuarioId);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            // 2. usuario_modo_rol: reemplazar por los roles elegidos (no-admin)
            using (var cmd = conexion.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = $"DELETE FROM {DbNames.UsuarioModoRol} WHERE usuario_id = @id;";
                cmd.Parameters.AddWithValue("@id", usuarioId);
                await cmd.ExecuteNonQueryAsync(ct);
            }
            if (!esGlobal)
            {
                foreach (var (modo, rolId) in new[]
                    { ("prestcontrol", roles.RolPrestId), ("dealercontrol", roles.RolDealerId),
                      ("autocontrol", roles.RolAutoId), ("pos500", roles.RolPosId) })
                {
                    if (rolId is not { } rid) continue;
                    using var cmd = conexion.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = $"INSERT INTO {DbNames.UsuarioModoRol} (usuario_id, modo, rol_id) VALUES (@id, @modo, @rol);";
                    cmd.Parameters.AddWithValue("@id", usuarioId);
                    cmd.Parameters.AddWithValue("@modo", modo);
                    cmd.Parameters.AddWithValue("@rol", rid);
                    await cmd.ExecuteNonQueryAsync(ct);
                }
            }

            // 2.5 usuario_modo_permiso (013): el set por pantalla de cada modo.
            //     Con checkboxes de la UI se guardan tal cual; sin ellos, se
            //     materializan los del rol. acceso_<modo> va SIEMPRE que haya rol
            //     (la puerta de acceso no es un checkbox).
            using (var cmd = conexion.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = $"DELETE FROM {DbNames.UsuarioModoPermiso} WHERE usuario_id = @id;";
                cmd.Parameters.AddWithValue("@id", usuarioId);
                await cmd.ExecuteNonQueryAsync(ct);
            }
            if (!esGlobal)
            {
                foreach (var (modo, rolId) in new[]
                    { ("prestcontrol", roles.RolPrestId), ("dealercontrol", roles.RolDealerId),
                      ("autocontrol", roles.RolAutoId), ("pos500", roles.RolPosId) })
                {
                    if (rolId is not { } rid) continue;

                    if (roles.PermisosPorModo is { } sets && sets.TryGetValue(modo, out var marcados))
                    {
                        foreach (var permisoId in marcados.Distinct())
                        {
                            using var cmd = conexion.CreateCommand();
                            cmd.Transaction = tx;
                            cmd.CommandText = $"""
                                INSERT IGNORE INTO {DbNames.UsuarioModoPermiso} (usuario_id, modo, permiso_id)
                                VALUES (@id, @modo, @permiso);
                                """;
                            cmd.Parameters.AddWithValue("@id", usuarioId);
                            cmd.Parameters.AddWithValue("@modo", modo);
                            cmd.Parameters.AddWithValue("@permiso", permisoId);
                            await cmd.ExecuteNonQueryAsync(ct);
                        }
                    }
                    else
                    {
                        using var cmd = conexion.CreateCommand();
                        cmd.Transaction = tx;
                        cmd.CommandText = $"""
                            INSERT IGNORE INTO {DbNames.UsuarioModoPermiso} (usuario_id, modo, permiso_id)
                            SELECT @id, @modo, rp.permiso_id FROM {DbNames.RolPermiso} rp WHERE rp.rol_id = @rol;
                            """;
                        cmd.Parameters.AddWithValue("@id", usuarioId);
                        cmd.Parameters.AddWithValue("@modo", modo);
                        cmd.Parameters.AddWithValue("@rol", rid);
                        await cmd.ExecuteNonQueryAsync(ct);
                    }

                    // La puerta de acceso al modo va siempre que haya rol
                    using (var cmd = conexion.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = $"""
                            INSERT IGNORE INTO {DbNames.UsuarioModoPermiso} (usuario_id, modo, permiso_id)
                            SELECT @id, @modo, p.id FROM {DbNames.Permiso} p WHERE p.codigo = @acceso;
                            """;
                        cmd.Parameters.AddWithValue("@id", usuarioId);
                        cmd.Parameters.AddWithValue("@modo", modo);
                        cmd.Parameters.AddWithValue("@acceso", $"acceso_{modo}");
                        await cmd.ExecuteNonQueryAsync(ct);
                    }
                }
            }

            // 3. Recomputar usuario_permiso: TODO si Admin, si no la UNIÓN de los
            //    sets por modo (013; antes venía directo de los roles).
            using (var cmd = conexion.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = $"DELETE FROM {DbNames.UsuarioPermiso} WHERE usuario_id = @id;";
                cmd.Parameters.AddWithValue("@id", usuarioId);
                await cmd.ExecuteNonQueryAsync(ct);
            }
            using (var cmd = conexion.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = esGlobal
                    ? $"INSERT IGNORE INTO {DbNames.UsuarioPermiso} (usuario_id, permiso_id) SELECT @id, p.id FROM {DbNames.Permiso} p;"
                    : $"""
                        INSERT IGNORE INTO {DbNames.UsuarioPermiso} (usuario_id, permiso_id)
                        SELECT @id, ump.permiso_id
                        FROM {DbNames.UsuarioModoPermiso} ump
                        WHERE ump.usuario_id = @id;
                        """;
                cmd.Parameters.AddWithValue("@id", usuarioId);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>
    /// Catálogo de permisos DE UN MODO (013): la unión de lo que otorgan los
    /// roles de ese modo. Así los checkboxes de cada estancia nunca mezclan
    /// permisos de otra. Excluye acceso_<modo> (eso lo maneja el combo de rol).
    /// </summary>
    public async Task<IReadOnlyList<Permiso>> ObtenerCatalogoPermisosDeModoAsync(string modo,
        CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT DISTINCT p.id, p.codigo, p.nombre, p.descripcion
            FROM {DbNames.Permiso} p
            JOIN {DbNames.RolPermiso} rp ON rp.permiso_id = p.id
            JOIN {DbNames.Rol} r ON r.id = rp.rol_id
            WHERE r.modo = @modo AND p.codigo NOT LIKE 'acceso\_%'
            ORDER BY p.id;
            """;
        cmd.Parameters.AddWithValue("@modo", modo);

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

    /// <summary>Ids de permiso que otorga un rol (para precargar los checkboxes).</summary>
    public async Task<IReadOnlyList<int>> ObtenerPermisoIdsDeRolAsync(int rolId, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"SELECT permiso_id FROM {DbNames.RolPermiso} WHERE rol_id = @rol;";
        cmd.Parameters.AddWithValue("@rol", rolId);

        var lista = new List<int>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            lista.Add(reader.GetInt32("permiso_id"));
        return lista;
    }

    /// <summary>Set de permisos marcados de un usuario en un modo (013). Vacío = nunca se guardó.</summary>
    public async Task<IReadOnlyList<int>> ObtenerPermisosModoUsuarioAsync(long usuarioId, string modo,
        CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT permiso_id FROM {DbNames.UsuarioModoPermiso}
            WHERE usuario_id = @id AND modo = @modo;
            """;
        cmd.Parameters.AddWithValue("@id", usuarioId);
        cmd.Parameters.AddWithValue("@modo", modo);

        var lista = new List<int>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            lista.Add(reader.GetInt32("permiso_id"));
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
