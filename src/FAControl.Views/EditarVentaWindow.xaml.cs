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
/// Se corrige TODO, incluida la cantidad de plazos, y también con cobros ya
/// hechos: la plata que el cliente entregó se reparte de nuevo sobre el plan
/// corregido. Los recibos no se tocan — conservan su número, su fecha y su
/// monto—; lo que cambia es a qué plazo se imputa cada uno.
///
/// Cuando ya hay cobros, el aviso lo dice ANTES de tocar nada: quien corrige
/// tiene que saber que está por mover plata que ya entró.
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
        VentanaAjustable.Ajustar(this);
        ChromeVentana.OcultarBotones(this);
        _datos = datos;

        TextoTitulo.Text = $"Corregir la venta {datos.Codigo}";

        // Opcion<T> y NO un record privado: WPF no bindea a tipos no publicos,
        // asi que el combo mostraba "OpcionMetodo { Valor = Efectivo, ... }".
        ComboMetodo.ItemsSource = Enum.GetValues<MetodoPago>()
            .Select(m => new Opcion<MetodoPago>(m, Textos.De(m))).ToList();
        ComboMetodo.SelectedIndex = Array.IndexOf(Enum.GetValues<MetodoPago>(), datos.Metodo);

        CajaPrecio.Text = datos.Precio.ToString("0.##", Textos.CulturaRd);
        CajaInicial.Text = datos.Inicial.ToString("0.##", Textos.CulturaRd);
        CajaPlazos.Text = datos.CantidadPlazos.ToString(CultureInfo.InvariantCulture);
        CajaNotas.Text = datos.Notas ?? string.Empty;

        // La cantidad de plazos solo tiene sentido en una venta financiada
        ZonaPlazos.Visibility = datos.Tipo == TipoVenta.Plazos
            ? Visibility.Visible : Visibility.Collapsed;

        // En una separación el adelanto no es "inicial de un plan": se etiqueta
        // como lo que es, para que el usuario no lo confunda con otra cosa.
        EtiquetaInicial.Text = datos.Tipo switch
        {
            TipoVenta.Separacion => "Adelanto recibido (DOP)",
            TipoVenta.Plazos => "Inicial (DOP)",
            _ => "Recibido al firmar (DOP)"
        };

        // Con plata ya cobrada, avisar de entrada que se va a repartir de nuevo
        if (datos.YaCobrado > 0m)
        {
            AvisoLimite.Visibility = Visibility.Visible;
            TextoLimite.Text =
                $"Ojo: el cliente ya pagó {datos.YaCobrado:N2} DOP de esta venta. Al corregirla, " +
                "esa plata se reparte de nuevo sobre el calendario corregido —los recibos no se " +
                "tocan, solo cambia a qué plazo se imputan—. Si sobra, queda a favor del cliente.";
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
        if (TextoCuenta is null)
            return;

        if (!TryLeer(out var precio, out var inicial, out var plazos))
        {
            TextoCuenta.Text = "Completa el precio, la inicial y los plazos para ver cómo queda.";
            return;
        }

        TextoCuenta.Text = _datos.Previsualizar(precio, inicial, plazos);
    }

    private bool TryLeer(out decimal precio, out decimal inicial, out int plazos)
    {
        precio = 0m;
        inicial = 0m;
        plazos = _datos.CantidadPlazos;

        if (!decimal.TryParse(CajaPrecio.Text, NumberStyles.Number, Textos.CulturaRd, out precio)
            || precio <= 0m)
            return false;
        if (!decimal.TryParse(CajaInicial.Text, NumberStyles.Number, Textos.CulturaRd, out inicial)
            || inicial < 0m)
            return false;
        // En una venta al contado o una separación no hay plazos que leer
        if (_datos.Tipo == TipoVenta.Plazos &&
            (!int.TryParse(CajaPlazos.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out plazos)
             || plazos < 1))
            return false;
        return true;
    }

    private void Confirmar_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(CajaMotivo.Text))
        {
            TextoError.Text = "Escribe por qué se corrige: queda en el historial.";
            CajaMotivo.Focus();
            return;
        }
        if (ComboMetodo.SelectedItem is not Opcion<MetodoPago> metodo)
        {
            TextoError.Text = "Elige el método de pago.";
            return;
        }

        if (!TryLeer(out var precio, out var inicial, out var plazos))
        {
            TextoError.Text = "Revisa el precio, la inicial y los plazos: el precio y los plazos " +
                              "tienen que ser mayores que cero y la inicial no puede ser negativa.";
            return;
        }
        if (inicial > precio)
        {
            TextoError.Text = "La inicial no puede ser mayor que el precio de venta.";
            return;
        }

        Resultado = new EdicionVenta(_datos.VentaId, precio, inicial, metodo.Valor,
            Notas: string.IsNullOrWhiteSpace(CajaNotas.Text) ? null : CajaNotas.Text.Trim(),
            Motivo: CajaMotivo.Text.Trim(),
            CantidadPlazos: _datos.Tipo == TipoVenta.Plazos ? plazos : null);
        DialogResult = true;
    }

    private void Volver_Click(object sender, RoutedEventArgs e) => Close();

}
