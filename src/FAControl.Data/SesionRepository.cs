using MySqlConnector;
using FAControl.Common;
using FAControl.Models;

namespace FAControl.Data;

/// <summary>Registro de logins/logouts en la tabla sesion.</summary>
public class SesionRepository
{
    private readonly ConexionFactory _factory;

    public SesionRepository(ConexionFactory factory) => _factory = factory;

    public async Task<long> RegistrarLoginAsync(long usuarioId, DateTime loginAtUtc, string? ipLocal, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {DbNames.Sesion} (usuario_id, login_at, ip_local)
            VALUES (@usuarioId, @loginAt, @ipLocal);
            SELECT LAST_INSERT_ID();
            """;
        cmd.Parameters.AddWithValue("@usuarioId", usuarioId);
        cmd.Parameters.AddWithValue("@loginAt", loginAtUtc);
        cmd.Parameters.AddWithValue("@ipLocal", (object?)ipLocal ?? DBNull.Value);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task RegistrarLogoutAsync(long sesionId, DateTime logoutAtUtc, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"UPDATE {DbNames.Sesion} SET logout_at = @logoutAt WHERE id = @id AND logout_at IS NULL;";
        cmd.Parameters.AddWithValue("@logoutAt", logoutAtUtc);
        cmd.Parameters.AddWithValue("@id", sesionId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Actividad por usuario en el rango: sesiones, tiempo activo y operaciones.
    /// Pedido del cliente 2026-07-16 ("agregar los usuarios en el historial y
    /// su tiempo activo").
    ///
    /// Reglas:
    ///  - una sesión aún abierta (logout_at NULL) cuenta hasta AHORA: es el
    ///    turno en curso, y ese usuario aparece "en línea";
    ///  - se suma en SQL, no en C#: son pocas filas pero la lógica de tiempo
    ///    debe estar donde están los datos;
    ///  - los usuarios sin sesiones en el rango no aparecen (no hay nada que
    ///    contar de ellos).
    /// </summary>
    public async Task<IReadOnlyList<ActividadUsuario>> ObtenerActividadAsync(
        DateTime? desdeUtc, DateTime? hastaUtc, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT u.id,
                   u.nombre,
                   COALESCE(r.nombre, '(sin rol)') AS rol_nombre,
                   COUNT(s.id) AS sesiones,
                   COALESCE(SUM(TIMESTAMPDIFF(SECOND, s.login_at,
                            COALESCE(s.logout_at, UTC_TIMESTAMP()))), 0) AS segundos,
                   MAX(s.login_at) AS ultimo_acceso,
                   MAX(s.logout_at IS NULL) AS en_linea,
                   (SELECT COUNT(*) FROM {DbNames.Auditoria} a
                     WHERE a.usuario_id = u.id
                       AND (@desde IS NULL OR a.timestamp >= @desde)
                       AND (@hasta IS NULL OR a.timestamp < @hasta)) AS operaciones
            FROM {DbNames.Sesion} s
            JOIN {DbNames.Usuario} u ON u.id = s.usuario_id
            LEFT JOIN {DbNames.Rol} r ON r.id = u.rol_id
            WHERE (@desde IS NULL OR s.login_at >= @desde)
              AND (@hasta IS NULL OR s.login_at < @hasta)
            GROUP BY u.id, u.nombre, r.nombre
            ORDER BY segundos DESC;
            """;
        cmd.Parameters.AddWithValue("@desde", (object?)desdeUtc ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@hasta", (object?)hastaUtc ?? DBNull.Value);

        var lista = new List<ActividadUsuario>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            lista.Add(new ActividadUsuario(
                UsuarioId: reader.GetInt64("id"),
                Nombre: reader.GetString("nombre"),
                RolNombre: reader.GetString("rol_nombre"),
                Sesiones: Convert.ToInt32(reader["sesiones"]),
                TiempoActivoSegundos: Convert.ToInt32(reader["segundos"]),
                Operaciones: Convert.ToInt32(reader["operaciones"]),
                UltimoAccesoUtc: reader.IsDBNull(reader.GetOrdinal("ultimo_acceso"))
                    ? null
                    : DateTime.SpecifyKind(reader.GetDateTime("ultimo_acceso"), DateTimeKind.Utc),
                EnLinea: Convert.ToBoolean(reader["en_linea"])));
        }
        return lista;
    }
}
