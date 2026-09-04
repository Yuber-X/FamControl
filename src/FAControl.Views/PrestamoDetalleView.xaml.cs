using System.Windows;
using System.Windows.Controls;
using FAControl.Models;
using FAControl.Printing;
using FAControl.ViewModels;

namespace FAControl.Views;

public partial class PrestamoDetalleView : UserControl
{
    private PrestamoDetalleViewModel? _vm;

    public PrestamoDetalleView() => InitializeComponent();

    // Lógica de UI: abrir la vista previa imprimible (mismo patrón que CobrosView)
    private void PrestamoDetalleView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
        {
            _vm.ImpresionSolicitada -= MostrarImpresion;
            _vm.IntimacionSolicitada -= MostrarIntimacion;
            _vm.ContratosSolicitados -= MostrarContratos;
        }

        _vm = e.NewValue as PrestamoDetalleViewModel;
        if (_vm is not null)
        {
            _vm.ImpresionSolicitada += MostrarImpresion;
            _vm.IntimacionSolicitada += MostrarIntimacion;
            _vm.ContratosSolicitados += MostrarContratos;
            _vm.EdicionSolicitada = PedirCorreccion;
        }
    }

    /// <summary>
    /// Abre el formulario de corrección (029) y devuelve lo que el usuario
    /// confirmó, o null si se arrepintió: abrir el diálogo no cambia nada.
    /// </summary>
    private EdicionPrestamo? PedirCorreccion(PrestamoParaEditar datos)
    {
        var ventana = new EditarPrestamoWindow(datos);
        return ventana.MostrarDesde(this) == true ? ventana.Resultado : null;
    }

    private void MostrarImpresion(PrestamoImpreso prestamo)
    {
        new PrestamoImpresionWindow(prestamo).MostrarDesde(this);
    }

    /// <summary>
    /// Los tres contratos del préstamo (2026-09-03). El expediente viaja para
    /// que lo que se imprima quede archivado en la ficha del cliente.
    /// </summary>
    private void MostrarContratos(PagareNotarialImpreso contrato, DuenoExpediente dueno)
    {
        new ContratosWindow(contrato, TipoContrato.Pagare, dueno, _vm?.Expediente)
            .MostrarDesde(this);
    }

    private void MostrarIntimacion(IntimacionImpresa intimacion)
    {
        var ventana = new DocumentoPreviewWindow(
            "Intimación de pago",
            $"Requerimiento formal de pago para {intimacion.DeudorNombre} — préstamo {intimacion.CodigoPrestamo}.",
            () => IntimacionDocumentFactory.Crear(intimacion),
            // Al imprimirla queda archivada sola en el expediente del cliente (026)
            archivar: () => ArchivarIntimacionAsync(intimacion));
        ventana.MostrarDesde(this);
    }

    /// <summary>
    /// Guarda un PDF de la intimación en el expediente del préstamo. Se genera
    /// en un archivo temporal: el expediente guarda archivos, no documentos WPF.
    /// </summary>
    private async Task ArchivarIntimacionAsync(IntimacionImpresa intimacion)
    {
        if (_vm is null)
            return;

        var temporal = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            $"Intimacion_{intimacion.CodigoPrestamo}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");
        try
        {
            ImpresoraRecibos.GuardarDocumentoPdf(IntimacionDocumentFactory.Crear(intimacion),
                temporal, $"Intimación {intimacion.CodigoPrestamo}");
            await _vm.Expediente.ArchivarImpresoAsync(_vm.Dueno, temporal, TipoDocumento.Intimacion);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "No se pudo archivar la intimación de {Codigo}",
                intimacion.CodigoPrestamo);
        }
        finally
        {
            try { if (System.IO.File.Exists(temporal)) System.IO.File.Delete(temporal); }
            catch (Exception) { /* temporal: si queda, Windows lo limpia */ }
        }
    }
}
