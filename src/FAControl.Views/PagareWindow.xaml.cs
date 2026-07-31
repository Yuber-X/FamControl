using System.Windows;
using Microsoft.Win32;
using FAControl.Models;
using FAControl.Printing;
using Serilog;

namespace FAControl.Views;

/// <summary>
/// Vista previa e impresión del pagaré (cliente 2026-07-17: contrato a firmar
/// por cliente y prestamista). Se muestra automáticamente al crear el préstamo
/// y también desde el botón "Imprimir pagaré".
/// </summary>
public partial class PagareWindow : Window
{
    private readonly PagareImpreso _pagare;
    private readonly DuenoExpediente? _expedienteDe;
    private readonly ViewModels.ExpedienteViewModel? _expediente;

    /// <param name="expedienteDe">
    /// Préstamo en cuyo expediente se archiva una copia al imprimir (026).
    /// Null cuando el pagaré todavía no tiene préstamo — la vista previa del
    /// borrador, antes de crearlo.
    /// </param>
    public PagareWindow(PagareImpreso pagare, DuenoExpediente? expedienteDe = null,
        ViewModels.ExpedienteViewModel? expediente = null)
    {
        InitializeComponent();
        ChromeVentana.OcultarBotones(this);
        _pagare = pagare;
        _expedienteDe = expedienteDe;
        _expediente = expediente;
        // Un documento nuevo para el visor: el mismo objeto no se puede
        // compartir entre el visor y la impresión (un FlowDocument tiene un
        // solo padre lógico).
        Visor.Document = PagareDocumentFactory.Crear(pagare);
    }

    // ---------- Zoom (FlowDocumentPageViewer.Zoom es un % ) ----------
    private void BotonAcercar_Click(object sender, RoutedEventArgs e) => AjustarZoom(+20);
    private void BotonAlejar_Click(object sender, RoutedEventArgs e) => AjustarZoom(-20);

    private void AjustarZoom(double delta)
    {
        var nuevo = Math.Clamp(Visor.Zoom + delta, 50, 300);
        Visor.Zoom = nuevo;
        EtiquetaZoom.Text = $"{nuevo:0}%";
    }

    private void BotonGuardarPdf_Click(object sender, RoutedEventArgs e)
    {
        var dialogo = new SaveFileDialog
        {
            Title = "Guardar pagaré como PDF",
            Filter = "PDF (*.pdf)|*.pdf",
            FileName = $"Pagare_{_pagare.CodigoPrestamo}.pdf"
        };
        if (dialogo.ShowDialog(this) != true)
            return;

        try
        {
            var documento = PagareDocumentFactory.Crear(_pagare);
            ImpresoraRecibos.GuardarDocumentoPdf(documento, dialogo.FileName, $"Pagaré {_pagare.CodigoPrestamo}");
            MessageBox.Show(this, $"Pagaré guardado en:\n{dialogo.FileName}",
                "Guardar PDF", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error guardando el pagaré {Codigo} en PDF", _pagare.CodigoPrestamo);
            MessageBox.Show(this, $"No se pudo guardar el PDF.\n\n{ex.Message}",
                "Guardar PDF", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BotonImprimir_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var documento = PagareDocumentFactory.Crear(_pagare);
            ImpresoraRecibos.ImprimirDocumento(documento, $"Pagaré {_pagare.CodigoPrestamo}");
            // Pedido de Yuber (2026-07-30): lo que se imprime queda guardado en
            // el expediente del cliente, sin que nadie tenga que acordarse.
            _ = ArchivarCopiaAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error imprimiendo el pagaré {Codigo}", _pagare.CodigoPrestamo);
            MessageBox.Show(this, $"No se pudo imprimir el pagaré.\n\n{ex.Message}",
                "Imprimir pagaré", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Deja una copia del pagaré en el expediente del préstamo. Se genera un PDF
    /// temporal porque el expediente guarda archivos, no documentos de WPF.
    ///
    /// Si algo falla no se le avisa al usuario: el pagaré YA se imprimió, que era
    /// lo que pidió. El fallo queda en el log.
    /// </summary>
    private async Task ArchivarCopiaAsync()
    {
        if (_expedienteDe is not { } dueno || _expediente is null)
            return;

        var temporal = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            $"Pagare_{_pagare.CodigoPrestamo}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");
        try
        {
            ImpresoraRecibos.GuardarDocumentoPdf(PagareDocumentFactory.Crear(_pagare), temporal,
                $"Pagaré {_pagare.CodigoPrestamo}");
            await _expediente.ArchivarImpresoAsync(dueno, temporal, TipoDocumento.Pagare);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "No se pudo archivar el pagaré {Codigo} en el expediente",
                _pagare.CodigoPrestamo);
        }
        finally
        {
            try { if (System.IO.File.Exists(temporal)) System.IO.File.Delete(temporal); }
            catch (Exception) { /* archivo temporal: si queda, Windows lo limpia */ }
        }
    }

    private void BotonCerrar_Click(object sender, RoutedEventArgs e) => Close();
}
