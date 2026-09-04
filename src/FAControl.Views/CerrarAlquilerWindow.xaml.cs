using System.Windows;
using System.Windows.Controls;
using FAControl.Models;
using FAControl.ViewModels;

namespace FAControl.Views;

/// <summary>
/// Cierre de un alquiler (031). UN solo diálogo para las dos formas de
/// terminar, como pidió el cliente ("con un solo btn seria suficiente"), pero
/// preguntando cuál es.
///
/// POR QUÉ SE PREGUNTA EN VEZ DE ASUMIR: por dentro las dos liberan el vehículo,
/// pero DEVUELTO es plata ganada y CANCELADO puede ser plata a devolver.
/// Elegir por el usuario perdería esa diferencia justo en los reportes.
///
/// La cuenta se muestra en vivo, con el mismo redondeo del servicio, para que
/// el mostrador vea cuánto corresponde cobrar ANTES de confirmar — sobre todo
/// cuando el cliente devolvió tarde.
/// </summary>
public partial class CerrarAlquilerWindow : Window
{
    private readonly CierreAlquilerPedido _pedido;

    /// <summary>Lo confirmado. Solo válido si el diálogo devolvió true.</summary>
    public CierreAlquilerDatos? Resultado { get; private set; }

    public CerrarAlquilerWindow(CierreAlquilerPedido pedido)
    {
        InitializeComponent();
        VentanaAjustable.Ajustar(this);
        ChromeVentana.OcultarBotones(this);
        _pedido = pedido;

        TextoTitulo.Text = $"Cerrar el alquiler {pedido.Codigo}";
        TextoVehiculo.Text = pedido.VehiculoDescripcion;
        SelectorFecha.SelectedDate = DateTime.Today;

        ActualizarCuenta();
        CajaMotivo.Focus();
    }

    private void Opcion_Changed(object sender, RoutedEventArgs e) => ActualizarCuenta();
    private void Fecha_Changed(object sender, SelectionChangedEventArgs e) => ActualizarCuenta();

    private bool EsCancelacion => OpcionCancelado?.IsChecked == true;

    private void ActualizarCuenta()
    {
        // Los Checked disparan durante InitializeComponent, antes de que existan
        // los demás controles.
        if (TextoPactado is null)
            return;

        BotonConfirmar.Style = (Style)FindResource(
            EsCancelacion ? "Boton.Destructivo" : "Boton.Primario");
        BotonConfirmar.Content = EsCancelacion ? "Cancelar el alquiler" : "Registrar la devolución";

        if (EsCancelacion)
        {
            // En una cancelación no hay días usados: el contrato no corrió.
            ZonaFecha.Visibility = Visibility.Collapsed;
            TextoPactado.Text = $"Estaba pactado en {_pedido.DiasPactados} día(s) por " +
                                $"{_pedido.MontoPactado:N2} DOP.";
            TextoReal.Text = "El contrato no corrió: no se cuenta como ingreso.";
            TextoDiferencia.Text = "Si el cliente ya había pagado algo, la devolución se maneja aparte.";
            return;
        }

        ZonaFecha.Visibility = Visibility.Visible;

        if (SelectorFecha.SelectedDate is not { } fecha)
        {
            TextoReal.Text = "Elige el día en que devolvió el vehículo.";
            TextoDiferencia.Text = string.Empty;
            return;
        }

        var (dias, total) = CalcularCuenta(DateOnly.FromDateTime(fecha));

        TextoPactado.Text = $"Pactado: {_pedido.DiasPactados} día(s) por {_pedido.MontoPactado:N2} DOP " +
                            $"({_pedido.TarifaDia:N2} por día).";
        TextoReal.Text = $"Real: {dias} día(s) → corresponde cobrar {total:N2} DOP.";

        var diferencia = total - _pedido.MontoPactado;
        TextoDiferencia.Text = diferencia switch
        {
            0m => "Coincide con lo pactado.",
            > 0m => $"Son {diferencia:N2} DOP MÁS de lo pactado " +
                    $"({dias - _pedido.DiasPactados} día(s) de atraso).",
            _ => $"Son {-diferencia:N2} DOP menos de lo pactado (devolvió antes)."
        };
    }

    /// <summary>
    /// La misma cuenta del servicio (días mínimo 1, redondeo a favor del
    /// negocio): lo que se ve aquí tiene que ser exactamente lo que se guarda.
    /// </summary>
    private (int Dias, decimal Total) CalcularCuenta(DateOnly devolucion)
    {
        var dias = Math.Max(1, devolucion.DayNumber - _pedido.FechaInicio.DayNumber);
        return (dias, Math.Round(_pedido.TarifaDia * dias, 2, MidpointRounding.AwayFromZero));
    }

    private void Confirmar_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(CajaMotivo.Text))
        {
            TextoError.Text = "Escribe el motivo: queda en el historial y es lo que explica el cierre.";
            CajaMotivo.Focus();
            return;
        }

        DateOnly? fechaDevolucion = null;
        if (!EsCancelacion)
        {
            if (SelectorFecha.SelectedDate is not { } fecha)
            {
                TextoError.Text = "Elige el día en que devolvió el vehículo.";
                return;
            }
            if (DateOnly.FromDateTime(fecha) < _pedido.FechaInicio)
            {
                TextoError.Text = "La devolución no puede ser anterior al inicio del alquiler.";
                return;
            }
            fechaDevolucion = DateOnly.FromDateTime(fecha);
        }

        Resultado = new CierreAlquilerDatos(_pedido.AlquilerId,
            EsCancelacion ? CierreAlquiler.Cancelado : CierreAlquiler.Devuelto,
            CajaMotivo.Text.Trim(), fechaDevolucion);
        DialogResult = true;
    }

    private void Volver_Click(object sender, RoutedEventArgs e) => Close();
}
