using MySqlConnector;
using FAControl.Common;
using FAControl.Data;
using FAControl.Models;

namespace FAControl.Services;

/// <summary>
/// Punto único para registrar auditoría. TODA mutación (crear/modificar/eliminar)
/// de cliente, préstamo, cuota, pago o usuario pasa por aquí.
/// </summary>
public class AuditoriaService
{
    private readonly AuditoriaRepository _repositorio;
    private readonly SesionRepository _sesiones;
    private readonly UsuarioRepository _usuarios;

    public AuditoriaService(AuditoriaRepository repositorio, SesionRepository sesiones,
        UsuarioRepository usuarios)
    {
        _repositorio = repositorio;
        _sesiones = sesiones;
        _usuarios = usuarios;
    }

    /// <summary>Registra una entrada con conexión propia (operaciones simples).</summary>
    public Task RegistrarAsync(AccionAuditoria accion, string entidad, long? entidadId,
        string? descripcion, CancellationToken ct = default) =>
        _repositorio.InsertarAsync(Construir(accion, entidad, entidadId, descripcion), ct);

    /// <summary>
    /// Registra dentro de una transacción existente. Usar en operaciones multi-paso
    /// (crear préstamo, registrar pago): la auditoría entra en la MISMA transacción.
    /// </summary>
    public Task RegistrarEnTransaccionAsync(AccionAuditoria accion, string entidad, long? entidadId,
        string? descripcion, MySqlConnection conexion, MySqlTransaction transaccion,
        CancellationToken ct = default) =>
        _repositorio.InsertarAsync(Construir(accion, entidad, entidadId, descripcion), conexion, transaccion, ct);

    /// <summary>
    /// Igual que la anterior, pero para el PUNTO DE VENTA: su conexión apunta a
    /// `pos500_db` y la tabla de auditoría vive en la base de la suite. Se
    /// califica el nombre para escribir donde corresponde sin salirse de la
    /// transacción — el historial del cliente sigue siendo uno solo.
    /// </summary>
    public Task RegistrarEnTransaccionDeOtraBaseAsync(AccionAuditoria accion, string entidad,
        long? entidadId, string? descripcion, MySqlConnection conexion, MySqlTransaction transaccion,
        string esquemaAuditoria, CancellationToken ct = default) =>
        _repositorio.InsertarAsync(Construir(accion, entidad, entidadId, descripcion),
            conexion, transaccion, ct, esquemaAuditoria);

    /// <summary>Visor del Historial (solo lectura, con filtros).</summary>
    public Task<IReadOnlyList<Auditoria>> BuscarAsync(FiltroAuditoria filtro, CancellationToken ct = default) =>
        _repositorio.BuscarAsync(filtro, ct);

    /// <summary>
    /// Actividad por usuario en el mismo rango que el historial: sesiones,
    /// tiempo activo y operaciones (cliente 2026-07-16).
    /// </summary>
    public Task<IReadOnlyList<ActividadUsuario>> ObtenerActividadAsync(
        DateOnly? desde, DateOnly? hasta, CancellationToken ct = default) =>
        _sesiones.ObtenerActividadAsync(
            AInstanteUtc(desde), AInstanteUtc(hasta?.AddDays(1)), ct);

    /// <summary>Usuarios para el combo de filtro del Historial.</summary>
    public Task<IReadOnlyList<Usuario>> ObtenerUsuariosAsync(CancellationToken ct = default) =>
        _usuarios.ObtenerTodosAsync(incluirProgramadores: false, ct);

    /// <summary>
    /// Fecha de negocio RD (UTC-4) → instante UTC del inicio de ese día.
    /// Mismo criterio que usa AuditoriaRepository para filtrar: si no, la
    /// actividad y el listado hablarían de rangos distintos.
    /// </summary>
    private static DateTime? AInstanteUtc(DateOnly? fecha) =>
        fecha is null ? null : fecha.Value.ToDateTime(TimeOnly.MinValue).AddHours(4);

    private static Auditoria Construir(AccionAuditoria accion, string entidad, long? entidadId, string? descripcion)
    {
        if (!SesionActual.HaySesionActiva)
            throw new InvalidOperationException("No se puede auditar sin sesión activa.");

        return new Auditoria
        {
            UsuarioId = SesionActual.Id,
            Entidad = entidad,
            EntidadId = entidadId,
            Accion = accion,
            Descripcion = descripcion,
            TimestampUtc = DateTime.UtcNow
        };
    }
}
