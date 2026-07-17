using FAControl.Common;
using FAControl.Data;
using Serilog;

namespace FAControl.Services;

/// <summary>
/// Prueba de que alguien con permiso aprobó la operación.
///
/// El constructor es INTERNAL a propósito: solo AutorizacionService puede
/// emitir una. Así un ViewModel no puede fabricarse una autorización falsa
/// para saltarse la regla — tiene que pasar por la validación real.
/// </summary>
public record AutorizacionPrestamo
{
    public long UsuarioId { get; }
    public string Username { get; }
    public string Nombre { get; }

    internal AutorizacionPrestamo(long usuarioId, string username, string nombre)
    {
        UsuarioId = usuarioId;
        Username = username;
        Nombre = nombre;
    }
}

/// <summary>
/// Pide la autorización al usuario (muestra el login del administrador).
/// La implementa la capa App: ViewModels no puede abrir ventanas.
/// </summary>
public interface IAutorizadorAdmin
{
    /// <summary>Devuelve la autorización, o null si se canceló.</summary>
    Task<AutorizacionPrestamo?> PedirAsync(string motivo, CancellationToken ct = default);
}

/// <summary>
/// Autorización de operaciones sensibles (cliente 2026-07-16): "solo los admins
/// deben de dar autorizacion positiva de los prestamos nuevos... cuando un
/// usuario cobrador crea el prestamo este muestra el login que permitira
/// progresar si el admin coloca su contraseña".
/// </summary>
public class AutorizacionService
{
    private readonly AuthService _auth;
    private readonly UsuarioRepository _usuarios;

    public AutorizacionService(AuthService auth, UsuarioRepository usuarios)
    {
        _auth = auth;
        _usuarios = usuarios;
    }

    /// <summary>
    /// True si quien está trabajando ya puede autorizar por sí mismo: en ese
    /// caso no tiene sentido pedirle su propia contraseña.
    /// </summary>
    public static bool UsuarioActualPuedeAutorizar =>
        SesionActual.TienePermiso(Permisos.PrestamosAutorizar);

    /// <summary>
    /// Valida las credenciales del autorizador. Devuelve null si no son
    /// correctas O si ese usuario no tiene permiso para autorizar: un
    /// Supervisor con contraseña válida NO alcanza.
    ///
    /// No abre sesión ni toca SesionActual: el cobrador sigue siendo el
    /// usuario activo, el admin solo aprueba y se va.
    /// </summary>
    public async Task<AutorizacionPrestamo?> ValidarAsync(string username, string password,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            return null;

        var usuario = await _auth.VerificarCredencialesAsync(username, password, ct);
        if (usuario is null)
        {
            Log.Warning("Autorización rechazada: credenciales inválidas para {Username}", username);
            return null;
        }

        var permisos = await _usuarios.ObtenerPermisosAsync(usuario.Id, ct);
        if (!permisos.Contains(Permisos.PrestamosAutorizar))
        {
            Log.Warning("Autorización rechazada: {Username} no tiene permiso para autorizar", username);
            return null;
        }

        Log.Information("Préstamo autorizado por {Username}", usuario.Username);
        return new AutorizacionPrestamo(usuario.Id, usuario.Username, usuario.NombreCompleto);
    }

    /// <summary>
    /// Autorización implícita cuando el propio usuario ya tiene el permiso.
    /// Internal: solo los Services la usan, nunca un ViewModel.
    /// </summary>
    internal static AutorizacionPrestamo DelUsuarioActual() =>
        new(SesionActual.Id, SesionActual.Username, SesionActual.Nombre);
}
