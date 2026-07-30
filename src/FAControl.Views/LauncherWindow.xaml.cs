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
/// Sirve para las dos cosas que muestra el launcher:
///  * una ESTANCIA de la suite (Modo con valor), que se abre al tocarla;
///  * la oferta de POS-500 (Modo en null), que no se abre aca.
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

    /// <summary>Columna de una estancia. Habilitado lo decide la licencia.</summary>
    public static TarjetaLanzador DeModo(IdentidadModo identidad, bool habilitado) =>
        new(identidad.Nombre, identidad.Etiqueta, identidad.Descripcion,
            identidad.ColorHex, identidad.ColorBrilloHex,
            Icono: identidad.Modo switch
            {
                ModoApp.PrestControl => "",     // billete
                ModoApp.DealerControl => "",    // vehiculo
                _ => ""
            },
            EstadoTexto: !identidad.Disponible ? "EN DESARROLLO"
                : habilitado ? "DISPONIBLE"
                : "REQUIERE CODIGO",
            Modo: identidad.Modo);

    /// <summary>Columna de POS-500: no se abre, se ofrece (cliente 2026-07-29).</summary>
    public static TarjetaLanzador DePos500(bool comprado) =>
        new(Pos500.Nombre, Pos500.Etiqueta, Pos500.Descripcion,
            Pos500.ColorHex, Pos500.ColorBrilloHex,
            Icono: "",                          // etiqueta de precio
            EstadoTexto: comprado ? "ADQUIRIDO" : "EN VENTA",
            Modo: null);

    private static Color Convertir(string hex) => (Color)ColorConverter.ConvertFromString(hex);
}

/// <summary>
/// Puerta de entrada de la suite (pedido del cliente 2026-07-16): tres columnas.
/// Desde el 2026-07-29 son DOS estancias — PrestControl y DealControl — más la
/// oferta de POS-500, que es un producto aparte y no se abre desde acá.
/// Al elegir una estancia se abre el login de ESE modo.
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

        List<TarjetaLanzador> tarjetas =
        [
            .. IdentidadModo.Todos.Select(m =>
                TarjetaLanzador.DeModo(m, _codigosVm.PermiteModo(m.Modo))),
            TarjetaLanzador.DePos500(_codigosVm.Pos500Comprado)
        ];
        ListaModos.ItemsSource = tarjetas;
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

        // POS-500 no es una estancia de FAControl: es un producto aparte que se
        // ofrece desde acá (cliente 2026-07-29). Tocarlo informa, no abre nada.
        if (tarjeta.Modo is not { } modo)
        {
            MostrarOfertaPos500();
            return;
        }

        // Puerta de la licencia POR MODO: durante la prueba está todo abierto;
        // al terminar, cada estancia pide SU código.
        if (!_codigosVm.PermiteModo(modo))
        {
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
    /// POS-500 a la venta. Si el cliente ya lo compró (código 5) el mensaje
    /// cambia: la instalación la hace el desarrollador, no se abre desde acá.
    /// </summary>
    private void MostrarOfertaPos500()
    {
        var texto = _codigosVm.Pos500Comprado
            ? $"{Pos500.Nombre} ya figura como adquirido en esta computadora.\n\n" +
              $"Se instala aparte de FAControl. Escribí al {Soporte.Telefono} para coordinar " +
              "la instalación."
            : $"{Pos500.Descripcion}\n\n" +
              $"No viene incluido en FAControl. Para cotizarlo o verlo funcionando, escribí " +
              $"al {Soporte.Telefono}.";

        MessageBox.Show(this, texto, $"{Pos500.Nombre} — {Pos500.Etiqueta}",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BotonSalir_Click(object sender, RoutedEventArgs e)
    {
        // Cerrar el launcher cierra la app entera (pedido de Yuber 2026-07-17)
        ModoElegido = null;
        DialogResult = false;
    }
}
