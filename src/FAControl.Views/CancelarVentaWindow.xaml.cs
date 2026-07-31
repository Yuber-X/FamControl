using System.Globalization;
using System.Windows;
using FAControl.ViewModels;

namespace FAControl.Views;

/// <summary>
/// Cancelación de una venta financiada: el cliente devolvió el vehículo (028).
///
/// Pide el motivo y el porcentaje que el negocio retiene de lo ya cobrado, con
/// la opción de dejarlo fijo para las próximas. El reparto se muestra en vivo
/// para que el dueño vea cuánta plata devuelve ANTES de confirmar.
///
/// El cálculo que vale es el del servicio: esto es la vista previa. Se usa el
/// mismo redondeo (a favor del negocio, como el resto de la app) para que el
/// número que se ve acá y el que se guarda sean el mismo.
/// </summary>
public partial class CancelarVentaWindow : Window
{
    private readonly decimal _cobrado;

    /// <summary>Motivo escrito por el usuario. Solo válido si el diálogo se confirmó.</summary>
    public string Motivo { get; private set; } = string.Empty;
    public decimal Porcentaje { get; private set; }
    /// <summary>True si pidió que ese porcentaje quede como el propuesto de ahora en más.</summary>
    public bool FijarPorcentaje { get; private set; }

    public CancelarVentaWindow(string codigoVenta, decimal cobrado,
        decimal porcentajeInicial, bool yaEstabaFijo)
    {
        InitializeComponent();
        ChromeVentana.OcultarBotones(this);
        _cobrado = cobrado;

        TextoTitulo.Text = $"Cancelar la venta {codigoVenta}";
        CajaPorcentaje.Text = porcentajeInicial.ToString("0.##", Textos.CulturaRd);
        CasillaFijar.IsChecked = yaEstabaFijo;
        ActualizarReparto();
    }

    private void Porcentaje_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) =>
        ActualizarReparto();

    private void ActualizarReparto()
    {
        TextoCobrado.Text = $"Cobrado hasta ahora (inicial + abonos): {_cobrado:N2} DOP";

        if (!TryLeerPorcentaje(out var porcentaje))
        {
            TextoRetenido.Text = "—";
            TextoDevuelto.Text = string.Empty;
            return;
        }

        var retenido = Math.Round(_cobrado * porcentaje / 100m, 2, MidpointRounding.AwayFromZero);
        TextoRetenido.Text = $"Se queda el negocio: {retenido:N2} DOP ({porcentaje:0.##}%)";
        TextoDevuelto.Text = $"Se le devuelve al cliente: {_cobrado - retenido:N2} DOP";
    }

    private bool TryLeerPorcentaje(out decimal porcentaje) =>
        decimal.TryParse(CajaPorcentaje.Text, NumberStyles.Number, Textos.CulturaRd, out porcentaje)
        && porcentaje is >= 0m and <= 100m;

    private void Confirmar_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(CajaMotivo.Text))
        {
            TextoError.Text = "Escribí el motivo: queda en el historial y es lo que explica la devolución.";
            CajaMotivo.Focus();
            return;
        }
        if (!TryLeerPorcentaje(out var porcentaje))
        {
            TextoError.Text = "El porcentaje tiene que ser un número entre 0 y 100.";
            CajaPorcentaje.Focus();
            return;
        }

        Motivo = CajaMotivo.Text.Trim();
        Porcentaje = porcentaje;
        FijarPorcentaje = CasillaFijar.IsChecked == true;
        DialogResult = true;
    }

    private void Volver_Click(object sender, RoutedEventArgs e) => Close();
}
