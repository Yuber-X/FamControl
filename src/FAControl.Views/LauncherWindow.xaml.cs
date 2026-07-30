using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FAControl.Common;
using FAControl.ViewModels;

namespace FAControl.Views;

/// <summary>
/// Una columna del launcher, ya en el lenguaje del XAML (Brushes y Colors).
/// Vive en Views porque es mapeo de PRESENTACION: Common no conoce WPF.
///
/// Una por estancia de la suite. El POS-500 es una estancia mas desde el
/// 2026-07-30; lo unico distinto es que, sin comprar, la columna ofrece el
/// producto en vez de mostrar un candado.
/// </summary>
public record TarjetaLanzador(
    string Nombre,
    string Etiqueta,
    string Descripcion,
    string ColorHex,
    string ColorBrilloHex,
    string Icono,
    string EstadoTexto,
    ModoApp? Modo)
{
    public Brush Acento => new SolidColorBrush(Convertir(ColorHex));

    /// <summary>El acento al 18%: fondo del icono y de la pildora de estado.</summary>
    public Brush AcentoSutil => new SolidColorBrush(Convertir(ColorHex)) { Opacity = 0.18 };

    /// <summary>Color del glow al pasar el mouse: el propio, mas oscuro.</summary>
    public Color ColorGlow => Convertir(ColorBrilloHex);

    /// <summary>
    /// Columna de una estancia. <c>habilitado</c> lo decide la licencia.
    /// POS-500 se muestra distinto cuando no esta comprado: no es que "falte un
    /// codigo", es que todavia no se compro — la columna lo ofrece.
    /// </summary>
    public static TarjetaLanzador DeModo(IdentidadModo identidad, bool habilitado) =>
        new(identidad.Nombre, identidad.Etiqueta, identidad.Descripcion,
            identidad.ColorHex, identidad.ColorBrilloHex,
            Icono: identidad.Modo switch
            {
                ModoApp.PrestControl => "",     // billete
                ModoApp.DealerControl => "",    // vehiculo
                ModoApp.Pos500 => "",           // etiqueta de precio
                _ => ""
            },
            EstadoTexto: !identidad.Disponible ? "EN DESARROLLO"
                : habilitado ? "DISPONIBLE"
                : identidad.Modo == ModoApp.Pos500 ? "EN VENTA"
                : "REQUIERE CODIGO",
            Modo: identidad.Modo);

    private static Color Convertir(string hex) => (Color)ColorConverter.ConvertFromString(hex);
}

/// <summary>
/// Puerta de entrada de la suite (pedido del cliente 2026-07-16): tres columnas,
/// una por estancia — PrestControl, DealControl y POS-500. Al elegir una se abre
/// el login de ESE modo. La que la licencia no habilite no entra; el POS-500 sin
/// comprar muestra la oferta en vez del candado.
///
/// No sigue el tema claro/oscuro a propósito: es la cara de la marca Familia
/// Almonte y siempre va en navy.
/// </summary>
public partial class LauncherWindow : Window
{
    private readonly CodigosViewModel _codigosVm;
    private readonly AjustesLocales _ajustes;

    /// <summary>El modo elegido. Null si el usuario cerró sin elegir.</summary>
    public ModoApp? ModoElegido { get; private set; }

    public LauncherWindow(CodigosViewModel codigosVm, AjustesLocales ajustes)
    {
        InitializeComponent();
        _codigosVm = codigosVm;
        _ajustes = ajustes;
        ChromeVentana.OcultarBotones(this);
        ContenedorLogo.Content = new LogoFA { Width = 92, Height = 92 };
        // Si el arranque directo está prendido, el usuario llegó acá por "cerrar
        // sesión": la casilla tiene que reflejar que sigue prendido.
        CasillaArranqueDirecto.IsChecked = _ajustes.ArranqueDirecto is not null;
        MostrarLicencia();
    }

