using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FAControl.Models;
using FAControl.Printing;
using FAControl.ViewModels;
using Microsoft.Win32;

namespace FAControl.Views;

/// <summary>
/// Financiamiento de una venta del dealer. Code-behind solo para lógica de UI:
/// abrir el visor de la carta de compromiso o del recibo de separación.
/// </summary>
public partial class VentaFinanciamientoView : UserControl
{
    private VentaFinanciamientoViewModel? _vm;
    public VentaFinanciamientoView()
    {
        InitializeComponent();

        // Los papeles de una venta recien registrada se retiran ACA y no en
        // DataContextChanged: Loaded garantiza que la pantalla ya esta en el
        // arbol visual, asi que los dialogos tienen dueno y aparecen centrados
        // sobre ella. El buzon se vacia al retirarlos, de modo que volver a
        // entrar no los reimprime.
        Loaded += async (_, _) =>
        {
            if (_vm?.TomarPapelesPendientes() is { } papeles)
                await EmitirPapelesAsync(papeles);
        };

        // Se engancha al ViewModel (que es SINGLETON) mientras esta vista esté
        // en pantalla, y se suelta al salir. Sin el Unloaded, cada "cerrar
        // sesión" dejaba una vista muerta suscrita: el evento la seguía
        // llamando y ella intentaba abrir ventanas colgando de un shell ya
        // cerrado (cliente 2026-08-20). Loaded vuelve a enganchar si WPF
        // recicla la instancia.
        DataContextChanged += (_, _) => Reenganchar();
        Loaded += (_, _) => Reenganchar();
        Unloaded += (_, _) => Desenganchar();
    }

    private void Reenganchar()
    {
        Desenganchar();
        _vm = DataContext as VentaFinanciamientoViewModel;
        if (_vm is not null)
        {
            _vm.CartaSolicitada += MostrarCarta;
            _vm.SeparacionSolicitada += MostrarSeparacion;
            // Delegados, no eventos: se sobreescriben en vez de acumularse, así
            // que siempre responde la vista que se enganchó última (la viva).
            _vm.CancelacionSolicitada = PedirCancelacion;
            _vm.EdicionSolicitada = PedirCorreccion;
        }
    }

    private void Desenganchar()
    {
        if (_vm is null)
            return;
        _vm.CartaSolicitada -= MostrarCarta;
        _vm.SeparacionSolicitada -= MostrarSeparacion;
    }

    private void MostrarCarta(CartaCompromisoImpresa carta)
    {
        var ventana = new DocumentoDealWindow("Carta de compromiso",
            $"CartaCompromiso_{carta.Codigo}",
            () => DocumentosDealFactory.CrearCartaCompromiso(carta));
        ventana.MostrarDesde(this);
    }

    private void MostrarSeparacion(ReciboSeparacionImpreso recibo)
    {
        var ventana = new DocumentoDealWindow("Recibo de separación",
            $"Separacion_{recibo.Codigo}",
            () => DocumentosDealFactory.CrearReciboSeparacion(recibo));
        ventana.MostrarDesde(this);
    }

    // ---------- Emisión automática al registrar la venta (033) ----------

    /// <summary>
    /// Imprime los papeles de la venta recién registrada y los archiva solos en
    /// su expediente.
    ///
    /// Pedido del cliente (2026-07-30): "al realizar y registrar la venta,
    /// debería imprimir el pagaré de forma inmediata (o la cantidad de contratos
    /// que se debería imprimir, como la carta de compromiso también), y guardar
    /// de forma automática los contratos imprimidos en el financiamiento de
    /// ventas como nuevos archivos".
    ///
    /// SE ARCHIVA AUNQUE NO SE IMPRIMA. El PDF se guarda apenas se genera el
    /// documento, no al mandar a la impresora: lo que se busca es tener el papel
    /// en el expediente, y atarlo a que la impresora responda significaría
    /// perder el registro cada vez que se queda sin papel.
    ///
    /// Nada de esto puede tumbar la pantalla: la venta YA está registrada. Si
    /// falla la impresión o el archivado, se registra y se sigue.
    /// </summary>
    private async Task EmitirPapelesAsync(PapelesDeVenta papeles)
    {
        var dueno = DuenoExpediente.DeVenta(papeles.VentaId);

        // 1. La factura
        try
        {
            new FacturaVentaWindow(papeles.Factura, papeles.VentaId, _vm!.Expediente)
                .MostrarDesde(this);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "No se pudo mostrar la factura de {Codigo}", papeles.Codigo);
        }

        // 2. El contrato que corresponda: carta de compromiso o recibo de
        //    separación. Nunca los dos: dependen de cómo se pactó la venta.
        if (papeles.Carta is { } carta)
        {
            await ArchivarYMostrarAsync(dueno, "Carta de compromiso",
                $"CartaCompromiso_{papeles.Codigo}",
                () => DocumentosDealFactory.CrearCartaCompromiso(carta));
        }
        else if (papeles.Separacion is { } recibo)
        {
            await ArchivarYMostrarAsync(dueno, "Recibo de separación",
                $"Separacion_{papeles.Codigo}",
                () => DocumentosDealFactory.CrearReciboSeparacion(recibo));
        }

        if (_vm is not null)
            await _vm.RefrescarExpedienteAsync();
    }

    /// <summary>
    /// Genera el PDF, lo mete en el expediente y abre el visor para imprimirlo.
    ///
    /// La FÁBRICA se llama dos veces a propósito: un FlowDocument tiene un solo
    /// padre lógico, así que el PDF y el visor necesitan cada uno el suyo.
    /// </summary>
    private async Task ArchivarYMostrarAsync(DuenoExpediente dueno, string titulo,
        string nombreArchivo, Func<System.Windows.Documents.FlowDocument> fabrica)
    {
        var temporal = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            $"{nombreArchivo}.pdf");
        try
        {
            ImpresoraRecibos.GuardarDocumentoPdf(fabrica(), temporal, titulo);
            await _vm!.Expediente.ArchivarImpresoAsync(dueno, temporal, TipoDocumento.Contrato);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "No se pudo archivar {Titulo} de {Dueno}", titulo, dueno.Descripcion);
        }
        finally
        {
            try { if (System.IO.File.Exists(temporal)) System.IO.File.Delete(temporal); }
            catch (Exception) { /* temporal: si queda, Windows lo limpia */ }
        }

        try
        {
            new DocumentoDealWindow(titulo, nombreArchivo, fabrica).MostrarDesde(this);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "No se pudo mostrar {Titulo}", titulo);
        }
    }

    /// <summary>
    /// Abre el formulario de corrección (033) y devuelve lo confirmado, o null
    /// si el usuario se arrepintió: abrir el diálogo no cambia nada.
    /// </summary>
    private EdicionVenta? PedirCorreccion(VentaParaEditar datos)
    {
        var ventana = new EditarVentaWindow(datos);
        return ventana.MostrarDesde(this) == true ? ventana.Resultado : null;
    }

    /// <summary>
    /// Pide motivo y porcentaje de retención para cancelar la venta (028).
    /// Devuelve null si el usuario se arrepintió: abrir el diálogo no cancela nada.
    /// </summary>
    private (string Motivo, decimal Porcentaje, bool Fijar)? PedirCancelacion(
        string codigo, decimal cobrado, decimal porcentaje, bool fija)
    {
        var ventana = new CancelarVentaWindow(codigo, cobrado, porcentaje, fija);
        return ventana.MostrarDesde(this) == true
            ? (ventana.Motivo, ventana.Porcentaje, ventana.FijarPorcentaje)
            : null;
    }
}
