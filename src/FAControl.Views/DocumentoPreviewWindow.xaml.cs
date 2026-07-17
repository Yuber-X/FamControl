using System;
using System.Windows;
using System.Windows.Documents;
using FAControl.Printing;
using Serilog;

namespace FAControl.Views;

/// <summary>
/// Vista previa + impresión genérica de un FlowDocument (reporte de clientes,
/// etc.). El documento se pasa como una fábrica para poder recrearlo: uno para
/// el visor y otro nuevo para la impresora (un FlowDocument tiene un solo padre).
/// </summary>
public partial class DocumentoPreviewWindow : Window
{
    private readonly Func<FlowDocument> _fabrica;
    private readonly string _descripcion;

    public DocumentoPreviewWindow(string titulo, string descripcion, Func<FlowDocument> fabrica)
    {
        InitializeComponent();
        Title = titulo;
        _descripcion = descripcion;
        _fabrica = fabrica;
        Visor.Document = fabrica();
    }

    private void BotonImprimir_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ImpresoraRecibos.ImprimirDocumento(_fabrica(), _descripcion);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error imprimiendo el documento {Desc}", _descripcion);
            MessageBox.Show(this, $"No se pudo imprimir.\n\n{ex.Message}",
                "Imprimir", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BotonCerrar_Click(object sender, RoutedEventArgs e) => Close();
}
