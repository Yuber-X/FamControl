using FAControl.Common;
using FAControl.Data;
using FAControl.Models;

namespace FAControl.Services;

/// <summary>Resultado de un intento de login.</summary>
public enum ResultadoLogin
{
    Exitoso,
    CredencialesInvalidas,
    BloqueadoTemporalmente,
    /// <summary>Credenciales válidas, pero el usuario no tiene acceso a ESE modo.</summary>
    SinAccesoAlModo
}

/// <summary>
/// Autenticación MULTIUSUARIO con BCrypt (cost 12) y rate-limiting:
/// 5 intentos fallidos → bloqueo temporal de 5 minutos.
/// El login carga rol y permisos efectivos en SesionActual.
/// Sigue sin haber 2FA ni recuperación por correo (por diseño).
///
/// El wizard de primer arranque crea al PRIMER Admin; a partir de ahí
/// los empleados los crea el Admin desde la pantalla de Usuarios
/// (UsuarioService), nunca este wizard.
/// </summary>
public class AuthService
{
    public const int MinLargoPassword = 8;
    private const int CostBcrypt = 12;
    private const int MaxIntentosFallidos = 5;
    private static readonly TimeSpan DuracionBloqueo = TimeSpan.FromMinutes(5);

    private readonly UsuarioRepository _usuarios;
    private readonly SesionRepository _sesiones;
    private readonly AuditoriaService _auditoria;

    private int _intentosFallidos;
    private DateTime? _bloqueadoHastaUtc;

    public AuthService(UsuarioRepository usuarios, SesionRepository sesiones, AuditoriaService auditoria)
    {
        _usuarios = usuarios;
        _sesiones = sesiones;
        _auditoria = auditoria;
    }

    /// <summary>True si aún no existe la cuenta inicial (primer arranque → wizard).</summary>
    public async Task<bool> RequiereCuentaInicialAsync(CancellationToken ct = default) =>
        !await _usuarios.ExisteAlgunUsuarioAsync(ct);

