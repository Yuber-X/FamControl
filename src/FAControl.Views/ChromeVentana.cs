using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FAControl.Views;

/// <summary>
/// Oculta los botones nativos de Windows (minimizar / maximizar / cerrar)
/// quitando el estilo WS_SYSMENU. Se aplica a TODAS las ventanas menos el login
/// (pedido de Yuber 2026-07-18): el cliente podría cerrar con la X y confundirse,
/// creyendo que sigue con sesión; FAControl tiene su propio "Cerrar sesión".
///
/// Mantiene la barra de título (arrastrar), solo elimina los tres botones.
/// </summary>
public static class ChromeVentana
{
    private const int GWL_STYLE = -16;
    private const int WS_SYSMENU = 0x00080000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    /// <summary>Llamar en el constructor de la ventana (tras InitializeComponent).</summary>
    public static void OcultarBotones(Window ventana)
    {
        ventana.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(ventana).Handle;
            var estilo = GetWindowLong(hwnd, GWL_STYLE);
            SetWindowLong(hwnd, GWL_STYLE, estilo & ~WS_SYSMENU);
        };
    }
}
