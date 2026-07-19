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

    public PagareWindow(PagareImpreso pagare)
    {
        InitializeComponent();
        ChromeVentana.OcultarBotones(this);
        _pagare = pagare;
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
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error imprimiendo el pagaré {Codigo}", _pagare.CodigoPrestamo);
            MessageBox.Show(this, $"No se pudo imprimir el pagaré.\n\n{ex.Message}",
                "Imprimir pagaré", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BotonCerrar_Click(object sender, RoutedEventArgs e) => Close();
}
