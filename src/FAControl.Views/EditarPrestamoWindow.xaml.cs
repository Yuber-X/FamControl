using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FAControl.Models;
using FAControl.ViewModels;

namespace FAControl.Views;

/// <summary>
/// Corrección de un préstamo ya registrado (029).
///
/// Pedido del cliente (2026-07-30): "un btn 'editar' que solo los admin pueden
/// tener, o un permiso otorgado por el mismo a un usuario... así si se produce
/// un error de digitación se pueda arreglar."
///
/// DOS NIVELES. Si el préstamo todavía no tiene cobros se corrige todo y la
/// tabla de amortización se rehace. Si ya tiene cobros, los montos quedan
/// bloqueados: hay recibos impresos que declaran esos números. El aviso rojo
/// explica el porqué — un campo gris sin explicación se lee como un error del
/// programa.
///
/// La vista previa NO se calcula acá: baja como delegado desde el ViewModel,
/// que llama al mismo AmortizacionService que después persiste el servicio.
/// Views no referencia Services (el grafo de proyectos lo impide, a propósito).
/// </summary>
public partial class EditarPrestamoWindow : Window
{
    private readonly PrestamoParaEditar _datos;

    /// <summary>La corrección confirmada. Solo válida si el diálogo devolvió true.</summary>
    public EdicionPrestamo? Resultado { get; private set; }

    public EditarPrestamoWindow(PrestamoParaEditar datos)
    {
        InitializeComponent();
        ChromeVentana.OcultarBotones(this);
        _datos = datos;

        TextoTitulo.Text = $"Corregir el préstamo {datos.Codigo}";

        // Opcion<T> y NO un record privado de esta ventana: WPF no puede
        // bindear a propiedades de tipos que no son publicos, asi que el combo
        // caia al ToString() por defecto del record y mostraba
        // "OpcionEnum { Valor = Mensual, Etiqueta = Mensual }" en vez de
        // "Mensual". Opcion<T> ademas ya sobreescribe ToString(), asi que ni
        // siquiera hace falta DisplayMemberPath.
        ComboModalidad.ItemsSource = Enum.GetValues<Modalidad>()
            .Select(m => new Opcion<Modalidad>(m, Textos.De(m))).ToList();
        ComboMetodo.ItemsSource = Enum.GetValues<MetodoAmortizacion>()
            .Select(m => new Opcion<MetodoAmortizacion>(m, Textos.De(m))).ToList();

        var actual = datos.Actual;
        CajaCapital.Text = actual.MontoCapital.ToString("0.##", Textos.CulturaRd);
        CajaTasa.Text = actual.TasaInteres.ToString("0.##", Textos.CulturaRd);
        CajaPlazo.Text = actual.PlazoCuotas.ToString(CultureInfo.InvariantCulture);
        SelectorFecha.SelectedDate = actual.FechaInicio.ToDateTime(TimeOnly.MinValue);
        ComboModalidad.SelectedIndex = Array.IndexOf(Enum.GetValues<Modalidad>(), actual.Modalidad);
        ComboMetodo.SelectedIndex = Array.IndexOf(Enum.GetValues<MetodoAmortizacion>(), actual.MetodoAmortizacion);
        CajaGarantia.Text = actual.Garantia ?? string.Empty;
        CajaNotas.Text = actual.Notas ?? string.Empty;

        // Se muestra la cuota PACTADA, no la sugerencia: es el dato que hay que
        // conservar al corregir un préstamo diferido.
        if (actual.CuotaInicioCapital is { } inicio)
            CajaInicioCapital.Text = inicio.ToString(CultureInfo.InvariantCulture);
        MostrarZonaInicioCapital();

        if (datos.Permitido.SoloDescriptivo)
        {
            AvisoLimite.Visibility = Visibility.Visible;
            TextoLimite.Text = datos.Permitido.Motivo;
            ZonaMontos.IsEnabled = false;
            PanelPreview.Visibility = Visibility.Collapsed;
        }

        ActualizarPreview();
        CajaMotivo.Focus();
    }

    private void Campo_TextChanged(object sender, TextChangedEventArgs e) => ActualizarPreview();
    private void Fecha_Changed(object sender, SelectionChangedEventArgs e) => ActualizarPreview();

    private void Combo_Changed(object sender, SelectionChangedEventArgs e)
    {
        MostrarZonaInicioCapital();
        ActualizarPreview();
    }

