using System.Windows;
using FAControl.Services;
using FAControl.Views;

namespace FAControl.App;

/// <summary>
/// Implementación de IAutorizadorAdmin: muestra la ventana de credenciales y
/// devuelve la autorización validada.
///
/// Vive en App porque es la única capa que puede abrir ventanas Y ver los
/// servicios a la vez. La ventana solo recoge texto; quien decide si la
/// autorización vale es AutorizacionService.
/// </summary>
public class AutorizadorAdmin : IAutorizadorAdmin
{
    private readonly AutorizacionService _autorizacion;

    public AutorizadorAdmin(AutorizacionService autorizacion) => _autorizacion = autorizacion;

    public Task<AutorizacionPrestamo?> PedirAsync(string motivo, CancellationToken ct = default)
    {
        AutorizacionPrestamo? resultado = null;

        var ventana = new AutorizacionWindow(motivo, async (username, password) =>
        {
            resultado = await _autorizacion.ValidarAsync(username, password, ct);
            return resultado is null
                // Mensaje deliberadamente ambiguo: no revela si el usuario
                // existe, si la contraseña falló o si le falta el permiso.
                ? "No se pudo autorizar. Verificá el usuario y la contraseña, " +
                  "y que esa cuenta tenga permiso para autorizar préstamos."
                : null;
        })
        {
            Owner = Application.Current.MainWindow
        };

        return Task.FromResult(ventana.ShowDialog() == true ? resultado : null);
    }
}
