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

        DataContextChanged += (_, e) =>
        {
            if (_vm is not null)
            {
                _vm.CartaSolicitada -= MostrarCarta;
                _vm.SeparacionSolicitada -= MostrarSeparacion;
            }
            _vm = e.NewValue as VentaFinanciamientoViewModel;
            if (_vm is not null)
            {
                _vm.CartaSolicitada += MostrarCarta;
                _vm.SeparacionSolicitada += MostrarSeparacion;
                _vm.CancelacionSolicitada = PedirCancelacion;
                _vm.EdicionSolicitada = PedirCorreccion;
            }
        };
    }

    private void MostrarCarta(CartaCompromisoImpresa carta)
    {
        var ventana = new DocumentoDealWindow("Carta de compromiso",
            $"CartaCompromiso_{carta.Codigo}",
            () => DocumentosDealFactory.CrearCartaCompromiso(carta))
        { Owner = Window.GetWindow(this) };
        ventana.ShowDialog();
    }

    private void MostrarSeparacion(ReciboSeparacionImpreso recibo)
    {
        var ventana = new DocumentoDealWindow("Recibo de separación",
            $"Separacion_{recibo.Codigo}",
            () => DocumentosDealFactory.CrearReciboSeparacion(recibo))
        { Owner = Window.GetWindow(this) };
        ventana.ShowDialog();
    }

    // ---------- Expediente digital (018, pedido 2026-07-27) ----------
    // Los diálogos de archivo son UI pura: la View los abre y le pasa las rutas
    // al ViewModel, que es quien decide qué se puede guardar y qué no.

    private async void SubirDocumentos_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null)
            return;

        var dialogo = new OpenFileDialog
        {
            Title = "Elegí los documentos del cliente",
            Multiselect = true,     // pedido: poder subir varios de una vez
            Filter = ExpedienteViewModel.FiltroArchivos
        };
        if (dialogo.ShowDialog(Window.GetWindow(this)) != true)
            return;

        await _vm.Expediente.AgregarArchivosAsync(dialogo.FileNames);
    }

    private async void ExportarExpediente_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null)
            return;

        var dialogo = new SaveFileDialog
        {
            Title = "¿Dónde guardo el ZIP del expediente?",
            FileName = $"Expediente_{_vm.Codigo}.zip",
            Filter = "Archivo comprimido (*.zip)|*.zip"
        };
        if (dialogo.ShowDialog(Window.GetWindow(this)) != true)
            return;

        await _vm.Expediente.ExportarZipAsync(dialogo.FileName);
    }

    /// <summary>
    /// Abre los archivos de esta venta en su propia pantalla, con vista de
    /// lista o de iconos. Es la misma ventana que usa PrestControl: el
    /// expediente ya sabe de quién es.
    /// </summary>
    private void VerContratos_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null)
            return;

        var ventana = new ExpedienteWindow(_vm.Expediente,
            $"Contratos y documentos — {_vm.Codigo}", _vm.ClienteNombre)
        {
            Owner = Window.GetWindow(this)
        };
        ventana.ShowDialog();
    }

    private void Documento_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid grid && grid.SelectedItem is DocumentoFila fila)
            AbrirAcciones(fila);
    }

    private void AccionesDocumento_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is DocumentoFila fila)
            AbrirAcciones(fila);
    }

    private void AbrirAcciones(DocumentoFila fila)
    {
        if (_vm is null)
            return;
        var ventana = new DocumentoAccionesWindow(_vm.Expediente, fila)
        { Owner = Window.GetWindow(this) };
        ventana.ShowDialog();
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
            var ventana = new FacturaVentaWindow(papeles.Factura, papeles.VentaId, _vm!.Expediente)
            { Owner = Window.GetWindow(this) };
            ventana.ShowDialog();
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
            var ventana = new DocumentoDealWindow(titulo, nombreArchivo, fabrica)
            { Owner = Window.GetWindow(this) };
            ventana.ShowDialog();
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
        var ventana = new EditarVentaWindow(datos) { Owner = Window.GetWindow(this) };
        return ventana.ShowDialog() == true ? ventana.Resultado : null;
    }

    /// <summary>
    /// Pide motivo y porcentaje de retención para cancelar la venta (028).
    /// Devuelve null si el usuario se arrepintió: abrir el diálogo no cancela nada.
    /// </summary>
    private (string Motivo, decimal Porcentaje, bool Fijar)? PedirCancelacion(
        string codigo, decimal cobrado, decimal porcentaje, bool fija)
    {
        var ventana = new CancelarVentaWindow(codigo, cobrado, porcentaje, fija)
        {
            Owner = Window.GetWindow(this)
        };
        return ventana.ShowDialog() == true
            ? (ventana.Motivo, ventana.Porcentaje, ventana.FijarPorcentaje)
            : null;
    }
}
