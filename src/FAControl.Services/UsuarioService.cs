using FAControl.Common;
using FAControl.Data;
using FAControl.Models;
using Serilog;

namespace FAControl.Services;

/// <summary>
/// Administración de empleados. Reglas del cliente (2026-07-16):
///  - SOLO el Admin crea usuarios y cambia sus contraseñas;
///  - el Admin restablece contraseñas SIN saber la anterior (a diferencia del
///    cambio propio desde Configuración, que sí exige la actual);
///  - los permisos se siembran por ROL y el Admin los ajusta por casillas.
///
/// Salvaguardas propias (no pedidas, pero el negocio se rompe sin ellas):
/// un Admin no puede autodesactivarse, ni quitarse su propio rol de Admin,
/// ni dejar al negocio sin ningún Admin activo.
/// </summary>
public class UsuarioService
{
    public const int MinLargoPassword = 8;
    private const int CostBcrypt = 12;

    private readonly UsuarioRepository _usuarios;
    private readonly AuditoriaService _auditoria;

    public UsuarioService(UsuarioRepository usuarios, AuditoriaService auditoria)
    {
        _usuarios = usuarios;
        _auditoria = auditoria;
    }

    /// <summary>
    /// Puerta única: toda operación de esta clase exige ser Admin.
    /// La UI ya oculta la pantalla, pero la regla se aplica AQUÍ — la UI no
    /// es una frontera de seguridad.
    /// </summary>
    private static void ExigirAdmin()
    {
        if (!SesionActual.EsAdmin)
            throw new UnauthorizedAccessException(
                "Solo un administrador puede gestionar usuarios.");
    }

    /// <summary>
    /// BLINDAJE DEL PROGRAMADOR (017, cliente 2026-07-27): ningún Admin puede
    /// tocar una cuenta con rol Programador — ni editarla, ni desactivarla, ni
    /// restablecerle la contraseña, ni verla. Solo otro Programador.
    /// La cuenta tampoco aparece en la lista, así que esto es la segunda puerta:
    /// aunque alguien llegue con el id en la mano, aquí se corta.
    /// </summary>
    private async Task ExigirPuedeTocarAsync(long usuarioId, CancellationToken ct)
    {
        if (SesionActual.EsProgramador)
            return;
        if (await _usuarios.EsProgramadorAsync(usuarioId, ct))
            throw new UnauthorizedAccessException(
                "Esa cuenta está reservada al desarrollador del sistema y no se puede modificar.");
    }

    /// <summary>Solo un Programador puede otorgar o quitar el rol Programador.</summary>
    private static void ExigirPuedeAsignarProgramador(RolesUsuario roles)
    {
        if (roles.EsProgramador && !SesionActual.EsProgramador)
            throw new UnauthorizedAccessException(
                "El rol Programador solo lo puede asignar otro Programador.");
    }

    public async Task<IReadOnlyList<Usuario>> ObtenerTodosAsync(CancellationToken ct = default)
    {
        ExigirAdmin();
        // El Admin no ve las cuentas del desarrollador (017)
        return await _usuarios.ObtenerTodosAsync(SesionActual.EsProgramador, ct);
    }

    public async Task<IReadOnlyList<Rol>> ObtenerRolesAsync(CancellationToken ct = default)
    {
        ExigirAdmin();
        var roles = await _usuarios.ObtenerRolesAsync(ct);
        // El rol Programador no se ofrece a nadie más (017)
        return SesionActual.EsProgramador
            ? roles
            : [.. roles.Where(r => r.Nombre != Roles.Programador)];
    }

    public async Task<IReadOnlyList<Permiso>> ObtenerCatalogoPermisosAsync(CancellationToken ct = default)
    {
        ExigirAdmin();
        return await _usuarios.ObtenerCatalogoPermisosAsync(ct);
    }

    // ---------- Permisos por pantalla (013, cliente 2026-07-25) ----------

    /// <summary>Catálogo de permisos de un modo (para los checkboxes). No mezcla modos.</summary>
    public async Task<IReadOnlyList<Permiso>> ObtenerCatalogoPermisosDeModoAsync(string modo,
        CancellationToken ct = default)
    {
        ExigirAdmin();
        return await _usuarios.ObtenerCatalogoPermisosDeModoAsync(modo, ct);
    }

