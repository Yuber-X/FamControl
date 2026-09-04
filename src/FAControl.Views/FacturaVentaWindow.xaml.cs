using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;
using FAControl.Models;
using FAControl.Printing;
using FAControl.ViewModels;
using Serilog;

namespace FAControl.Views;

/// <summary>
/// Vista previa, impresión y PDF de la factura de una venta al contado
/// (pedido 2026-07-25). El visual mostrado es EXACTAMENTE el que se imprime.
/// </summary>
public partial class FacturaVentaWindow : Window
{
    private readonly FacturaVentaImpresa _factura;
    private readonly long _ventaId;
    private readonly ExpedienteViewModel _expediente;
    private DocumentoFila? _firmada;

    public FacturaVentaWindow(FacturaVentaImpresa factura, long ventaId,
        ExpedienteViewModel expediente)
    {
        InitializeComponent();
        VentanaAjustable.Ajustar(this);
        ChromeVentana.OcultarBotones(this);
        _factura = factura;
        _ventaId = ventaId;
        _expediente = expediente;
        ContenedorFactura.Content = FacturaVentaVisualFactory.Crear(factura);
        _ = MostrarFirmadaAsync();
    }

    /// <summary>Si ya hay una factura firmada escaneada, se avisa y se ofrece abrirla.</summary>
    private async Task MostrarFirmadaAsync()
    {
        _firmada = await _expediente.ObtenerFacturaEscaneadaAsync(_ventaId);
        if (_firmada is null)
        {
            BotonVerFirmada.Visibility = Visibility.Collapsed;
            TextoFirmada.Text = string.Empty;
            BotonEscanear.Content = "Reemplazar por la firmada…";
            return;
        }

        BotonVerFirmada.Visibility = Visibility.Visible;
        TextoFirmada.Text = $"Firmada: {_firmada.Nombre} ({_firmada.FechaTexto})";
        BotonEscanear.Content = "Subir otra versión firmada…";
    }

    /// <summary>
    /// Sube la factura firmada y escaneada al expediente del contrato
    /// (pedido del cliente 2026-07-27). La del sistema no se borra.
    /// </summary>
    private async void BotonEscanear_Click(object sender, RoutedEventArgs e)
    {
        var dialogo = new OpenFileDialog
        {
            Title = "Elige la factura firmada y escaneada",
            Filter = ExpedienteViewModel.FiltroArchivos
        };
        if (dialogo.ShowDialog(this) != true)
            return;

        var subida = await _expediente.ReemplazarFacturaAsync(_ventaId, dialogo.FileName);
        if (subida is null)
            return;

        await MostrarFirmadaAsync();
        MessageBox.Show(this,
            "La factura firmada quedó guardada en el expediente del contrato.\n\n" +
            "Desde ahora aparece aquí y en la sección Expediente del financiamiento.",
            "Factura firmada", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BotonVerFirmada_Click(object sender, RoutedEventArgs e)
    {
        if (_firmada is null)
            return;

        var ruta = _expediente.RutaParaAbrir(_firmada);
        if (ruta is null)
            return;
        try
        {
            Process.Start(new ProcessStartInfo(ruta) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error abriendo la factura firmada de la venta {Id}", _ventaId);
            MessageBox.Show(this, $"Windows no pudo abrir el archivo.\n\n{ex.Message}",
                "Ver factura firmada", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BotonImprimir_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var visualImpresion = FacturaVentaVisualFactory.Crear(_factura);
            ImpresoraRecibos.Imprimir(visualImpresion, $"Factura {_factura.Codigo}");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error imprimiendo la factura {Codigo}", _factura.Codigo);
            MessageBox.Show(this, $"No se pudo imprimir la factura.\n\n{ex.Message}",
                "Imprimir factura", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BotonPdf_Click(object sender, RoutedEventArgs e)
    {
        var dialogo = new SaveFileDialog
        {
            Title = "Guardar factura como PDF",
            FileName = $"Factura_{_factura.Codigo}.pdf",
            Filter = "Documento PDF (*.pdf)|*.pdf"
        };
        if (dialogo.ShowDialog(this) != true)
            return;

        try
        {
            var visualPdf = FacturaVentaVisualFactory.Crear(_factura);
            ImpresoraRecibos.GuardarPdf(visualPdf, dialogo.FileName,
                $"Factura {_factura.Codigo} — FAControl");
            MessageBox.Show(this, $"Factura guardada en:\n{dialogo.FileName}",
                "Guardar PDF", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error exportando la factura {Codigo} a PDF", _factura.Codigo);
            MessageBox.Show(this, $"No se pudo guardar el PDF.\n\n{ex.Message}",
                "Guardar PDF", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BotonCerrar_Click(object sender, RoutedEventArgs e) => Close();
}