    /// <summary>
    /// Crea el PRIMER Admin desde el wizard de primer arranque.
    /// Solo corre cuando la tabla usuario está vacía: los demás empleados
    /// los crea el Admin desde Usuarios (regla del cliente 2026-07-16).
    /// </summary>
    public async Task<long> CrearCuentaInicialAsync(string username, string nombre, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("El nombre de usuario es obligatorio.");
        if (string.IsNullOrWhiteSpace(nombre))
            throw new ArgumentException("El nombre es obligatorio.");
        ValidarPassword(password);

        if (await _usuarios.ExisteAlgunUsuarioAsync(ct))
            throw new InvalidOperationException(
                "Ya existe una cuenta. Los empleados nuevos los crea un Admin desde la pantalla de Usuarios.");

        // El primer usuario SIEMPRE es Admin: si no, nadie podría administrar nada.
        var roles = await _usuarios.ObtenerRolesAsync(ct);
        var admin = roles.FirstOrDefault(r => r.Nombre == Roles.Admin)
            ?? throw new InvalidOperationException(
                "El catálogo de roles está vacío. Ejecutá scripts/db/005_multicuentas.sql.");

        var hash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: CostBcrypt);
        return await _usuarios.CrearAsync(username.Trim(), hash, nombre.Trim(), null, admin.Id, ct);
    }

    /// <summary>
    /// Valida credenciales para ENTRAR a un modo concreto y, si todo pasa:
    /// registra la sesión en BD, actualiza last_login_at, inicializa
    /// SesionActual (con el modo) y audita el login.
    ///
    /// La puerta de acceso por modo (cliente 2026-07-18) se aplica ACÁ: aunque
    /// la contraseña sea correcta, si el usuario no tiene acceso_&lt;modo&gt; no se
    /// abre sesión (no cuenta como intento fallido: la clave sí era válida).
    /// </summary>
    public async Task<ResultadoLogin> LoginAsync(string username, string password,
        ModoApp modo, CancellationToken ct = default)
    {
        if (_bloqueadoHastaUtc is { } bloqueo && DateTime.UtcNow < bloqueo)
            return ResultadoLogin.BloqueadoTemporalmente;

        var usuario = await _usuarios.ObtenerPorUsernameAsync(username.Trim(), ct);
        if (usuario is null || !BCrypt.Net.BCrypt.Verify(password, usuario.PasswordHash))
        {
            RegistrarIntentoFallido();
            return ResultadoLogin.CredencialesInvalidas;
        }

        _intentosFallidos = 0;
        _bloqueadoHastaUtc = null;

        // Permisos EFECTIVOS (rol + overrides): se leen acá para decidir el
        // acceso al modo y luego viven en SesionActual toda la sesión.
        var permisos = await _usuarios.ObtenerPermisosAsync(usuario.Id, ct);

        // Puerta de acceso por modo: el Admin entra a todo; los demás solo si el
        // Admin les habilitó acceso_<modo>. Sin acceso NO se abre sesión.
        var esAdmin = usuario.RolNombre == Roles.Admin;
        if (!esAdmin && !permisos.Contains(Permisos.AccesoDe(modo)))
            return ResultadoLogin.SinAccesoAlModo;

        var ahoraUtc = DateTime.UtcNow;
        var sesionId = await _sesiones.RegistrarLoginAsync(usuario.Id, ahoraUtc, ipLocal: null, ct);
        await _usuarios.ActualizarUltimoLoginAsync(usuario.Id, ahoraUtc, ct);

        SesionActual.Iniciar(usuario.Id, usuario.Username, usuario.Nombre,
            usuario.RolNombre, permisos, ahoraUtc, sesionId);
        SesionActual.EstablecerModo(modo);
        await _auditoria.RegistrarAsync(AccionAuditoria.Login, DbNames.Usuario, usuario.Id,
            $"Login de {usuario.Username} ({usuario.RolNombre}) en {modo}", ct);

        return ResultadoLogin.Exitoso;
    }

    /// <summary>
    /// Verifica credenciales SIN abrir sesión ni tocar SesionActual.
    /// Lo usa la autorización de préstamos: un Admin pone su contraseña para
    /// aprobar la operación de un cobrador, pero NO se cambia el usuario activo.
    /// Devuelve el usuario autorizante, o null si las credenciales no valen.
    /// </summary>
    public async Task<Usuario?> VerificarCredencialesAsync(string username, string password,
        CancellationToken ct = default)
    {
        var usuario = await _usuarios.ObtenerPorUsernameAsync(username.Trim(), ct);
        if (usuario is null || !BCrypt.Net.BCrypt.Verify(password, usuario.PasswordHash))
            return null;
        return usuario;
    }

    /// <summary>Cierra la sesión: registra logout en BD, audita y limpia SesionActual.</summary>
    public async Task LogoutAsync(CancellationToken ct = default)
    {
        if (!SesionActual.HaySesionActiva)
            return;

        await _auditoria.RegistrarAsync(AccionAuditoria.Logout, DbNames.Usuario, SesionActual.Id,
            $"Logout de {SesionActual.Username}", ct);
        await _sesiones.RegistrarLogoutAsync(SesionActual.SesionId, DateTime.UtcNow, ct);
        SesionActual.Cerrar();
    }

    /// <summary>Cambio de contraseña desde Configuración: exige la contraseña actual.</summary>
    public async Task CambiarPasswordAsync(string passwordActual, string passwordNueva, CancellationToken ct = default)
    {
        if (!SesionActual.HaySesionActiva)
            throw new InvalidOperationException("No hay sesión activa.");
        ValidarPassword(passwordNueva);

        var usuario = await _usuarios.ObtenerPorUsernameAsync(SesionActual.Username, ct)
            ?? throw new InvalidOperationException("Usuario no encontrado.");

        if (!BCrypt.Net.BCrypt.Verify(passwordActual, usuario.PasswordHash))
            throw new InvalidOperationException("La contraseña actual no es correcta.");

        var nuevoHash = BCrypt.Net.BCrypt.HashPassword(passwordNueva, workFactor: CostBcrypt);
        await _usuarios.CambiarPasswordAsync(usuario.Id, nuevoHash, ct);
        await _auditoria.RegistrarAsync(AccionAuditoria.Modificar, DbNames.Usuario, usuario.Id,
            "Cambio de contraseña", ct);
    }

    private static void ValidarPassword(string password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinLargoPassword)
            throw new ArgumentException($"La contraseña debe tener al menos {MinLargoPassword} caracteres.");
    }

    private void RegistrarIntentoFallido()
    {
        _intentosFallidos++;
        if (_intentosFallidos >= MaxIntentosFallidos)
        {
            _bloqueadoHastaUtc = DateTime.UtcNow.Add(DuracionBloqueo);
            _intentosFallidos = 0;
        }
    }
}
