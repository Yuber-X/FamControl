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
    /// La UI ya oculta la pantalla, pero la regla se aplica ACÁ — la UI no
    /// es una frontera de seguridad.
    /// </summary>
    private static void ExigirAdmin()
    {
        if (!SesionActual.EsAdmin)
            throw new UnauthorizedAccessException(
                "Solo un administrador puede gestionar usuarios.");
    }

    public async Task<IReadOnlyList<Usuario>> ObtenerTodosAsync(CancellationToken ct = default)
    {
        ExigirAdmin();
        return await _usuarios.ObtenerTodosAsync(ct);
    }

    public async Task<IReadOnlyList<Rol>> ObtenerRolesAsync(CancellationToken ct = default)
    {
        ExigirAdmin();
        return await _usuarios.ObtenerRolesAsync(ct);
    }

    public async Task<IReadOnlyList<Permiso>> ObtenerCatalogoPermisosAsync(CancellationToken ct = default)
    {
        ExigirAdmin();
        return await _usuarios.ObtenerCatalogoPermisosAsync(ct);
    }

    public async Task<IReadOnlyList<string>> ObtenerPermisosAsync(long usuarioId, CancellationToken ct = default)
    {
        ExigirAdmin();
        return await _usuarios.ObtenerPermisosAsync(usuarioId, ct);
    }

    /// <summary>Crea un empleado. El trigger le siembra los permisos del rol.</summary>
    public async Task<long> CrearAsync(string username, string nombre, string? apellido,
        int rolId, string password, CancellationToken ct = default)
    {
        ExigirAdmin();
        ValidarDatos(username, nombre);
        ValidarPassword(password);

        if (await _usuarios.ObtenerPorUsernameAsync(username.Trim(), ct) is not null)
            throw new InvalidOperationException($"Ya existe un usuario con el username '{username.Trim()}'.");

        var hash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: CostBcrypt);
        var id = await _usuarios.CrearAsync(username.Trim(), hash, nombre.Trim(),
            string.IsNullOrWhiteSpace(apellido) ? null : apellido.Trim(), rolId, ct);

        var roles = await _usuarios.ObtenerRolesAsync(ct);
        var rol = roles.FirstOrDefault(r => r.Id == rolId)?.Nombre ?? "sin rol";
        await _auditoria.RegistrarAsync(AccionAuditoria.Crear, DbNames.Usuario, id,
            $"Usuario {username.Trim()} creado con rol {rol}", ct);
        Log.Information("Usuario {Username} creado con rol {Rol} por {Admin}",
            username.Trim(), rol, SesionActual.Username);
        return id;
    }

    /// <summary>Actualiza datos y rol. Cambiar el rol RESIEMBRA los permisos (vía trigger).</summary>
    public async Task ActualizarAsync(long id, string nombre, string? apellido, int rolId,
        bool activo, CancellationToken ct = default)
    {
        ExigirAdmin();
        if (string.IsNullOrWhiteSpace(nombre))
            throw new ArgumentException("El nombre es obligatorio.");

        var antes = await _usuarios.ObtenerPorIdAsync(id, ct)
            ?? throw new InvalidOperationException("El usuario no existe.");

        var roles = await _usuarios.ObtenerRolesAsync(ct);
        var rolNuevo = roles.FirstOrDefault(r => r.Id == rolId)
            ?? throw new InvalidOperationException("El rol indicado no existe.");

        // Un Admin no puede autodesactivarse ni degradarse: se quedaría fuera
        // de su propia aplicación en caliente.
        if (id == SesionActual.Id && !activo)
            throw new InvalidOperationException("No podés desactivar tu propia cuenta.");
        if (id == SesionActual.Id && antes.RolNombre == Roles.Admin && rolNuevo.Nombre != Roles.Admin)
            throw new InvalidOperationException("No podés quitarte a vos mismo el rol de Admin.");

        // El negocio nunca puede quedarse sin un Admin activo.
        var perderiaAdmin = antes.RolNombre == Roles.Admin && antes.Activo
                            && (rolNuevo.Nombre != Roles.Admin || !activo);
        if (perderiaAdmin && await _usuarios.ContarAdminsActivosAsync(ct) <= 1)
            throw new InvalidOperationException(
                "Es el único Admin activo. Asigná otro Admin antes de cambiar este.");

        await _usuarios.ActualizarAsync(id, nombre.Trim(),
            string.IsNullOrWhiteSpace(apellido) ? null : apellido.Trim(), rolId, activo, ct);

        await _auditoria.RegistrarAsync(AccionAuditoria.Modificar, DbNames.Usuario, id,
            $"Usuario {antes.Username}: rol {antes.RolNombre} → {rolNuevo.Nombre}, " +
            $"activo {antes.Activo} → {activo}", ct);
    }

    /// <summary>
    /// El Admin restablece la contraseña de un empleado SIN conocer la anterior
    /// (pedido explícito del cliente). El cambio de la propia contraseña sigue
    /// en AuthService y sí exige la actual.
    /// </summary>
    public async Task RestablecerPasswordAsync(long usuarioId, string passwordNueva,
        CancellationToken ct = default)
    {
        ExigirAdmin();
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

    /// <summary>Overrides: ajusta los permisos de un usuario sin cambiarle el rol.</summary>
    public async Task GuardarPermisosAsync(long usuarioId, IEnumerable<string> codigos,
        CancellationToken ct = default)
    {
        ExigirAdmin();
        var usuario = await _usuarios.ObtenerPorIdAsync(usuarioId, ct)
            ?? throw new InvalidOperationException("El usuario no existe.");

        var lista = codigos.Distinct().ToList();

        // Un Admin no puede quitarse a si mismo la administracion de usuarios:
        // se quedaria sin poder volver a entrar a esta pantalla.
        if (usuarioId == SesionActual.Id &&
            (!lista.Contains(Permisos.Usuarios) || !lista.Contains(Permisos.Configuracion)))
            throw new InvalidOperationException(
                "No podés quitarte a vos mismo los permisos de Usuarios o Configuración.");

        await _usuarios.ReemplazarPermisosAsync(usuarioId, lista, ct);
        await _auditoria.RegistrarAsync(AccionAuditoria.Modificar, DbNames.Usuario, usuarioId,
            $"Permisos de {usuario.Username}: {string.Join(", ", lista.OrderBy(c => c))}", ct);
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
