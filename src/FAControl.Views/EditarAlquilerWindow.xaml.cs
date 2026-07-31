using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FAControl.Models;
using FAControl.ViewModels;

namespace FAControl.Views;

/// <summary>
/// Corrección de un alquiler ya registrado (031 — "así si se produce un error
/// de digitación se pueda arreglar").
///
/// Los días y el total NO se editan: son derivados de las fechas y la tarifa, y
/// se recalculan con la misma cuenta que usa el servicio. Dejarlos escribir a
/// mano permitiría guardar un contrato que se contradice a sí mismo.
///
/// El cálculo baja como delegado desde el ViewModel: Views no referencia
/// Services (lo impide el grafo de proyectos, a propósito).
/// </summary>
public partial class EditarAlquilerWindow : Window
{
    private readonly AlquilerParaEditar _datos;

    /// <summary>La corrección confirmada. Solo válida si el diálogo devolvió true.</summary>
    public EdicionAlquiler? Resultado { get; private set; }

    public EditarAlquilerWindow(AlquilerParaEditar datos)
    {
        InitializeComponent();
        ChromeVentana.OcultarBotones(this);
        _datos = datos;

        TextoTitulo.Text = $"Corregir el alquiler {datos.Codigo}";

        var actual = datos.Actual;
        SelectorInicio.SelectedDate = actual.FechaInicio.ToDateTime(TimeOnly.MinValue);
        SelectorFin.SelectedDate = actual.FechaFin.ToDateTime(TimeOnly.MinValue);
        CajaTarifa.Text = actual.TarifaDia.ToString("0.##", Textos.CulturaRd);
        CajaNotas.Text = actual.Notas ?? string.Empty;

        ActualizarCuenta();
        CajaMotivo.Focus();
    }

    private void Campo_Changed(object sender, SelectionChangedEventArgs e) => ActualizarCuenta();
    private void Tarifa_Changed(object sender, TextChangedEventArgs e) => ActualizarCuenta();

    private void ActualizarCuenta()
    {
        // Los eventos de los DatePicker disparan durante InitializeComponent
        if (TextoCuenta is null)
            return;

        if (!TryLeer(out var inicio, out var fin, out var tarifa))
        {
            TextoCuenta.Text = "Completá las fechas y la tarifa para ver cómo queda.";
            return;
        }
        if (fin < inicio)
        {
            TextoCuenta.Text = "La fecha de fin no puede ser anterior a la de inicio.";
            return;
        }

        var (dias, total) = _datos.Calcular(inicio, fin, tarifa);
        TextoCuenta.Text = $"{dias} día(s) × {tarifa:N2} = {total:N2} DOP";
    }

    private bool TryLeer(out DateOnly inicio, out DateOnly fin, out decimal tarifa)
    {
        inicio = default; fin = default; tarifa = 0m;

        if (SelectorInicio.SelectedDate is not { } i || SelectorFin.SelectedDate is not { } f)
            return false;
        if (!decimal.TryParse(CajaTarifa.Text, NumberStyles.Number, Textos.CulturaRd, out tarifa)
            || tarifa <= 0m)
            return false;

        inicio = DateOnly.FromDateTime(i);
        fin = DateOnly.FromDateTime(f);
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
        if (!TryLeer(out var inicio, out var fin, out var tarifa))
        {
            TextoError.Text = "Revisá las fechas y la tarifa: la tarifa tiene que ser mayor que cero.";
            return;
        }
        if (fin < inicio)
        {
            TextoError.Text = "La fecha de fin no puede ser anterior a la de inicio.";
            return;
        }

        Resultado = new EdicionAlquiler(_datos.AlquilerId, inicio, fin, tarifa,
            Notas: string.IsNullOrWhiteSpace(CajaNotas.Text) ? null : CajaNotas.Text.Trim(),
            Motivo: CajaMotivo.Text.Trim());
        DialogResult = true;
    }

    private void Volver_Click(object sender, RoutedEventArgs e) => Close();
}
