using FAControl.Common;
using FAControl.Data;
using Serilog;

namespace FAControl.Services;

/// <summary>
/// Adopción del comprobante digitado a mano como secuencia predeterminada
/// (pedido del cliente 2026-09-03): "si se digita un NCF en cobros o en un
/// préstamo y la operación sale bien, ese mismo NCF se toma como el
/// predeterminado y se agrega en Configuración para continuar la secuencia".
///
/// Vive aquí y no dentro de cada servicio porque lo llaman cinco caminos
/// distintos —préstamo nuevo, cobro de cuota, cobro de alquiler, abono a plazo
/// y "Asignar" en el detalle del préstamo— y todos necesitan exactamente la
/// misma garantía: <b>no romper la operación que ya se guardó</b>.
///
/// Por eso se llama SIEMPRE después del commit y jamás propaga. El cobro es un
/// hecho financiero; mover la numeración predeterminada es una comodidad. Si lo
/// segundo falla, lo primero sigue siendo válido y el usuario no tiene por qué
/// ver un error.
/// </summary>
internal static class NcfPredeterminado
{
    public static async Task AdoptarAsync(NcfRepository ncf, ModoApp modo, string? ncfUsado,
        CancellationToken ct = default)
    {
        try
        {
            if (await ncf.AdoptarComoPredeterminadaAsync(modo, ncfUsado, ct))
                Log.Information("Secuencia NCF de {Modo} adoptada desde el comprobante {Ncf}",
                    modo, ncfUsado);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "No se pudo adoptar {Ncf} como secuencia predeterminada de {Modo}",
                ncfUsado, modo);
        }
    }
}
