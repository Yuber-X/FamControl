using System.Windows;
using System.Windows.Controls;
using FAControl.ViewModels;

namespace FAControl.Views;

public partial class HistorialView : UserControl
{
    public HistorialView() => InitializeComponent();

    // Abre la ficha con la descripción completa de la fila (lógica de UI).
    private void VerFicha_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not AuditoriaFila fila)
            return;

        var entidad = fila.EntidadIdTexto == "—"
            ? fila.EntidadTexto
            : $"{fila.EntidadTexto} (#{fila.EntidadIdTexto})";

        new HistorialFichaWindow(fila.FechaTexto, fila.UsuarioTexto, fila.AccionTexto,
            entidad, fila.DescripcionTexto).MostrarDesde(this);
    }
}
