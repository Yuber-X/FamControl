using System.Windows;
using System.Windows.Threading;
using FAControl.Common;
using FAControl.Services;
using FAControl.Views;
using Serilog;

namespace FAControl.App;

/// <summary>
/// Notificador de vencimientos (pedido del cliente, 2026-07-10, estilo POS-400):
/// al iniciar sesión avisa qué clientes se pasaron de su fecha de pago.
/// Reglas:
///  - se muestra UNA vez por arranque;
///  - si la app sigue abierta, vuelve a avisar al cambiar el día de negocio
///    (12:00 AM hora RD) — un timer revisa cada minuto;
///  - cada cliente puede silenciarse individualmente ("No volver a preguntar"),
///    y eso persiste en ajustes.json;
///  - se activa/desactiva desde Configuración.
/// </summary>
public class NotificadorVencidos : IAvisoVencidos
{
    private readonly ClienteService _clientes;
    private readonly AjustesLocales _ajustes;
    private readonly DispatcherTimer _timer;
    private DateOnly? _ultimaFechaAvisada;
    private bool _mostrando;

    public NotificadorVencidos(ClienteService clientes, AjustesLocales ajustes)
    {
        _clientes = clientes;
        _ajustes = ajustes;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _timer.Tick += (_, _) => _ = VerificarAsync();
    }

    /// <summary>Arranca el ciclo: aviso inicial + vigilancia del cambio de día.</summary>
    public void Iniciar()
    {
        _ = VerificarAsync();
        _timer.Start();
    }

    /// <summary>
    /// Fuerza el aviso ignorando el "ya avisé hoy". Lo usa Configuración al
    /// restablecer los silenciados, para que el cambio se vea en el momento.
    /// </summary>
    public Task RevisarAhoraAsync() => VerificarAsync(forzado: true);

    private async Task VerificarAsync(bool forzado = false)
    {
        try
        {
            if (!_ajustes.AvisoVencidosActivo || _mostrando)
                return;

            // Una vez por arranque; se repite solo cuando cambia el día de negocio.
            // Una revisión forzada se salta el guard: si no, tras restablecer los
            // silenciados el aviso no reaparecería hasta mañana.
            var hoy = FechaNegocio.Hoy;
            if (!forzado && _ultimaFechaAvisada == hoy)
                return;
            _ultimaFechaAvisada = hoy;

            var vencidos = (await _clientes.ObtenerClientesConVencidasAsync())
                .Where(v => !_ajustes.AvisoVencidosSilenciados.Contains(v.ClienteId))
                .ToList();
            if (vencidos.Count == 0)
                return;

            _mostrando = true;
            try
            {
                // El aviso corre en segundo plano y puede caer justo cuando el
                // shell se está cerrando: sin ventana viva no hay a quién
                // avisarle, y colgarlo de una cerrada tumbaba la aplicación.
                if (FAControl.Views.VentanaDuena.Principal() is not { } duena)
                    return;

                var ventana = new AvisoVencidosWindow(vencidos) { Owner = duena };
                ventana.ShowDialog();

                var silenciados = ventana.ObtenerSilenciados();
                if (silenciados.Count > 0)
                {
                    _ajustes.AvisoVencidosSilenciados.AddRange(
                        silenciados.Where(id => !_ajustes.AvisoVencidosSilenciados.Contains(id)));
                    _ajustes.Guardar();
                    Log.Information("Aviso de vencidos: {Cantidad} cliente(s) silenciado(s)", silenciados.Count);
                }
            }
            finally
            {
                _mostrando = false;
            }
        }
        catch (Exception ex)
        {
            // El aviso nunca debe impedir usar la aplicación
            Log.Error(ex, "Error en el notificador de vencimientos");
        }
    }
}
