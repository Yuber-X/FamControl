using MySqlConnector;
using FAControl.Common;
using FAControl.Models;

namespace FAControl.Data;

/// <summary>Escritura del log de auditoría. La tabla es inmutable: solo INSERT y SELECT.</summary>
public class AuditoriaRepository
{
    private readonly ConexionFactory _factory;

    public AuditoriaRepository(ConexionFactory factory) => _factory = factory;

    public async Task InsertarAsync(Auditoria entrada, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        await InsertarAsync(entrada, conexion, transaccion: null, ct);
    }

    /// <summary>
    /// Variante para participar en una transacción existente (venta de préstamo,
    /// registro de pago, etc. — la auditoría entra en la MISMA transacción atómica).
    /// </summary>
    public async Task InsertarAsync(Auditoria entrada, MySqlConnection conexion,
        MySqlTransaction? transaccion, CancellationToken ct = default)
    {
        using var cmd = conexion.CreateCommand();
        cmd.Transaction = transaccion;
        cmd.CommandText = $"""
            INSERT INTO {DbNames.Auditoria} (usuario_id, modo, entidad, entidad_id, accion, descripcion, ip_local, timestamp)
            VALUES (@usuarioId, @modo, @entidad, @entidadId, @accion, @descripcion, @ipLocal, @timestamp);
            """;
        cmd.Parameters.AddWithValue("@usuarioId", entrada.UsuarioId);
        cmd.Parameters.AddWithValue("@modo", (object?)entrada.Modo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@entidad", entrada.Entidad);
        cmd.Parameters.AddWithValue("@entidadId", (object?)entrada.EntidadId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@accion", AccionADb(entrada.Accion));
        cmd.Parameters.AddWithValue("@descripcion", (object?)entrada.Descripcion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ipLocal", (object?)entrada.IpLocal ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@timestamp", entrada.TimestampUtc);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Visor del Historial: búsqueda con filtros opcionales, más reciente primero.</summary>
    public async Task<IReadOnlyList<Auditoria>> BuscarAsync(FiltroAuditoria filtro, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        // LEFT JOIN y no INNER: la auditoría es inmutable y debe sobrevivir
        // aunque el usuario se borre. Con INNER esas filas desaparecerían del
        // historial, que es justo lo que la auditoría existe para impedir.
        cmd.CommandText = $"""
            SELECT a.id, a.usuario_id, COALESCE(u.nombre, '(usuario eliminado)') AS usuario_nombre,
                   a.modo, a.entidad, a.entidad_id, a.accion, a.descripcion, a.ip_local, a.timestamp
            FROM {DbNames.Auditoria} a
            LEFT JOIN {DbNames.Usuario} u ON u.id = a.usuario_id
            WHERE (@desde IS NULL OR a.timestamp >= @desde)
              AND (@hasta IS NULL OR a.timestamp < @hasta)
              AND (@entidad IS NULL OR a.entidad = @entidad)
              AND (@accion IS NULL OR a.accion = @accion)
              AND (@usuarioId IS NULL OR a.usuario_id = @usuarioId)
              AND (@modo IS NULL OR a.modo = @modo)
            ORDER BY a.id DESC
            LIMIT @limite;
            """;
        // Los límites llegan en UTC (el llamador convierte el día de negocio RD)
        cmd.Parameters.AddWithValue("@desde", (object?)ADateTimeUtc(filtro.Desde) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@hasta", (object?)ADateTimeUtc(filtro.Hasta?.AddDays(1)) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@entidad", (object?)filtro.Entidad ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@accion", filtro.Accion is null ? DBNull.Value : AccionADb(filtro.Accion.Value));
        cmd.Parameters.AddWithValue("@usuarioId", (object?)filtro.UsuarioId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@modo", (object?)filtro.Modo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@limite", filtro.Limite);

        var entradas = new List<Auditoria>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            entradas.Add(new Auditoria
            {
                Id = reader.GetInt64("id"),
                UsuarioId = reader.GetInt64("usuario_id"),
                UsuarioNombre = reader.GetString("usuario_nombre"),
                Modo = reader.IsDBNull(reader.GetOrdinal("modo")) ? null : reader.GetString("modo"),
                Entidad = reader.GetString("entidad"),
                EntidadId = reader.IsDBNull(reader.GetOrdinal("entidad_id")) ? null : reader.GetInt64("entidad_id"),
                Accion = AccionDeDb(reader.GetString("accion")),
                Descripcion = reader.IsDBNull(reader.GetOrdinal("descripcion")) ? null : reader.GetString("descripcion"),
                IpLocal = reader.IsDBNull(reader.GetOrdinal("ip_local")) ? null : reader.GetString("ip_local"),
                TimestampUtc = DateTime.SpecifyKind(reader.GetDateTime("timestamp"), DateTimeKind.Utc)
            });
        }
        return entradas;
    }

    /// <summary>Fecha de negocio RD (UTC-4) → instante UTC del inicio de ese día.</summary>
    private static DateTime? ADateTimeUtc(DateOnly? fecha) =>
        fecha?.ToDateTime(TimeOnly.MinValue).AddHours(4);

    private static AccionAuditoria AccionDeDb(string valor) => valor switch
    {
        "crear" => AccionAuditoria.Crear,
        "modificar" => AccionAuditoria.Modificar,
        "eliminar" => AccionAuditoria.Eliminar,
        "consultar" => AccionAuditoria.Consultar,
        "login" => AccionAuditoria.Login,
        "logout" => AccionAuditoria.Logout,
        "anular" => AccionAuditoria.Anular,
        _ => throw new ArgumentOutOfRangeException(nameof(valor), valor, "Acción desconocida en BD.")
    };

    /// <summary>Mapea el enum C# al valor del ENUM MySQL.</summary>
    private static string AccionADb(AccionAuditoria accion) => accion switch
    {
        AccionAuditoria.Crear => "crear",
        AccionAuditoria.Modificar => "modificar",
        AccionAuditoria.Eliminar => "eliminar",
        AccionAuditoria.Consultar => "consultar",
        AccionAuditoria.Login => "login",
        AccionAuditoria.Logout => "logout",
        AccionAuditoria.Anular => "anular",
        _ => throw new ArgumentOutOfRangeException(nameof(accion))
    };
}
