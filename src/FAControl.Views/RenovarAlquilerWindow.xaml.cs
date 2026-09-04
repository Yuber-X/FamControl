using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FAControl.Models;
using FAControl.ViewModels;

namespace FAControl.Views;

/// <summary>
/// Renovación de un alquiler (039 — "preguntar si el cliente seguirá con el
/// alquiler... actualizar su fecha de devolución según el usuario confirme la
/// nueva fecha y precio nuevo o el mismo").
///
/// La tarifa viene propuesta con la vigente: renovar al mismo precio es lo
/// normal, y hacer que el usuario la vuelva a escribir cada vez invita a un
/// tipeo que le cambia el precio al cliente sin que nadie lo note.
///
/// Los días del tramo se cuentan desde el día SIGUIENTE al fin actual: el
/// último día pactado ya está cobrado en el tramo anterior. El cálculo baja
/// como delegado desde el ViewModel porque Views no referencia Services, y así
/// la vista previa da exactamente el mismo número que después se guarda.
/// </summary>
public partial class RenovarAlquilerWindow : Window
{
    private readonly RenovacionAlquilerPedido _datos;

    /// <summary>La renovación confirmada. Solo válida si el diálogo devolvió true.</summary>
    public RenovacionAlquiler? Resultado { get; private set; }

    public RenovarAlquilerWindow(RenovacionAlquilerPedido datos)
    {
        InitializeComponent();
        VentanaAjustable.Ajustar(this);
        ChromeVentana.OcultarBotones(this);
        _datos = datos;

        TextoTitulo.Text = $"Renovar el alquiler {datos.Codigo}";
        TextoSubtitulo.Text =
            $"{datos.VehiculoDescripcion} · {datos.ClienteNombre}. " +
            $"Hoy el contrato va hasta el " +
            $"{datos.FechaFinActual.ToString(Textos.FormatoFecha, Textos.CulturaRd)}; " +
            "el vehículo sigue alquilado y no vuelve al inventario.";

        // Propuesta: una semana más, que es la renovación más común. El usuario
        // la cambia si el cliente pidió otra cosa.
        SelectorFin.SelectedDate = datos.FechaFinActual.AddDays(7).ToDateTime(TimeOnly.MinValue);
        SelectorFin.DisplayDateStart = datos.FechaFinActual.AddDays(1).ToDateTime(TimeOnly.MinValue);
        CajaTarifa.Text = datos.TarifaVigente.ToString("0.##", Textos.CulturaRd);

        ActualizarCuenta();
        SelectorFin.Focus();
    }

    private void Campo_Changed(object sender, SelectionChangedEventArgs e) => ActualizarCuenta();
    private void Tarifa_Changed(object sender, TextChangedEventArgs e) => ActualizarCuenta();

    private void ActualizarCuenta()
    {
        // Los eventos del DatePicker disparan durante InitializeComponent
        if (TextoCuenta is null)
            return;

        TextoTarifa.Text = string.Empty;

        if (!TryLeer(out var fin, out var tarifa))
        {
            TextoCuenta.Text = "Elige la fecha nueva y la tarifa para ver cómo queda.";
            return;
        }
        if (fin <= _datos.FechaFinActual)
        {
            TextoCuenta.Text =
                "La fecha nueva tiene que ser posterior al " +
                $"{_datos.FechaFinActual.ToString(Textos.FormatoFecha, Textos.CulturaRd)}.";
            return;
        }

        if (tarifa != _datos.TarifaVigente)
        {
            TextoTarifa.Text =
                $"Tarifa nueva. Los días que van hasta el " +
                $"{_datos.FechaFinActual.ToString(Textos.FormatoFecha, Textos.CulturaRd)} " +
                $"siguen a {_datos.TarifaVigente.ToString("N2", Textos.CulturaRd)} DOP: " +
                "el precio nuevo rige de aquí en adelante.";
        }

        var (dias, monto) = _datos.Calcular(_datos.FechaFinActual, fin, tarifa);
        TextoCuenta.Text = $"{dias} día(s) más × {tarifa:N2} = {monto:N2} DOP que se suman al contrato.";
    }

    private bool TryLeer(out DateOnly fin, out decimal tarifa)
    {
        fin = default;
        tarifa = 0m;

        if (SelectorFin.SelectedDate is not { } f)
            return false;
        if (!decimal.TryParse(CajaTarifa.Text, NumberStyles.Number, Textos.CulturaRd, out tarifa)
            || tarifa <= 0m)
            return false;

        fin = DateOnly.FromDateTime(f);
        return true;
    }

    private void Confirmar_Click(object sender, RoutedEventArgs e)
    {
        if (!TryLeer(out var fin, out var tarifa))
        {
            TextoError.Text = "Revisa la fecha y la tarifa: la tarifa tiene que ser mayor que cero.";
            return;
        }
        if (fin <= _datos.FechaFinActual)
        {
            TextoError.Text =
                "La fecha nueva tiene que ser posterior al " +
                $"{_datos.FechaFinActual.ToString(Textos.FormatoFecha, Textos.CulturaRd)}.";
            return;
        }

        Resultado = new RenovacionAlquiler(_datos.AlquilerId, fin, tarifa,
            Notas: string.IsNullOrWhiteSpace(CajaNotas.Text) ? null : CajaNotas.Text.Trim());
        DialogResult = true;
    }

    private void Volver_Click(object sender, RoutedEventArgs e) => Close();
}
