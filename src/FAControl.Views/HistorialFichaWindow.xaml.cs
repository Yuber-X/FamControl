using System.Windows;

namespace FAControl.Views;

/// <summary>
/// Ficha de una entrada de auditoría (pedido de Yuber 2026-07-18): muestra todos
/// los campos y la DESCRIPCIÓN completa, que en el grid nunca se ve entera. Le da
/// al Admin mejor control de lo que hicieron sus usuarios.
/// </summary>
public partial class HistorialFichaWindow : Window
{
    public HistorialFichaWindow(string fecha, string usuario, string accion,
        string entidad, string descripcion)
    {
        InitializeComponent();
        VentanaAjustable.Ajustar(this);
        ChromeVentana.OcultarBotones(this);
        ValorFecha.Text = fecha;
        ValorUsuario.Text = usuario;
        ValorAccion.Text = accion;
        ValorEntidad.Text = entidad;
        ValorDescripcion.Text = descripcion;
    }

    private void BotonCerrar_Click(object sender, RoutedEventArgs e) => Close();
}