    /// <summary>Permisos que otorga un rol (precarga de los checkboxes al elegirlo).</summary>
    public async Task<IReadOnlyList<int>> ObtenerPermisoIdsDeRolAsync(int rolId, CancellationToken ct = default)
    {
        ExigirAdmin();
        return await _usuarios.ObtenerPermisoIdsDeRolAsync(rolId, ct);
    }

    /// <summary>Set marcado de un usuario en un modo (vacío si nunca se guardó con 013).</summary>
    public async Task<IReadOnlyList<int>> ObtenerPermisosModoUsuarioAsync(long usuarioId, string modo,
        CancellationToken ct = default)
    {
        ExigirAdmin();
        await ExigirPuedeTocarAsync(usuarioId, ct);
        return await _usuarios.ObtenerPermisosModoUsuarioAsync(usuarioId, modo, ct);
    }

    public async Task<IReadOnlyList<string>> ObtenerPermisosAsync(long usuarioId, CancellationToken ct = default)
    {
        ExigirAdmin();
        await ExigirPuedeTocarAsync(usuarioId, ct);
        return await _usuarios.ObtenerPermisosAsync(usuarioId, ct);
    }

    /// <summary>Roles POR MODO de un usuario (para precargar el formulario).</summary>
    public async Task<RolesUsuario> ObtenerRolesDeUsuarioAsync(long usuarioId, CancellationToken ct = default)
    {
        ExigirAdmin();
        await ExigirPuedeTocarAsync(usuarioId, ct);
        return await _usuarios.ObtenerRolesDeUsuarioAsync(usuarioId, ct);
    }

    /// <summary>
    /// Crea un empleado y le asigna sus roles POR MODO (o Admin global).
    /// Los permisos efectivos se recomputan como la unión de esos roles.
    /// </summary>
    public async Task<long> CrearAsync(string username, string nombre, string? apellido,
        RolesUsuario roles, string password, CancellationToken ct = default)
    {
        ExigirAdmin();
        ExigirPuedeAsignarProgramador(roles);
        ValidarDatos(username, nombre);
        ValidarPassword(password);
        ValidarTieneAlgunAcceso(roles);

        if (await _usuarios.ObtenerPorUsernameAsync(username.Trim(), ct) is not null)
            throw new InvalidOperationException($"Ya existe un usuario con el username '{username.Trim()}'.");

        var rolAdminId = await _usuarios.ObtenerRolAdminIdAsync(ct);
        var rolProgramadorId = await _usuarios.ObtenerRolProgramadorIdAsync(ct);
        var hash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: CostBcrypt);
        var id = await _usuarios.CrearAsync(username.Trim(), hash, nombre.Trim(),
            string.IsNullOrWhiteSpace(apellido) ? null : apellido.Trim(),
            roles.EsProgramador ? rolProgramadorId : roles.EsAdmin ? rolAdminId : null, ct);

        await _usuarios.GuardarRolesPorModoAsync(id, roles, rolAdminId, rolProgramadorId, ct);

