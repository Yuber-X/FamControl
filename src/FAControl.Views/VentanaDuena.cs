using System.Windows;



namespace FAControl.Views;

/// <summary>
/// Quién es el <c>Owner</c> de un diálogo, y si todavía está vivo.
///
/// EL PROBLEMA QUE RESUELVE (cliente 2026-08-20). Al crear un préstamo salía
/// <i>"No se puede establecer la propiedad Owner en un elemento Window que se
/// ha cerrado"</i> y el pagaré no se imprimía. La causa:
///
///   - Los ViewModels de página son SINGLETON; el shell (MainWindow) es
///     TRANSIENT y se rehace en cada "Cerrar sesión" / "Cambiar usuario".
///   - Cada shell nuevo crea su propia instancia de la vista, que se suscribe
///     a los eventos del ViewModel singleton (PagareSolicitado y compañía).
///   - Las vistas de los shells YA CERRADOS nunca se desuscriben: el evento las
///     mantiene vivas. Al dispararse, la primera en atender es la más vieja,
///     que intenta abrir su ventana colgando de un shell cerrado y revienta.
///     Como la excepción corta la lista de invocación, la vista buena —la que
///     está en pantalla— nunca llega a abrir el pagaré.
///
/// La regla: <b>una vista que ya no está en pantalla no abre ventanas</b>. Los
/// manejadores preguntan por <see cref="DeLaVista"/> y, si devuelve null, no
/// hacen nada. La vista viva sí abre la suya y el usuario ve su papel.
/// </summary>
public static class VentanaDuena
{
    /// <summary>True si la ventana existe y sigue abierta (no cerrada).</summary>
    /// <remarks>
    /// WPF no expone un <c>IsClosed</c>. Al cerrarse, la ventana pierde su
    /// <see cref="PresentationSource"/> (su HWND) y su <c>IsLoaded</c> pasa a
    /// false: se comprueban las dos cosas porque la primera también descarta
    /// una ventana creada pero nunca mostrada.
    /// </remarks>
    public static bool Viva(Window? ventana) =>
        ventana is not null &&
        ventana.IsLoaded &&
        PresentationSource.FromVisual(ventana) is not null;

    /// <summary>
    /// La ventana que contiene a esta vista, o <c>null</c> si esa ventana ya se
    /// cerró o la vista quedó fuera del árbol visual (vista huérfana).
    /// </summary>
    public static Window? DeLaVista(DependencyObject vista)
    {
        var ventana = Window.GetWindow(vista);
        return Viva(ventana) ? ventana : null;
    }

    /// <summary>
    /// La ventana principal de la aplicación si está viva. Es el dueño de los
    /// diálogos que no salen de una vista (avisos automáticos, autorizaciones).
    /// Puede ser null en el hueco entre que se cierra un shell y se abre el
    /// siguiente: ahí el diálogo se muestra sin dueño en vez de reventar.
    /// </summary>
    public static Window? Principal()
    {
        var ventana = Application.Current?.MainWindow;
        return Viva(ventana) ? ventana : null;
    }

    /// <summary>
    /// Abre <paramref name="dialogo"/> como modal colgando de la ventana de
    /// <paramref name="vista"/>. Si esa ventana ya se cerró no abre nada y
    /// devuelve null: esta vista es de un shell viejo.
    /// </summary>
    public static bool? MostrarDesde(this Window dialogo, DependencyObject vista)
    {
        if (DeLaVista(vista) is not { } duena)
            return null;

        dialogo.Owner = duena;
        return dialogo.ShowDialog();
    }

    /// <summary>
    /// Abre <paramref name="dialogo"/> colgando de la ventana principal si hay
    /// una viva, y suelto si no la hay. Para los diálogos que NO salen de una
    /// vista: el ticket del punto de venta y el cierre de caja, que los dispara
    /// la App al recibir el evento del ViewModel.
    ///
    /// Aquí sí se muestra sin dueño en vez de no mostrar nada: el papel es la
    /// razón de ser de la operación que acaba de terminar.
    /// </summary>
    public static bool? MostrarDesdeLaPrincipal(this Window dialogo)
    {
        if (Principal() is { } duena)
            dialogo.Owner = duena;
        return dialogo.ShowDialog();
    }
}