    /// <summary>La cuota donde arranca el capital vive con el método diferido.</summary>
    private void MostrarZonaInicioCapital()
    {
        // Los combos disparan SelectionChanged durante InitializeComponent,
        // cuando los demás controles todavía no existen.
        if (ZonaInicioCapital is null)
            return;

        ZonaInicioCapital.Visibility =
            ComboMetodo.SelectedItem is Opcion<MetodoAmortizacion> m &&
            m.Valor == MetodoAmortizacion.CapitalDiferido &&
            ComboModalidad.SelectedItem is Opcion<Modalidad> mod &&
            mod.Valor != Modalidad.PagoUnico
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    /// <summary>Muestra en qué queda la cuota con los valores tipeados hasta ahora.</summary>
    private void ActualizarPreview()
    {
        // Los eventos de los combos disparan durante InitializeComponent,
        // antes de que existan los demás controles.
        if (TextoPreview is null || _datos.Permitido.SoloDescriptivo)
            return;

        if (!TryLeerMontos(out var parametros))
        {
            TextoPreview.Text = "Completá los datos para ver cómo queda la cuota.";
            TextoPreviewDetalle.Text = string.Empty;
            return;
        }

        var preview = _datos.Previsualizar(parametros);
        TextoPreview.Text = preview.Titular;
        TextoPreviewDetalle.Text = preview.Detalle;
    }

    private bool TryLeerMontos(out ParametrosAmortizacion parametros)
    {
        parametros = null!;

        if (!decimal.TryParse(CajaCapital.Text, NumberStyles.Number, Textos.CulturaRd, out var capital)
            || capital <= 0)
            return false;
        if (!decimal.TryParse(CajaTasa.Text, NumberStyles.Number, Textos.CulturaRd, out var tasa)
            || tasa < 0)
            return false;
        if (!int.TryParse(CajaPlazo.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var plazo)
            || plazo <= 0)
            return false;
        if (SelectorFecha.SelectedDate is not { } fecha)
            return false;
        if (ComboModalidad.SelectedItem is not Opcion<Modalidad> modalidad)
            return false;
        if (ComboMetodo.SelectedItem is not Opcion<MetodoAmortizacion> metodo)
            return false;

        // Cuota donde arranca el capital. Vacío = que lo decida el sistema, la
        // misma regla que el formulario de préstamo nuevo.
        int? inicioCapital = null;
        if (metodo.Valor == MetodoAmortizacion.CapitalDiferido &&
            modalidad.Valor != Modalidad.PagoUnico &&
            !string.IsNullOrWhiteSpace(CajaInicioCapital.Text))
        {
            if (!int.TryParse(CajaInicioCapital.Text, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var inicio) ||
                inicio < 1 || inicio > plazo)
                return false;
            inicioCapital = inicio;
        }

        parametros = new ParametrosAmortizacion(capital, tasa, plazo, modalidad.Valor, metodo.Valor,
            DateOnly.FromDateTime(fecha), inicioCapital);
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

        var leyo = TryLeerMontos(out var parametros);
        if (!leyo && !_datos.Permitido.SoloDescriptivo)
        {
            TextoError.Text = "Revisá el capital, la tasa, las cuotas y la fecha: " +
                              "tienen que ser números válidos mayores que cero.";
            return;
        }

        // Con cobros hechos los montos van bloqueados; se mandan los que ya
        // tenía el préstamo, que además es lo que el servicio va a respetar.
        var actual = _datos.Actual;
        parametros ??= new ParametrosAmortizacion(actual.MontoCapital, actual.TasaInteres,
            actual.PlazoCuotas, actual.Modalidad, actual.MetodoAmortizacion, actual.FechaInicio,
            actual.CuotaInicioCapital);

        Resultado = new EdicionPrestamo(_datos.PrestamoId,
            parametros.MontoCapital, parametros.TasaInteresMensual, parametros.PlazoCuotas,
            parametros.Modalidad, parametros.Metodo, parametros.FechaPrimerPago,
            Garantia: string.IsNullOrWhiteSpace(CajaGarantia.Text) ? null : CajaGarantia.Text.Trim(),
            Notas: string.IsNullOrWhiteSpace(CajaNotas.Text) ? null : CajaNotas.Text.Trim(),
            Motivo: CajaMotivo.Text.Trim(),
            // Sin esto la corrección recalculaba el préstamo diferido con la
            // cuota sugerida en vez de la pactada (bug 2026-08-20).
            CuotaInicioCapital: parametros.CuotaInicioCapital);
        DialogResult = true;
    }

    private void Volver_Click(object sender, RoutedEventArgs e) => Close();

}