        await _auditoria.RegistrarAsync(AccionAuditoria.Crear, DbNames.Usuario, id,
            $"Usuario {username.Trim()} creado ({DescribirRoles(roles)})", ct);
        Log.Information("Usuario {Username} creado por {Admin}", username.Trim(), SesionActual.Username);
        return id;
    }

    /// <summary>Actualiza datos y roles por modo. Recomputa la unión de permisos.</summary>
    public async Task ActualizarAsync(long id, string nombre, string? apellido,
        RolesUsuario roles, bool activo, CancellationToken ct = default)
    {
        ExigirAdmin();
        ExigirPuedeAsignarProgramador(roles);
        await ExigirPuedeTocarAsync(id, ct);
        if (string.IsNullOrWhiteSpace(nombre))
            throw new ArgumentException("El nombre es obligatorio.");
        ValidarTieneAlgunAcceso(roles);

        var antes = await _usuarios.ObtenerPorIdAsync(id, ct)
            ?? throw new InvalidOperationException("El usuario no existe.");
        var eraAdmin = antes.RolNombre == Roles.Admin;

        // Un Admin no puede autodesactivarse ni quitarse la administración.
        if (id == SesionActual.Id && !activo)
            throw new InvalidOperationException("No puedes desactivar tu propia cuenta.");
        if (id == SesionActual.Id && eraAdmin && !roles.EsAdmin)
            throw new InvalidOperationException("No puedes quitarte a ti mismo el rol de Admin.");

        // El negocio nunca puede quedarse sin un Admin activo.
        var perderiaAdmin = eraAdmin && antes.Activo && (!roles.EsAdmin || !activo);
        if (perderiaAdmin && await _usuarios.ContarAdminsActivosAsync(ct) <= 1)
            throw new InvalidOperationException(
                "Es el único Admin activo. Asigna otro Admin antes de cambiar este.");

        // Datos básicos (el rol_id lo fija GuardarRolesPorModo); activo va aquí.
        await _usuarios.ActualizarAsync(id, nombre.Trim(),
            string.IsNullOrWhiteSpace(apellido) ? null : apellido.Trim(), antes.RolId, activo, ct);
        var rolAdminId = await _usuarios.ObtenerRolAdminIdAsync(ct);
        var rolProgramadorId = await _usuarios.ObtenerRolProgramadorIdAsync(ct);
        await _usuarios.GuardarRolesPorModoAsync(id, roles, rolAdminId, rolProgramadorId, ct);

        await _auditoria.RegistrarAsync(AccionAuditoria.Modificar, DbNames.Usuario, id,
            $"Usuario {antes.Username}: {DescribirRoles(roles)}, activo {antes.Activo} → {activo}", ct);
    }

    private static void ValidarTieneAlgunAcceso(RolesUsuario r)
    {
        if (!r.EsAdmin && !r.EsProgramador
            && r.RolPrestId is null && r.RolDealerId is null && r.RolAutoId is null)
            throw new ArgumentException("Asigna al menos un rol (en algún modo) o marca administrador.");
    }

    private static string DescribirRoles(RolesUsuario r) => r.EsProgramador
        ? "Programador"
        : r.EsAdmin
            ? "Administrador"
            : $"Prest={r.RolPrestId?.ToString() ?? "—"}, Dealer={r.RolDealerId?.ToString() ?? "—"}, Auto={r.RolAutoId?.ToString() ?? "—"}";

    /// <summary>
    /// El Admin restablece la contraseña de un empleado SIN conocer la anterior
    /// (pedido explícito del cliente). El cambio de la propia contraseña sigue
    /// en AuthService y sí exige la actual.
    /// </summary>
    public async Task RestablecerPasswordAsync(long usuarioId, string passwordNueva,
        CancellationToken ct = default)
    {
        ExigirAdmin();
        await ExigirPuedeTocarAsync(usuarioId, ct);
        ValidarPassword(passwordNueva);

        var usuario = await _usuarios.ObtenerPorIdAsync(usuarioId, ct)
            ?? throw new InvalidOperationException("El usuario no existe.");

        var hash = BCrypt.Net.BCrypt.HashPassword(passwordNueva, workFactor: CostBcrypt);
        await _usuarios.CambiarPasswordAsync(usuarioId, hash, ct);

        // La contraseña JAMAS se escribe en la auditoria ni en el log.
        await _auditoria.RegistrarAsync(AccionAuditoria.Modificar, DbNames.Usuario, usuarioId,
            $"Contraseña de {usuario.Username} restablecida por {SesionActual.Username}", ct);
        Log.Information("Contraseña de {Username} restablecida por {Admin}",
            usuario.Username, SesionActual.Username);
    }

    private static void ValidarDatos(string username, string nombre)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("El nombre de usuario es obligatorio.");
        if (string.IsNullOrWhiteSpace(nombre))
            throw new ArgumentException("El nombre es obligatorio.");
    }

    private static void ValidarPassword(string password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinLargoPassword)
            throw new ArgumentException($"La contraseña debe tener al menos {MinLargoPassword} caracteres.");
    }
}
