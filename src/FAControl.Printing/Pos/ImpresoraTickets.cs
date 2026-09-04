// Portado de POS500.Printing el 2026-07-30 al integrar el punto de venta a la
// suite. Los datos que imprime vienen de pos500_db; el usuario que factura,
// del SesionActual compartido de FAControl.
using System.Windows;
using System.Windows.Controls;

namespace FAControl.Printing.Pos;

/// <summary>
/// Envío del ticket a la impresora (PrintVisual, patrón PrestControl).
/// La impresión NUNCA bloquea la venta (spec §9.6): quien llama ya persistió
/// la factura y maneja el reintento si esto falla.
/// </summary>
public static class ImpresoraTickets
{
    /// <summary>Abre el diálogo de impresión del sistema. True si se envió a imprimir.</summary>
    /// <param name="paginar">
    /// Repartir el visual en varias hojas si no entra en una.
    ///
    /// FALSE para el ticket: la térmica usa rollo continuo y ahí paginar sería
    /// el error, porque cortaría el ticket en pedazos. TRUE para lo que va en
    /// hoja suelta —el cierre de caja en Carta—, donde lo que no entra se
    /// perdía sin decir nada: PrintVisual recorta en silencio.
    /// </param>
    public static bool Imprimir(FrameworkElement visual, string descripcion, int copias = 1,
        bool paginar = false)
    {
        var dialogo = new PrintDialog();
        if (dialogo.ShowDialog() != true)
            return false;

        if (paginar && NoEntraEnUnaHoja(visual, dialogo))
        {
            var documento = VisualPaginado.Paginar(visual,
                dialogo.PrintableAreaWidth, dialogo.PrintableAreaHeight);
            for (var i = 0; i < Math.Max(1, copias); i++)
                dialogo.PrintDocument(documento.DocumentPaginator, descripcion);
            return true;
        }

        for (var i = 0; i < Math.Max(1, copias); i++)
            dialogo.PrintVisual(visual, descripcion);
        return true;
    }

    /// <summary>
    /// El visual es más alto que el área imprimible. Se mide con el alto REAL
    /// que reporta la impresora elegida, no con un valor fijo: no es lo mismo
    /// Carta que A4, ni con márgenes distintos.
    /// </summary>
    private static bool NoEntraEnUnaHoja(FrameworkElement visual, PrintDialog dialogo)
    {
        var alto = Math.Max(visual.ActualHeight, visual.DesiredSize.Height);
        return alto > 0 && dialogo.PrintableAreaHeight > 0 && alto > dialogo.PrintableAreaHeight;
    }

    /// <summary>
    /// Imprime SIN preguntar (flujo por defecto al cobrar, pedido Yuber
    /// 2026-07-12): usa la impresora configurada o la predeterminada.
    /// </summary>
    /// <remarks>
    /// PrintQueue y PrintServer se LIBERAN: los dos son IDisposable y envuelven
    /// handles del spooler de Windows. Sin liberarlos, cada ticket dejaba uno
    /// colgando, y en un mostrador que factura todo el dia eso se acumula hasta
    /// que el spooler falla — con el sintoma ("de repente dejo de imprimir")
    /// apareciendo horas despues y lejos de la causa.
    ///
    /// Si la impresora configurada ya no existe, el constructor de PrintQueue
    /// tira y se deja propagar: quien llama lo atrapa y cae a la vista previa.
    /// </remarks>
    public static void ImprimirDirecto(FrameworkElement visual, string descripcion,
        int copias = 1, string? nombreImpresora = null)
    {
        var dialogo = new PrintDialog();

        if (string.IsNullOrWhiteSpace(nombreImpresora))
        {
            ImprimirCopias(dialogo, visual, descripcion, copias);
            return;
        }

        using var servidor = new System.Printing.PrintServer();
        using var cola = new System.Printing.PrintQueue(servidor, nombreImpresora);
        dialogo.PrintQueue = cola;
        ImprimirCopias(dialogo, visual, descripcion, copias);
    }

    private static void ImprimirCopias(PrintDialog dialogo, FrameworkElement visual,
        string descripcion, int copias)
    {
        for (var i = 0; i < Math.Max(1, copias); i++)
            dialogo.PrintVisual(visual, descripcion);
    }
}
