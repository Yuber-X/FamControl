using System.Windows;

namespace FAControl.Views;

/// <summary>
/// Deja una ventana dentro de la pantalla (pedido del cliente 2026-09-04:
/// "hacer la ventana auto-ajustable... el usuario no tiene alcance a
/// pequeñecerla").
///
/// EL PROBLEMA. Varias ventanas se abrían con alto fijo o con
/// <c>SizeToContent="Height"</c> y <c>ResizeMode="NoResize"</c>. En un monitor
/// grande se veían bien; en una laptop de 768px de alto —que es lo que hay en
/// el mostrador— el formulario crecía por debajo del borde de la pantalla, con
/// los botones de guardar fuera de vista y sin forma de achicar la ventana ni
/// de hacer scroll. La ventana quedaba inservible.
///
/// LA REGLA: una ventana NORMAL no puede ser más alta ni más ancha que el área
/// de trabajo (la pantalla menos la barra de tareas). Lo que no entre, se
/// scrollea.
///
/// OJO CON LAS MAXIMIZADAS. El tope se quita mientras la ventana está
/// maximizada: WPF respeta MaxWidth/MaxHeight también al maximizar, así que un
/// tope fijo dejaba al Launcher abriendo en una ventana chica en vez de a
/// pantalla completa (bug reportado el 2026-09-04, introducido ese mismo día
/// por la primera versión de esta clase).
/// </summary>
public static class VentanaAjustable
{
    /// <summary>
    /// Limita la ventana al área de trabajo mientras esté en estado normal.
    /// Se llama en el constructor, después de InitializeComponent. El margen
    /// deja aire para que no quede pegada a los bordes.
    /// </summary>
    public static void Ajustar(Window ventana, double margen = 60)
    {
        void Aplicar()
        {
            if (ventana.WindowState != WindowState.Normal)
            {
                // Maximizada (o minimizada): sin tope. Con uno puesto, la
                // ventana maximizada se queda del tamaño del tope.
                ventana.MaxWidth = double.PositiveInfinity;
                ventana.MaxHeight = double.PositiveInfinity;
                return;
            }

            var anchoUtil = Math.Max(320, SystemParameters.WorkArea.Width - margen);
            var altoUtil = Math.Max(320, SystemParameters.WorkArea.Height - margen);

            ventana.MaxWidth = anchoUtil;
            ventana.MaxHeight = altoUtil;

            // SizeToContent no respeta MaxHeight por sí solo cuando el contenido
            // crece: hay que recortar el alto ya calculado.
            if (!double.IsNaN(ventana.Width) && ventana.Width > anchoUtil)
                ventana.Width = anchoUtil;
            if (!double.IsNaN(ventana.Height) && ventana.Height > altoUtil)
                ventana.Height = altoUtil;
            if (ventana.IsLoaded)
            {
                if (ventana.ActualWidth > anchoUtil)
                    ventana.Width = anchoUtil;
                if (ventana.ActualHeight > altoUtil)
                    ventana.Height = altoUtil;
            }
        }

        Aplicar();
        // Al cargarse, el alto real ya está resuelto (SizeToContent incluido):
        // es el único momento en que se puede comprobar de verdad.
        ventana.Loaded += (_, _) => Aplicar();
        // Y al maximizar o restaurar hay que poner o quitar el tope.
        ventana.StateChanged += (_, _) => Aplicar();
    }
}
