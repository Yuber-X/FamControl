using System.IO;
using FAControl.Models;
using FAControl.Printing;
using Serilog;

namespace FAControl.Views;

/// <summary>
/// Imprime un contrato y deja la copia en el expediente del préstamo (pedido
/// del cliente 2026-09-03: "los docs imprimidos deben de guardarse en contratos
/// al cliente correspondiente").
///
/// Vive suelto y no dentro de una ventana porque hay DOS caminos que imprimen:
/// la ventana de contratos, donde el usuario mira y manda a imprimir uno; y
/// Nuevo Préstamo, que al guardar manda a imprimir de una los que estén
/// tildados, sin abrir vista previa. Los dos tienen que archivar igual.
/// </summary>
public static class ImpresionDeContratos
{
    /// <summary>
    /// Manda el contrato a la impresora y archiva la copia.
    ///
    /// La impresión propaga si falla —el usuario necesita enterarse de que no
    /// salió el papel—, pero el archivado NO: si el PDF de respaldo no se pudo
    /// guardar, el contrato ya está impreso y firmado, que es lo que importa.
    /// El fallo queda en el log.
    /// </summary>
    public static void ImprimirYArchivar(TipoContrato tipo, PagareNotarialImpreso contrato,
        DuenoExpediente? expedienteDe, ViewModels.ExpedienteViewModel? expediente)
    {
        var descripcion = ContratoDocumentFactory.Descripcion(tipo, contrato.Deuda.CodigoPrestamo);
        ImpresoraRecibos.ImprimirDocumento(ContratoDocumentFactory.Crear(tipo, contrato), descripcion);
        _ = ArchivarAsync(tipo, contrato, expedienteDe, expediente);
    }

    /// <summary>
    /// Archiva una copia en PDF sin imprimir. La usa el guardado de Nuevo
    /// Préstamo cuando el contrato no está tildado para imprimir pero igual
    /// conviene que quede el respaldo.
    /// </summary>
    public static Task ArchivarAsync(TipoContrato tipo, PagareNotarialImpreso contrato,
        DuenoExpediente? expedienteDe, ViewModels.ExpedienteViewModel? expediente)
    {
        if (expedienteDe is not { } dueno || expediente is null)
            return Task.CompletedTask;
        return GuardarEnExpedienteAsync(tipo, contrato, dueno, expediente);
    }

    private static async Task GuardarEnExpedienteAsync(TipoContrato tipo,
        PagareNotarialImpreso contrato, DuenoExpediente dueno,
        ViewModels.ExpedienteViewModel expediente)
    {
        // El expediente guarda ARCHIVOS, no documentos de WPF, así que primero
        // hay que materializar el PDF. La marca de tiempo evita que reimprimir
        // el mismo contrato pise la copia anterior: las dos son válidas y puede
        // importar cuál se firmó.
        var nombre = TiposDeContrato.NombreArchivo(tipo, contrato.Deuda.CodigoPrestamo);
        var temporal = Path.Combine(Path.GetTempPath(),
            $"{nombre}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");
        try
        {
            ImpresoraRecibos.GuardarDocumentoPdf(
                ContratoDocumentFactory.Crear(tipo, contrato), temporal,
                ContratoDocumentFactory.Descripcion(tipo, contrato.Deuda.CodigoPrestamo));

            // Los tres son contratos a los ojos del expediente. El pagaré
            // conserva su propio tipo porque las fichas viejas ya lo usan y
            // cambiarlo dejaría los papeles anteriores clasificados distinto.
            var tipoDocumento = tipo == TipoContrato.Pagare
                ? TipoDocumento.Pagare
                : TipoDocumento.Contrato;

            await expediente.ArchivarImpresoAsync(dueno, temporal, tipoDocumento);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "No se pudo archivar el contrato {Tipo} de {Dueno}",
                tipo, dueno.Descripcion);
        }
        finally
        {
            try
            {
                if (File.Exists(temporal))
                    File.Delete(temporal);
            }
            catch (Exception ex)
            {
                // Un temporal que queda en %TEMP% no le hace daño a nadie.
                Log.Debug(ex, "No se pudo borrar el temporal {Ruta}", temporal);
            }
        }
    }
}
