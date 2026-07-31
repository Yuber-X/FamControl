using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FAControl.Models;
using FAControl.ViewModels;

namespace FAControl.Views;

/// <summary>
/// Corrección de una venta ya registrada (033 — el botón "Editar" que el
/// cliente pidió junto al de cancelar).
///
/// DOS NIVELES. Sin abonos se corrige todo y el calendario de plazos se
/// regenera. Con abonos, el precio y la inicial quedan bloqueados: cada abono
/// emitió un recibo numerado que se entregó impreso y afirma un saldo. El aviso
/// rojo explica el porqué — un campo gris sin explicación se lee como un error
/// del programa.
///
/// La vista previa del calendario baja como delegado desde el ViewModel: Views
/// no referencia Services (lo impide el grafo de proyectos, a propósito).
/// </summary>
public partial class EditarVentaWindow : Window
{
    private readonly VentaParaEditar _datos;

    /// <summary>La corrección confirmada. Solo válida si el diálogo devolvió true.</summary>
    public EdicionVenta? Resultado { get; private set; }

    public EditarVentaWindow(VentaParaEditar datos)
    {
        InitializeComponent();
        ChromeVentana.OcultarBotones(this);
        _datos = datos;

        TextoTitulo.Text = $"Corregir la venta {datos.Codigo}";

        ComboMetodo.ItemsSource = Enum.GetValues<MetodoPago>()
            .Select(m => new OpcionMetodo(m, Textos.De(m))).ToList();
        ComboMetodo.DisplayMemberPath = nameof(OpcionMetodo.Etiqueta);
        ComboMetodo.SelectedIndex = Array.IndexOf(Enum.GetValues<MetodoPago>(), datos.Metodo);

        CajaPrecio.Text = datos.Precio.ToString("0.##", Textos.CulturaRd);
        CajaInicial.Text = datos.Inicial.ToString("0.##", Textos.CulturaRd);
        CajaNotas.Text = datos.Notas ?? string.Empty;

        // En una separación el adelanto no es "inicial de un plan": se etiqueta
        // como lo que es, para que el usuario no lo confunda con otra cosa.
        EtiquetaInicial.Text = datos.Tipo switch
        {
            TipoVenta.Separacion => "Adelanto recibido (DOP)",
            TipoVenta.Plazos => "Inicial (DOP)",
            _ => "Recibido al firmar (DOP)"
        };

        if (datos.Permitido.SoloDescriptivo)
        {
            AvisoLimite.Visibility = Visibility.Visible;
            TextoLimite.Text = datos.Permitido.Motivo;
            ZonaMontos.IsEnabled = false;
            PanelCuenta.Visibility = Visibility.Collapsed;
        }

        ActualizarCuenta();
        CajaMotivo.Focus();
    }

    private void Campo_Changed(object sender, TextChangedEventArgs e) => ActualizarCuenta();

    /// <summary>Muestra en qué queda el calendario con los valores tipeados.</summary>
    private void ActualizarCuenta()
    {
        // TextChanged dispara durante InitializeComponent, antes de que existan
        // los demás controles.
        if (TextoCuenta is null || _datos.Permitido.SoloDescriptivo)
            return;

        if (!TryLeer(out var precio, out var inicial))
        {
            TextoCuenta.Text = "Completá el precio y la inicial para ver cómo queda.";
            return;
        }

        TextoCuenta.Text = _datos.Previsualizar(precio, inicial);
    }

    private bool TryLeer(out decimal precio, out decimal inicial)
    {
        precio = 0m;
        inicial = 0m;
        if (!decimal.TryParse(CajaPrecio.Text, NumberStyles.Number, Textos.CulturaRd, out precio)
            || precio <= 0m)
            return false;
        if (!decimal.TryParse(CajaInicial.Text, NumberStyles.Number, Textos.CulturaRd, out inicial)
            || inicial < 0m)
            return false;
        return true;
    }

    private void Confirmar_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(CajaMotivo.Text))
        {
            TextoError.Text = "Escribí por qué se corrige: queda en el historial.";
            CajaMotivo.Focus();
            return;
        }
        if (ComboMetodo.SelectedItem is not OpcionMetodo metodo)
        {
            TextoError.Text = "Elegí el método de pago.";
            return;
        }

        var leyo = TryLeer(out var precio, out var inicial);
        if (!leyo && !_datos.Permitido.SoloDescriptivo)
        {
            TextoError.Text = "Revisá el precio y la inicial: el precio tiene que ser mayor que cero " +
                              "y la inicial no puede ser negativa.";
            return;
        }
        if (leyo && inicial > precio)
        {
            TextoError.Text = "La inicial no puede ser mayor que el precio de venta.";
            return;
        }

        // Con abonos hechos los montos van bloqueados: se mandan los que ya
        // tenía la venta, que además es lo que el servicio va a respetar.
        if (!leyo)
        {
            precio = _datos.Precio;
            inicial = _datos.Inicial;
        }

        Resultado = new EdicionVenta(_datos.VentaId, precio, inicial, metodo.Valor,
            Notas: string.IsNullOrWhiteSpace(CajaNotas.Text) ? null : CajaNotas.Text.Trim(),
            Motivo: CajaMotivo.Text.Trim());
        DialogResult = true;
    }

    private void Volver_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Opción del combo de método de pago.</summary>
    private record OpcionMetodo(MetodoPago Valor, string Etiqueta);
}
