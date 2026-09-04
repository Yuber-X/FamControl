using System.Windows;
using System.Windows.Controls.Primitives;
using Microsoft.Win32;
using FAControl.Models;
using FAControl.Printing;
using Serilog;

namespace FAControl.Views;

/// <summary>
/// Vista previa e impresión de los TRES contratos de un préstamo (pedido del
/// cliente 2026-09-03): el pagaré de siempre, el acta notarial y la combinación
/// de ambos.
///
/// Reemplaza a <c>PagareWindow</c>, que abría directo un solo documento. Los
/// tres comparten visor, zoom, PDF e impresión: lo único que cambia es qué
/// fábrica arma el FlowDocument.
///
/// LO QUE SE IMPRIME QUEDA ARCHIVADO en el expediente del préstamo. Antes esto
/// no pasaba: <c>PagareWindow</c> sabía archivar, pero las dos pantallas que la
/// abrían nunca le pasaban el expediente, así que el archivado salía temprano y
/// no guardaba nada. Aquí el expediente viaja siempre.
/// </summary>
public partial class ContratosWindow : Window
{
    private readonly PagareNotarialImpreso _contrato;
    private readonly DuenoExpediente? _expedienteDe;
    private readonly ViewModels.ExpedienteViewModel? _expediente;
    private TipoContrato _seleccionado;

    /// <param name="expedienteDe">
    /// Préstamo en cuyo expediente se archiva lo impreso. Null solo en el
    /// borrador de Nuevo Préstamo, cuando el préstamo todavía no existe.
    /// </param>
    public ContratosWindow(PagareNotarialImpreso contrato,
        TipoContrato inicial = TipoContrato.Pagare,
        DuenoExpediente? expedienteDe = null,
        ViewModels.ExpedienteViewModel? expediente = null)
    {
        InitializeComponent();
        ChromeVentana.OcultarBotones(this);
        VentanaAjustable.Ajustar(this);
        _contrato = contrato;
        _expedienteDe = expedienteDe;
        _expediente = expediente;
        Title = $"Contratos — préstamo {contrato.Deuda.CodigoPrestamo}";
        Mostrar(inicial);
    }

    // ==================================================================
    // Selección del documento
    // ==================================================================

    private void Seleccionar_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: string tag } && int.TryParse(tag, out var valor))
            Mostrar((TipoContrato)valor);
    }

    private void Mostrar(TipoContrato tipo)
    {
        _seleccionado = tipo;

        // Los ToggleButton se manejan a mano: un RadioButton con estilo de
        // segmento habría hecho lo mismo, pero obliga a compartir GroupName y
        // eso rompe si algún día la ventana muestra dos juegos de botones.
        BotonPagare.IsChecked = tipo == TipoContrato.Pagare;
        BotonNotarial.IsChecked = tipo == TipoContrato.Notarial;
        BotonCombinado.IsChecked = tipo == TipoContrato.Combinado;

        Descripcion.Text = TiposDeContrato.Descripcion(tipo);
        BotonImprimir.Content = $"Imprimir {TiposDeContrato.Nombre(tipo).ToLowerInvariant()}";

        // Un FlowDocument tiene un solo padre lógico: el del visor no se puede
        // mandar también a la impresora, así que siempre se pide uno nuevo.
        Visor.Document = ContratoDocumentFactory.Crear(tipo, _contrato);
        Visor.Zoom = 100;
        EtiquetaZoom.Text = "100%";

        MostrarFaltantes(tipo);
    }

    /// <summary>
    /// Avisa qué le falta al acta, sin bloquear nada. Un acta se imprime
    /// incompleta a propósito: el notario llena a mano lo que falte. Lo que sí
    /// hay que evitar es que alguien la imprima sin darse cuenta de los huecos.
    /// </summary>
    private void MostrarFaltantes(TipoContrato tipo)
    {
        if (tipo == TipoContrato.Pagare)
        {
            AvisoFaltantes.Visibility = Visibility.Collapsed;
            return;
        }

        var faltan = _contrato.Acto.QueFalta();
        if (faltan.Count == 0)
        {
            AvisoFaltantes.Visibility = Visibility.Collapsed;
            return;
        }

        TextoFaltantes.Text =
            $"El acta se imprime igual, con una raya para llenar a mano, pero todavía falta " +
            $"{string.Join(", ", faltan)}. Lo que se repite en todas las actas (notario, " +
            $"representante y testigos) se carga una sola vez en Configuración → Pagaré notarial.";
        AvisoFaltantes.Visibility = Visibility.Visible;
    }

    // ==================================================================
    // Acciones
    // ==================================================================

    private void BotonImprimir_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ImpresionDeContratos.ImprimirYArchivar(
                _seleccionado, _contrato, _expedienteDe, _expediente);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error imprimiendo el contrato {Tipo} del préstamo {Codigo}",
                _seleccionado, _contrato.Deuda.CodigoPrestamo);
            MessageBox.Show(this, $"No se pudo imprimir.\n\n{ex.Message}",
                "Imprimir", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BotonGuardarPdf_Click(object sender, RoutedEventArgs e)
    {
        var dialogo = new SaveFileDialog
        {
            Title = $"Guardar {TiposDeContrato.Nombre(_seleccionado).ToLowerInvariant()} como PDF",
            Filter = "PDF (*.pdf)|*.pdf",
            FileName = TiposDeContrato.NombreArchivo(_seleccionado, _contrato.Deuda.CodigoPrestamo) + ".pdf"
        };
        if (dialogo.ShowDialog(this) != true)
            return;

        try
        {
            ImpresoraRecibos.GuardarDocumentoPdf(
                ContratoDocumentFactory.Crear(_seleccionado, _contrato),
                dialogo.FileName,
                ContratoDocumentFactory.Descripcion(_seleccionado, _contrato.Deuda.CodigoPrestamo));
            MessageBox.Show(this, $"Guardado en:\n{dialogo.FileName}",
                "Guardar PDF", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error guardando el contrato {Tipo} en PDF", _seleccionado);
            MessageBox.Show(this, $"No se pudo guardar el PDF.\n\n{ex.Message}",
                "Guardar PDF", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ---------- Zoom ----------
    private void BotonAcercar_Click(object sender, RoutedEventArgs e) => AjustarZoom(+20);
    private void BotonAlejar_Click(object sender, RoutedEventArgs e) => AjustarZoom(-20);

    private void AjustarZoom(double delta)
    {
        var nuevo = Math.Clamp(Visor.Zoom + delta, 50, 300);
        Visor.Zoom = nuevo;
        EtiquetaZoom.Text = $"{nuevo:0}%";
    }

    private void BotonCerrar_Click(object sender, RoutedEventArgs e) => Close();
}
