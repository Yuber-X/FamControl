using System.Windows;
using Microsoft.Win32;
using FAControl.Models;
using FAControl.Printing;
using Serilog;

namespace FAControl.Views;

/// <summary>
/// Vista previa, impresión y PDF de la ficha del vehículo (pedido 2026-07-25).
/// El visual mostrado es EXACTAMENTE el que se imprime.
/// </summary>
public partial class FichaVehiculoWindow : Window
{
    private readonly FichaVehiculoImpresa _ficha;

    public FichaVehiculoWindow(FichaVehiculoImpresa ficha)
    {
        InitializeComponent();
        VentanaAjustable.Ajustar(this);
        ChromeVentana.OcultarBotones(this);
        _ficha = ficha;
        ContenedorFicha.Content = FichaVehiculoVisualFactory.Crear(ficha);
    }

    private void BotonImprimir_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var visualImpresion = FichaVehiculoVisualFactory.Crear(_ficha);
            ImpresoraRecibos.Imprimir(visualImpresion, $"Ficha {_ficha.Codigo}");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error imprimiendo la ficha del vehículo {Codigo}", _ficha.Codigo);
            MessageBox.Show(this, $"No se pudo imprimir la ficha.\n\n{ex.Message}",
                "Imprimir ficha", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BotonPdf_Click(object sender, RoutedEventArgs e)
    {
        var dialogo = new SaveFileDialog
        {
            Title = "Guardar ficha como PDF",
            FileName = $"Ficha_{_ficha.Codigo}.pdf",
            Filter = "Documento PDF (*.pdf)|*.pdf"
        };
        if (dialogo.ShowDialog(this) != true)
            return;

        try
        {
            var visualPdf = FichaVehiculoVisualFactory.Crear(_ficha);
            ImpresoraRecibos.GuardarPdf(visualPdf, dialogo.FileName,
                $"Ficha {_ficha.Codigo} — FAControl");
            MessageBox.Show(this, $"Ficha guardada en:\n{dialogo.FileName}",
                "Guardar PDF", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error exportando la ficha {Codigo} a PDF", _ficha.Codigo);
            MessageBox.Show(this, $"No se pudo guardar el PDF.\n\n{ex.Message}",
                "Guardar PDF", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BotonCerrar_Click(object sender, RoutedEventArgs e) => Close();
}
