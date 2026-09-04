using FAControl.Common;
using FAControl.Data;
using FAControl.Models;
using Serilog;

namespace FAControl.Services;

/// <summary>
/// Comprobante fiscal (NCF) — pedido del cliente 2026-07-25. La empresa está
/// legalizada ante la DGII y usa su versión gratuita (Facturador Gratuito),
/// así que hay DOS caminos y los dos se soportan:
///  * REGISTRAR: pegar el e-NCF generado por fuera (Facturador Gratuito).
///  * ASIGNAR: tomar el siguiente de la secuencia local autorizada
///    (reserva atómica FOR UPDATE; ver docs/NCF-DGII.md).
/// Un NCF puesto en un préstamo NO se cambia desde aquí (regla DGII: el
/// comprobante emitido es irreversible; una corrección es asunto del contador).
/// </summary>
public class NcfService
{
    private readonly ConexionFactory _factory;
    private readonly NcfRepository _ncf;
    private readonly PrestamoRepository _prestamos;
    private readonly AuditoriaService _auditoria;

    public NcfService(ConexionFactory factory, NcfRepository ncf,
        PrestamoRepository prestamos, AuditoriaService auditoria)
    {
        _factory = factory;
        _ncf = ncf;
        _prestamos = prestamos;
        _auditoria = auditoria;
    }

    /// <summary>
    /// La secuencia de LA ESTANCIA ACTIVA (030). Cada modo lleva la suya: un
    /// negocio de varios rubros puede tener una autorización de la DGII por
    /// cada uno, o hasta otro RNC. Null = esa estancia todavía no configuró
    /// ninguna, que es como arrancan todas.
    /// </summary>
    public Task<NcfSecuencia?> ObtenerSecuenciaAsync(CancellationToken ct = default) =>
        _ncf.ObtenerActivaAsync(SesionActual.Modo, ct);

    /// <summary>
    /// El próximo comprobante que entregaría la secuencia, ya formateado
    /// (ej. "B0200000046"), para mostrarlo como marcador en las cajas de NCF
    /// (pedido del cliente 2026-09-03).
    ///
    /// Null cuando NO hay que mostrar nada: esta estancia no configuró
    /// secuencia, está apagada, venció o se agotó. Un marcador con un número
    /// que la app no va a poder entregar sería peor que ninguno.
    /// </summary>
    public async Task<string?> ProximoNcfAsync(CancellationToken ct = default)
    {
        try
        {
            var secuencia = await ObtenerSecuenciaAsync(ct);
            if (secuencia is null || !secuencia.Activo)
                return null;
            if (secuencia.EstaVencida(FechaNegocio.Hoy) || secuencia.EstaAgotada)
                return null;
            return secuencia.Formatear(secuencia.Proxima);
        }
        catch (Exception ex)
        {
            // Un marcador es una ayuda visual: si falla, la pantalla sigue.
            Log.Warning(ex, "No se pudo calcular el próximo NCF para el marcador");
            return null;
        }
    }

    /// <summary>
    /// Adopta como predeterminado el comprobante que se acaba de usar a mano
    /// (pedido del cliente 2026-09-03). Se llama DESPUÉS de que la operación
    /// commiteó.
    ///
    /// NUNCA propaga: el cobro o el préstamo ya están guardados y son válidos.
    /// Que no se haya podido mover la secuencia es un detalle de comodidad —
    /// hacerlo estallar aquí le mostraría un error al cajero por una operación
    /// que en realidad salió bien.
    /// </summary>
    public async Task AdoptarComoPredeterminadaAsync(string? ncfUsado, CancellationToken ct = default)
    {
        await NcfPredeterminado.AdoptarAsync(_ncf, SesionActual.Modo, ncfUsado, ct);
    }

