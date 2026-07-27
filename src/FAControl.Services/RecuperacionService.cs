using FAControl.Common;
using FAControl.Data;
using FAControl.Models;
using Serilog;

namespace FAControl.Services;

/// <summary>
/// Las dos operaciones de emergencia del launcher (pedido del cliente
/// 2026-07-27), las que se disparan con los códigos 3 y 4.
///
/// Corren SIN sesión abierta a propósito: existen justamente para cuando nadie
/// puede entrar. Por eso NO pasan por UsuarioService (que exige Admin) y por eso
/// están detrás de un código que solo tiene el desarrollador.
/// Todo queda en el log de Serilog: no hay auditoría en BD porque no hay usuario
/// autenticado a quien atribuirle la operación (y en el código 4 la tabla de
/// auditoría se borra junto con el resto).
/// </summary>
public class RecuperacionService
{
    private const int CostBcrypt = 12;

    private readonly UsuarioRepository _usuarios;
    private readonly VerificadorBaseDatos _verificador;
    private readonly RespaldoService _respaldos;

    public RecuperacionService(UsuarioRepository usuarios, VerificadorBaseDatos verificador,
        RespaldoService respaldos)
    {
        _usuarios = usuarios;
        _verificador = verificador;
        _respaldos = respaldos;
    }

    /// <summary>
    /// CÓDIGO 3 — "el cliente perdió todas las contraseñas y no quiere perder
    /// datos". Devuelve el acceso sin tocar un solo dato del negocio:
    ///  * si el usuario existe → se le pone la contraseña nueva, se reactiva y
    ///    se le asegura el rol pedido;
    ///  * si no existe → se crea.
    /// Con <paramref name="comoProgramador"/> la cuenta queda con el rol
    /// Programador (017): es la única vía para crear esa cuenta, y por eso el
    /// Admin no puede fabricarse una.
    /// </summary>
    public async Task<string> RestablecerAccesoAsync(string username, string passwordNueva,
        bool comoProgramador, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("Escribí el nombre de usuario a recuperar.");
        if (string.IsNullOrEmpty(passwordNueva) || passwordNueva.Length < AuthService.MinLargoPassword)
            throw new ArgumentException(
                $"La contraseña debe tener al menos {AuthService.MinLargoPassword} caracteres.");

        var rolId = comoProgramador
            ? await _usuarios.ObtenerRolProgramadorIdAsync(ct)
              ?? throw new InvalidOperationException(
                  "Esta base todavía no tiene el rol Programador. Aplicá scripts/db/017_rol_programador.sql.")
            : await _usuarios.ObtenerRolAdminIdAsync(ct)
              ?? throw new InvalidOperationException(
                  "Esta base no tiene el rol Admin. El esquema está incompleto.");

        var limpio = username.Trim();
        var hash = BCrypt.Net.BCrypt.HashPassword(passwordNueva, workFactor: CostBcrypt);
        var existente = await _usuarios.ObtenerPorUsernameCualquierEstadoAsync(limpio, ct);

        if (existente is null)
        {
            var id = await _usuarios.CrearAsync(limpio, hash, limpio, null, rolId, ct);
            // El trigger le siembra los permisos del rol; la unión se recalcula igual
            await _usuarios.GuardarRolesPorModoAsync(id,
                new RolesUsuario(!comoProgramador, null, null, null, null, comoProgramador),
                comoProgramador ? null : rolId, comoProgramador ? rolId : null, ct);

            Log.Warning("RECUPERACIÓN: cuenta {Username} CREADA con rol {Rol} desde el launcher",
                limpio, comoProgramador ? Roles.Programador : Roles.Admin);
            return $"Se creó la cuenta '{limpio}' con acceso total. Ya podés iniciar sesión.";
        }

        await _usuarios.CambiarPasswordAsync(existente.Id, hash, ct);
        await _usuarios.ActualizarAsync(existente.Id, existente.Nombre, existente.Apellido,
            rolId, activo: true, ct);
        await _usuarios.GuardarRolesPorModoAsync(existente.Id,
            new RolesUsuario(!comoProgramador, null, null, null, null, comoProgramador),
            comoProgramador ? null : rolId, comoProgramador ? rolId : null, ct);

        Log.Warning("RECUPERACIÓN: contraseña de {Username} restablecida desde el launcher", limpio);
        return $"Listo: la cuenta '{limpio}' quedó con la contraseña nueva y acceso total. " +
               "Ningún dato del negocio se tocó.";
    }

    /// <summary>
    /// CÓDIGO 4 — "restablecer todo desde el inicio". ⚠️ BORRA TODOS LOS DATOS.
    /// Antes de borrar SIEMPRE saca un respaldo .sql a la carpeta que se indique:
    /// si el respaldo falla, la operación se aborta y no se borra nada.
    /// Al terminar, la base queda como recién instalada (con el catálogo de
    /// roles y permisos), lista para el wizard de cuenta inicial.
    /// </summary>
    /// <returns>Ruta del respaldo que quedó guardado.</returns>
    public async Task<string> RestablecerTodoAsync(string carpetaRespaldo, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(carpetaRespaldo))
            throw new ArgumentException("Elegí dónde guardar el respaldo antes de restablecer.");

        var archivo = Path.Combine(carpetaRespaldo,
            $"FAControl_antes_de_restablecer_{DateTime.Now:yyyy-MM-dd_HHmm}.sql");

        // Si esto falla, la excepción sube y NO se llega a borrar nada
        await _respaldos.RespaldarAsync(archivo, ct);
        Log.Warning("RESTABLECER TODO: respaldo previo guardado en {Archivo}", archivo);

        await _verificador.BorrarYRecrearAsync(ct);
        Log.Warning("RESTABLECER TODO: base de datos borrada y recreada vacía");

        return archivo;
    }
}