    /// <summary>
    /// Leyenda del pie y estado de cada columna. Se rehacen las tarjetas porque
    /// el candado de cada modo depende de la licencia, y la licencia puede
    /// cambiar sin cerrar el launcher (el usuario acaba de digitar un código).
    /// </summary>
    private void MostrarLicencia()
    {
        _codigosVm.RefrescarEstado();
        TextoLicencia.Text = _codigosVm.PermiteUsar
            ? $"Elegí un modo para iniciar sesión.  ·  {_codigosVm.EstadoTexto}"
            : _codigosVm.EstadoTexto;

        ListaModos.ItemsSource = IdentidadModo.Todos
            .Select(m => TarjetaLanzador.DeModo(m, _codigosVm.PermiteModo(m.Modo)))
            .ToList();
    }

    /// <summary>
    /// Puerta de la licencia (pedido del cliente 2026-07-27): con la prueba
    /// vencida o sin activar, el launcher se abre igual — hay que poder digitar
    /// el código — pero no deja entrar a ningún modo.
    /// </summary>
    private void BotonCodigos_Click(object sender, RoutedEventArgs e)
    {
        var ventana = new CodigosWindow(_codigosVm) { Owner = this };
        ventana.ShowDialog();
        MostrarLicencia();
    }

    /// <summary>Ayuda y soporte: el número del desarrollador (cliente 2026-07-29).</summary>
    private void BotonAyuda_Click(object sender, RoutedEventArgs e) =>
        new AyudaWindow { Owner = this }.ShowDialog();

    private void Modo_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not TarjetaLanzador tarjeta)
            return;

        if (tarjeta.Modo is not { } modo)
            return;

        // Módulo que todavía no se puede abrir: honesto en vez de un login que
        // no lleva a ningún lado.
        if (!IdentidadModo.De(modo).Disponible)
        {
            MessageBox.Show(this,
                modo == ModoApp.Pos500
                    ? $"{Pos500.Descripcion}\n\nYa viene instalado con FAControl y se está " +
                      "terminando de integrar a la suite. En cuanto esté, se habilita con su " +
                      $"código, sin reinstalar nada.\n\nConsultas al {Soporte.Telefono}."
                    : $"{tarjeta.Nombre} todavía está en desarrollo.\n\n{tarjeta.Descripcion}",
                $"{tarjeta.Nombre}", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Puerta de la licencia POR MODO: durante la prueba está todo abierto;
        // al terminar, cada estancia pide SU código.
        if (!_codigosVm.PermiteModo(modo))
        {
            // El POS-500 se vende aparte: sin comprar no es "te falta un código",
            // es una oferta. Los otros dos son módulos que el cliente ya tiene.
            if (modo == ModoApp.Pos500)
            {
                MostrarOfertaPos500();
                return;
            }

            MessageBox.Show(this,
                $"{tarjeta.Nombre} no está activado en esta computadora.\n\n" +
                $"{_codigosVm.EstadoTexto}.\n\n" +
                "Usá el botón \"Ingresar código\" para activarlo o para iniciar la prueba.",
                $"{tarjeta.Nombre} no está activado", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Arranque directo: la casilla se aplica al modo que se acaba de elegir.
        // Desmarcada, apaga el que hubiera guardado (así el launcher también
        // sirve para volver atrás, no solo Configuración).
        _ajustes.FijarArranqueDirecto(
            CasillaArranqueDirecto.IsChecked == true ? modo : null);
        _ajustes.Guardar();

        ModoElegido = modo;
        DialogResult = true;
    }

    /// <summary>
    /// POS-500 a la venta: ya viene instalado con FAControl, solo hay que
    /// comprarlo. Se habilita con el código 5, sin volver a instalar nada.
    /// </summary>
    private void MostrarOfertaPos500() =>
        MessageBox.Show(this,
            $"{Pos500.Descripcion}\n\n" +
            "Ya viene instalado con FAControl: para usarlo solo hace falta el código de " +
            $"activación.\n\nPara cotizarlo o verlo funcionando, escribí al {Soporte.Telefono}.",
            $"{Pos500.Nombre} — {Pos500.Etiqueta}",
            MessageBoxButton.OK, MessageBoxImage.Information);

    private void BotonSalir_Click(object sender, RoutedEventArgs e)
    {
        // Cerrar el launcher cierra la app entera (pedido de Yuber 2026-07-17)
        ModoElegido = null;
        DialogResult = false;
    }
}