    /// <summary>Guarda la configuración de la secuencia (solo Admin) + auditoría.</summary>
    public async Task GuardarSecuenciaAsync(NcfSecuencia secuencia, CancellationToken ct = default)
    {
        if (!SesionActual.EsAdmin)
            throw new UnauthorizedAccessException("Solo un administrador puede configurar la secuencia de comprobantes.");
        var prefijo = secuencia.Prefijo?.Trim().ToUpperInvariant() ?? string.Empty;
        if (prefijo.Length < 3)
            throw new ArgumentException("El prefijo del comprobante debe tener al menos 3 caracteres (ej. B02 o E32).");
        secuencia.Prefijo = prefijo;
        if (secuencia.Largo is < 6 or > 12)
            throw new ArgumentException("El largo de la secuencia debe estar entre 6 y 12 dígitos (8 tradicional, 10 e-CF).");
        if (secuencia.Proxima < 1)
            throw new ArgumentException("La próxima secuencia debe ser 1 o mayor.");
        if (secuencia.FinRango is { } fin && fin < secuencia.Proxima)
            throw new ArgumentException("El fin del rango no puede ser menor que la próxima secuencia.");

        // Se guarda contra la estancia activa: la pantalla de Configuración
        // siempre edita la secuencia del modo en el que se está trabajando.
        await _ncf.GuardarAsync(SesionActual.Modo, secuencia, ct);
        await _auditoria.RegistrarAsync(AccionAuditoria.Modificar, DbNames.NcfSecuencia, null,
            $"Secuencia NCF {secuencia.Prefijo} de {IdentidadModo.De(SesionActual.Modo).Nombre}: " +
            $"próxima {secuencia.Proxima}" +
            (secuencia.FinRango is { } f ? $", fin {f}" : "") +
            (secuencia.Vencimiento is { } v ? $", vence {v:dd/MM/yyyy}" : "") +
            (secuencia.Activo ? "" : " — DESACTIVADA"), ct);
        Log.Information("Secuencia NCF {Prefijo} guardada (próxima {Proxima})",
            secuencia.Prefijo, secuencia.Proxima);
    }

    /// <summary>
    /// Pone comprobante a un préstamo que NO tiene: pegado (manual) o de la
    /// secuencia (auto). Atómico: reserva + update + auditoría en una transacción.
    /// </summary>
    public async Task<string> AsignarAsync(long prestamoId, string? ncfManual = null,
        CancellationToken ct = default)
    {
        if (!SesionActual.TienePermiso(Permisos.PrestamosCrear))
            throw new UnauthorizedAccessException("No tienes permiso para asignar comprobantes fiscales.");

        var prestamo = await _prestamos.ObtenerPorIdAsync(prestamoId, ct)
            ?? throw new InvalidOperationException($"No existe el préstamo con id {prestamoId}.");
        if (!string.IsNullOrWhiteSpace(prestamo.Ncf))
            throw new InvalidOperationException(
                $"El préstamo {prestamo.Codigo} ya tiene el comprobante {prestamo.Ncf}. " +
                "Un comprobante emitido no se cambia (si hay un error, consultalo con el contador).");

        var manual = ncfManual?.Trim().ToUpperInvariant();

        using var conexion = await _factory.AbrirAsync(ct);
        using var transaccion = await conexion.BeginTransactionAsync(ct);
        try
        {
            var ncf = string.IsNullOrWhiteSpace(manual)
                ? await _ncf.ReservarSiguienteAsync(SesionActual.Modo, conexion, transaccion, FechaNegocio.Hoy, ct)
                : manual;

            await _prestamos.ActualizarNcfAsync(prestamoId, ncf, conexion, transaccion, ct);
            await _auditoria.RegistrarEnTransaccionAsync(AccionAuditoria.Modificar, DbNames.Prestamo,
                prestamoId,
                $"Comprobante fiscal {ncf} asignado al préstamo {prestamo.Codigo}" +
                (string.IsNullOrWhiteSpace(manual) ? " (de la secuencia)" : " (registrado externo)"),
                conexion, transaccion, ct);
            await transaccion.CommitAsync(ct);

            Log.Information("NCF {Ncf} asignado al préstamo {Codigo}", ncf, prestamo.Codigo);
            if (!string.IsNullOrWhiteSpace(manual))
                await AdoptarComoPredeterminadaAsync(ncf, ct);
            return ncf;
        }
        catch
        {
            await transaccion.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
