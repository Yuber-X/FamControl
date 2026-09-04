using FAControl.Common;
using FAControl.Data;
using Serilog;

namespace FAControl.Services;

/// <summary>
/// Las dos operaciones destructivas del launcher (pedido del cliente
/// 2026-07-29), las que se disparan con los códigos 6 y 7:
///
///   * CÓDIGO 6 — respaldar y limpiar todo: saca un .sql y deja la base vacía.
///   * CÓDIGO 7 — eliminar todo: borra base, expedientes, ajustes y licencia,
///     SIN respaldo. Es para retirar la instalación.
///
/// Corren SIN sesión abierta a propósito: existen para cuando nadie puede
/// entrar. Por eso NO pasan por los servicios que exigen Admin, y por eso están
/// detrás de un código que solo tiene el desarrollador.
/// Todo queda en el log de Serilog: no hay auditoría en BD porque no hay usuario
/// autenticado a quien atribuirle la operación (y además la tabla de auditoría
/// se borra junto con el resto).
///
/// La recuperación de contraseñas ya NO vive aquí: desde el 2026-07-29 esa puerta
/// es la cuenta de respaldo del desarrollador que se siembra con el esquema
/// (scripts/db/020_usuario_programador.sql). Dos puertas traseras para lo mismo
/// era una de más.
/// </summary>
public class RecuperacionService
{
    private readonly VerificadorBaseDatos _verificador;
    private readonly RespaldoService _respaldos;
    private readonly AjustesLocales _ajustes;

    public RecuperacionService(VerificadorBaseDatos verificador, RespaldoService respaldos,
        AjustesLocales ajustes)
    {
        _verificador = verificador;
        _respaldos = respaldos;
        _ajustes = ajustes;
    }

    /// <summary>
    /// CÓDIGO 6 — "hacer respaldo y limpiar todo". ⚠️ BORRA TODOS LOS DATOS.
    /// Antes de borrar SIEMPRE saca un respaldo .sql a la carpeta que se indique:
    /// si el respaldo falla, la operación se aborta y no se borra nada.
    /// Al terminar, la base queda como recién instalada (con el catálogo de
    /// roles y permisos), lista para el wizard de cuenta inicial.
    /// </summary>
    /// <returns>Ruta del respaldo que quedó guardado.</returns>
    public async Task<string> RespaldarYLimpiarAsync(string carpetaRespaldo, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(carpetaRespaldo))
            throw new ArgumentException("Elige dónde guardar el respaldo antes de limpiar.");

        var archivo = Path.Combine(carpetaRespaldo,
            $"FAControl_antes_de_limpiar_{DateTime.Now:yyyy-MM-dd_HHmm}.sql");

        // Si esto falla, la excepción sube y NO se llega a borrar nada
        await _respaldos.RespaldarAsync(archivo, ct);
        Log.Warning("LIMPIAR TODO: respaldo previo guardado en {Archivo}", archivo);

        // Los expedientes viven en disco, no en la base: si no se copian, el .sql
        // deja los contratos escaneados huérfanos.
        var expedientes = ExpedienteService.RespaldarTodoEnZip(
            ExpedienteService.CarpetaRaiz(_ajustes), carpetaRespaldo);
        if (expedientes is not null)
            Log.Warning("LIMPIAR TODO: expedientes respaldados en {Archivo}", expedientes);

        await _verificador.BorrarYRecrearAsync(ct);
        Log.Warning("LIMPIAR TODO: base de datos borrada y recreada vacía");

        return archivo;
    }

    /// <summary>
    /// CÓDIGO 7 — "eliminar todo". ⚠️ SIN RESPALDO Y SIN VUELTA ATRÁS.
    /// Borra la base (no la recrea), la carpeta de expedientes, los ajustes de
    /// la PC y la licencia. Es la operación para retirar FAControl de un equipo.
    ///
    /// La marca de inicio de prueba del registro de Windows se deja a propósito:
    /// eliminar todo no puede servir para estirar los 14 días de prueba.
    /// </summary>
    /// <returns>Resumen de lo que se borró, para mostrarlo al usuario.</returns>
    public async Task<string> EliminarTodoAsync(CancellationToken ct = default)
    {
        var hecho = new List<string>();

        await _verificador.BorrarAsync(ct);
        hecho.Add("Base de datos borrada");
        Log.Warning("ELIMINAR TODO: base de datos borrada sin respaldo");

        var expedientes = ExpedienteService.CarpetaRaiz(_ajustes);
        if (Directory.Exists(expedientes))
        {
            Directory.Delete(expedientes, recursive: true);
            hecho.Add("Expedientes borrados");
            Log.Warning("ELIMINAR TODO: carpeta de expedientes {Carpeta} borrada", expedientes);
        }

        AjustesLocales.Borrar();
        hecho.Add("Ajustes de esta PC borrados");

        LicenciaLocal.Borrar();
        hecho.Add("Licencia borrada");
        Log.Warning("ELIMINAR TODO: ajustes y licencia borrados — instalación retirada");

        return string.Join(" · ", hecho);
    }
}
