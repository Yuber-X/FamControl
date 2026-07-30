using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using FAControl.Common;
using FAControl.Data;
using FAControl.Services;
using FAControl.ViewModels;
using FAControl.Views;
using Serilog;

namespace FAControl.App;

/// <summary>
/// Bootstrap: Serilog + contenedor de dependencias + flujo login → shell.
/// ShutdownMode es OnExplicitShutdown porque cerramos la LoginWindow
/// antes de abrir el MainWindow.
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _servicios;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ConfigurarSerilog();
        _servicios = ConfigurarServicios();

        // El tema ya NO es global: depende del MODO elegido (DealControl arranca
        // en modo noche). Se aplica en CicloDeVidaAsync apenas se elige el modo,
        // antes del login de esa estancia, para que no parpadee.

        Log.Information("FAControl iniciando");

        // Sin base de datos operativa no se muestra ninguna ventana:
        // diagnóstico con mensajes claros (y creación automática si falta)
        if (!await PrepararBaseDatosAsync())
        {
            Shutdown();
            return;
        }

        // El ciclo de vida se maneja a mano: nada de OnMainWindowClose, porque
        // las ventanas van y vienen (launcher → login → shell → launcher...).
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        await CicloDeVidaAsync();
    }

    /// <summary>
    /// Ciclo completo pedido por Yuber (2026-07-17):
    ///   launcher → (elegir modo) → login → shell
    ///   shell "cerrar sesión"   → vuelve al LAUNCHER
    ///   shell "cambiar usuario" → vuelve al LOGIN del mismo modo
    ///   cerrar el launcher      → cierra la aplicación
    /// </summary>
    private async Task CicloDeVidaAsync()
    {
        // Arranque directo (cliente 2026-07-29): si el usuario marcó "abrir
        // siempre este modo", la primera vuelta se salta el launcher. Solo la
        // PRIMERA: al cerrar sesión vuelve a verlo, para poder cambiar de
        // estancia sin tener que entrar a Configuración.
        var directo = _servicios!.GetRequiredService<AjustesLocales>().ArranqueDirecto;
        // …salvo que la licencia no habilite ESE modo: ahí hace falta el
        // launcher para poder digitar el código que corresponda.
        if (directo is { } fijado
            && !_servicios!.GetRequiredService<LicenciaService>().PermiteModo(fijado))
            directo = null;

        while (true)
        {
            ModoApp? modo;
            if (directo is { } modoDirecto)
            {
                modo = modoDirecto;
                directo = null;
                Log.Information("Arranque directo en {Modo} (el usuario lo dejó fijado)", modoDirecto);
            }
            else
            {
                modo = MostrarLauncher();
            }

            if (modo is null)
                break;                       // cerró el launcher → se acaba la app

            // El tema de la estancia elegida (DealControl arranca oscuro) se
            // aplica ya: así el login de ese modo aparece con su tema, sin flicker.
            Tema.Aplicar(_servicios!.GetRequiredService<AjustesLocales>().TemaOscuroDe(modo.Value));

            // Se queda en el login de ESTE modo hasta que entre o cancele.
            // "Cambiar usuario" vuelve acá sin pasar por el launcher.
            var volverAlLauncher = false;
            while (!volverAlLauncher)
            {
                if (!MostrarLogin(modo.Value))
                    break;                   // canceló el login → vuelve al launcher

                volverAlLauncher = await AbrirShellAsync(modo.Value);
            }
        }

        Shutdown();
    }

    /// <summary>Devuelve el modo elegido, o null si el usuario cerró el launcher.</summary>
    private ModoApp? MostrarLauncher()
    {
        var launcher = _servicios!.GetRequiredService<LauncherWindow>();
        MainWindow = launcher;
        return launcher.ShowDialog() == true ? launcher.ModoElegido : null;
    }

    /// <summary>True si el login fue exitoso; false si se canceló.</summary>
    private bool MostrarLogin(ModoApp modo)
    {
        var login = _servicios!.GetRequiredService<LoginWindow>();
        // El VM se toma de la ventana, NO del contenedor: ambos son transient,
        // así que pedirlo aparte devolvería OTRA instancia y nos suscribiríamos
        // a un LoginExitoso que nunca se dispara.
        var loginVm = (LoginViewModel)login.DataContext;
        loginVm.Modo = modo;   // decide qué acceso se exige (puerta por modo)
        login.MostrarModo(IdentidadModo.De(modo));
        MainWindow = login;

        var entro = false;
        loginVm.LoginExitoso += (_, _) =>
        {
            entro = true;
            login.DialogResult = true;
        };
        login.ShowDialog();
        return entro;
    }

    /// <summary>
    /// Abre el shell y espera a que se cierre.
    /// True  = hay que volver al launcher (cerrar sesión).
    /// False = hay que volver al login del mismo modo (cambiar usuario).
    /// </summary>
    private async Task<bool> AbrirShellAsync(ModoApp modo)
    {
        var servicios = _servicios!;
        var shell = servicios.GetRequiredService<MainWindow>();
        var ajustes = servicios.GetRequiredService<AjustesLocales>();
        var mainVm = servicios.GetRequiredService<MainViewModel>();

        // El shell es nuevo pero el VM es el mismo: hay que reajustarlo al
        // usuario y al MODO activos (ambos cambian qué muestra el sidebar).
        mainVm.EstablecerModo(modo);

        shell.AplicarEscala(ajustes.FactorEscala);
        shell.MostrarModo(IdentidadModo.De(modo));

        var configuracionVm = servicios.GetRequiredService<ConfiguracionViewModel>();
        configuracionVm.EscalaCambiada += shell.AplicarEscala;
        configuracionVm.TemaCambiado += Tema.Aplicar;

        MainWindow = shell;
        shell.Show();
        await mainVm.InicializarAsync();

        // Export automático a Excel (si está activo y toca) — en segundo plano
        _ = servicios.GetRequiredService<ExportacionService>().EjecutarAutomaticoSiTocaAsync(ajustes);
        // Respaldo automático de la BD (si está activo y toca) — en segundo plano
        _ = servicios.GetRequiredService<RespaldoService>().EjecutarAutomaticoSiTocaAsync(ajustes);
        // Recordatorios por correo (si están activos y toca) — en segundo plano
        _ = servicios.GetRequiredService<RecordatorioService>().EjecutarAutomaticoSiTocaAsync();
        // Aviso de clientes pasados de fecha
        servicios.GetRequiredService<NotificadorVencidos>().Iniciar();

        // Espera a que el shell se cierre sin bloquear el hilo de UI
        var cerrado = new TaskCompletionSource<bool>();
        shell.Closed += (_, _) => cerrado.TrySetResult(shell.VolverAlLauncher);
        var volverAlLauncher = await cerrado.Task;

        // Se desuscribe: el shell muere acá y dejarlo enganchado al VM singleton
        // haría que Configuración escale una ventana ya cerrada.
        configuracionVm.EscalaCambiada -= shell.AplicarEscala;
        configuracionVm.TemaCambiado -= Tema.Aplicar;

        return volverAlLauncher;
    }

    /// <summary>
    /// Diagnóstico previo al login. Los MessageBox van directo aquí (bootstrap,
    /// capa UI, aún no hay ventanas): IDialogService es para los ViewModels.
    /// Devuelve false cuando la app no debe continuar.
    /// </summary>
    private async Task<bool> PrepararBaseDatosAsync()
    {
        const string titulo = "FAControl";
        try
        {
            var verificador = _servicios!.GetRequiredService<VerificadorBaseDatos>();
            switch (await verificador.VerificarAsync())
            {
                case EstadoBaseDatos.Lista:
                    return true;

                case EstadoBaseDatos.FaltaBaseDatos:
                    var crear = MessageBox.Show(
                        "La base de datos de FAControl todavía no existe en este equipo.\n\n" +
                        "¿Quieres crearla ahora? Toma solo unos segundos y no afecta nada más del sistema.",
                        titulo + " — Primer arranque",
                        MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
                    if (!crear)
                        return false;

                    await verificador.CrearEsquemaAsync();
                    Log.Information("Base de datos creada automáticamente en el primer arranque");
                    MessageBox.Show(
                        "Base de datos creada correctamente. ¡Todo listo para empezar!",
                        titulo, MessageBoxButton.OK, MessageBoxImage.Information);
                    return true;

                case EstadoBaseDatos.CredencialesInvalidas:
                    MessageBox.Show(
                        "MySQL rechazó el usuario o la contraseña configurados.\n\n" +
                        "Revisa la cadena de conexión en FAControl.App.dll.config " +
                        "(paso 6 de la guía de instalación).",
                        titulo, MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;

                default: // SinServidor
                    MessageBox.Show(
                        "No se pudo conectar con MySQL.\n\n" +
                        "Verifica que el servicio MySQL80 esté en ejecución " +
                        "(services.msc → MySQL80 → Iniciar) y vuelve a abrir FAControl.",
                        titulo, MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Fallo preparando la base de datos al arrancar");
            MessageBox.Show(
                "No se pudo preparar la base de datos:\n\n" + ex.Message + "\n\n" +
                "Si el usuario configurado no tiene permisos para crear bases de datos, " +
                "ejecuta scripts\\db\\001_create_schema.sql como root (ver INSTALL.md).",
                titulo, MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Cierre de sesión de respaldo si el usuario cerró la ventana sin logout
        try
        {
            var auth = _servicios?.GetService<AuthService>();
            auth?.LogoutAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "No se pudo registrar el logout al salir");
        }

        Log.Information("FAControl finalizado");
        Log.CloseAndFlush();
        _servicios?.Dispose();
        base.OnExit(e);
    }

    private static void ConfigurarSerilog()
    {
        var carpetaLogs = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(carpetaLogs);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(carpetaLogs, "facontrol-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30)
            .CreateLogger();
    }

    private static ServiceProvider ConfigurarServicios()
    {
        var servicios = new ServiceCollection();

        // Data
        servicios.AddSingleton<ConexionFactory>();
        servicios.AddSingleton<VerificadorBaseDatos>();
        servicios.AddSingleton<UsuarioRepository>();
        servicios.AddSingleton<SesionRepository>();
        servicios.AddSingleton<AuditoriaRepository>();
        servicios.AddSingleton<ClienteRepository>();
        servicios.AddSingleton<PrestamoRepository>();
        servicios.AddSingleton<PagoRepository>();
        servicios.AddSingleton<ContadorRepository>();
        servicios.AddSingleton<DashboardRepository>();
        servicios.AddSingleton<ReporteRepository>();
        servicios.AddSingleton<ExportacionRepository>();
        servicios.AddSingleton<VehiculoRepository>();
        servicios.AddSingleton<VentaVehiculoRepository>();
        servicios.AddSingleton<AlquilerRepository>();
        servicios.AddSingleton<VehiculoGastoRepository>();
        servicios.AddSingleton<NcfRepository>();
        servicios.AddSingleton<PanelDealRepository>();
        servicios.AddSingleton<ClienteDealRepository>();
        servicios.AddSingleton<DocumentoVentaRepository>();
        servicios.AddSingleton<VehiculoReparacionRepository>();
        servicios.AddSingleton<VentaPlazoRepository>();
        servicios.AddSingleton<ReporteDealRepository>();

        // ---- Punto de venta (POS-500) ----
        // Su propia base: ConexionPos500 es un tipo distinto justamente para que
        // el contenedor no le pase por error la conexión de préstamos.
        servicios.AddSingleton<ConexionPos500>();
        servicios.AddSingleton<VerificadorPos500>();
        servicios.AddSingleton<FAControl.Data.Pos.ClienteRepository>();
        servicios.AddSingleton<FAControl.Data.Pos.ProductoRepository>();
        servicios.AddSingleton<FAControl.Data.Pos.FacturaRepository>();
        servicios.AddSingleton<FAControl.Data.Pos.CuadreRepository>();
        servicios.AddSingleton<FAControl.Data.Pos.AnaliticaRepository>();
        servicios.AddSingleton<FAControl.Data.Pos.ConfiguracionNegocioRepository>();
        servicios.AddSingleton<FAControl.Data.Pos.ExportacionRepository>();

        // Services
        servicios.AddSingleton<AuditoriaService>();
        servicios.AddSingleton<AuthService>();
        servicios.AddSingleton<UsuarioService>();
        servicios.AddSingleton<AutorizacionService>();
        // La ventana de autorización la abre App: ViewModels no puede
        servicios.AddSingleton<IAutorizadorAdmin, AutorizadorAdmin>();
        servicios.AddSingleton<AmortizacionService>();
        servicios.AddSingleton<ClienteService>();
        servicios.AddSingleton<PrestamoService>();
        servicios.AddSingleton<ContratoService>();
        servicios.AddSingleton<EmailService>();
        servicios.AddSingleton<RecordatorioService>();
        servicios.AddSingleton<PagoService>();
        servicios.AddSingleton<DashboardService>();
        servicios.AddSingleton<ReporteService>();
        servicios.AddSingleton<ExportacionService>();
        servicios.AddSingleton<VehiculoService>();
        servicios.AddSingleton<VentaVehiculoService>();
        servicios.AddSingleton<AlquilerService>();
        servicios.AddSingleton<VehiculoGastoService>();
        servicios.AddSingleton<NcfService>();
        servicios.AddSingleton<PanelDealService>();
        servicios.AddSingleton<ClienteDealService>();
        servicios.AddSingleton<ExpedienteService>();
        servicios.AddSingleton<VentaPlazoService>();
        servicios.AddSingleton<ReporteDealService>();

        // ---- Servicios del punto de venta ----
        // La auditoría que reciben es la COMPARTIDA de la suite: el cliente tiene
        // un solo historial, no uno por módulo.
        servicios.AddSingleton<FAControl.Services.Pos.ClienteService>();
        servicios.AddSingleton<FAControl.Services.Pos.ProductoService>();
        servicios.AddSingleton<FAControl.Services.Pos.VentaService>();
        servicios.AddSingleton<FAControl.Services.Pos.FacturaService>();
        servicios.AddSingleton<FAControl.Services.Pos.CuadreService>();
        servicios.AddSingleton<FAControl.Services.Pos.AnaliticaService>();
        servicios.AddSingleton<FAControl.Services.Pos.ConfiguracionNegocioService>();
        servicios.AddSingleton<FAControl.Services.Pos.ExportacionService>();
        servicios.AddSingleton(sp =>
            new RespaldoService(sp.GetRequiredService<ConexionFactory>().CadenaConexion));
        servicios.AddSingleton(FAControl.Common.AjustesLocales.Cargar());
        // Licencia de la instalación (4 códigos del launcher, 2026-07-27)
        servicios.AddSingleton(FAControl.Common.LicenciaLocal.Cargar());
        servicios.AddSingleton<LicenciaService>();
        servicios.AddSingleton<RecuperacionService>();
        servicios.AddSingleton<FAControl.Common.IDialogService, DialogService>();
        servicios.AddSingleton<NotificadorVencidos>();
        // MISMA instancia: el guard "ya avisé hoy" vive en ella, y Configuración
        // necesita resetearlo en el notificador real, no en una copia.
        servicios.AddSingleton<FAControl.Common.IAvisoVencidos>(
            sp => sp.GetRequiredService<NotificadorVencidos>());

        // ViewModels
        // TRANSIENT: el login se abre varias veces (launcher → login, cambiar
        // usuario, cerrar sesión → launcher → login). Una ventana WPF CERRADA
        // no se puede volver a mostrar, y su VM arrastra el estado del intento
        // anterior. Cada apertura estrena ventana y VM.
        servicios.AddTransient<LoginViewModel>();
        servicios.AddSingleton<UsuariosViewModel>();
        servicios.AddSingleton<ContratosViewModel>();
        servicios.AddSingleton<ClientesViewModel>();
        servicios.AddSingleton<ClienteFichaViewModel>();
        servicios.AddSingleton<ClienteFichaDealViewModel>();
        servicios.AddSingleton<CodigosViewModel>();
        servicios.AddSingleton<ExpedienteViewModel>();
        servicios.AddSingleton<ClienteFormViewModel>();
        servicios.AddSingleton<PrestamosViewModel>();
        servicios.AddSingleton<PrestamoNuevoViewModel>();
        servicios.AddSingleton<PrestamoDetalleViewModel>();
        servicios.AddSingleton<CobrosViewModel>();
        servicios.AddSingleton<PanelViewModel>();
        servicios.AddSingleton<PanelDealViewModel>();
        servicios.AddSingleton<VehiculoFichaViewModel>();
        servicios.AddSingleton<VentaFinanciamientoViewModel>();
        servicios.AddSingleton<ContratosDealViewModel>();
        servicios.AddSingleton<ReportesDealViewModel>();
        servicios.AddSingleton<ReportesViewModel>();
        servicios.AddSingleton<HistorialViewModel>();
        servicios.AddSingleton<ConfiguracionViewModel>();
        servicios.AddSingleton<VehiculosViewModel>();
        servicios.AddSingleton<VehiculoFormViewModel>();
        servicios.AddSingleton<VentasViewModel>();
        servicios.AddSingleton<VentaNuevaViewModel>();
        servicios.AddSingleton<AlquileresViewModel>();
        servicios.AddSingleton<AlquilerNuevoViewModel>();
        servicios.AddSingleton<GastosViewModel>();
        servicios.AddSingleton<MainViewModel>();

        // Views
        // Todas TRANSIENT por la misma razón: al cerrar sesión el shell se
        // cierra y hay que estrenar uno al volver a entrar. El MainViewModel
        // sigue siendo singleton (conserva los VM de página); solo la ventana
        // se rehace, y RefrescarPermisos() la reajusta al usuario nuevo.
        servicios.AddTransient<LoginWindow>();
        servicios.AddTransient<MainWindow>();
        servicios.AddTransient<LauncherWindow>();

        return servicios.BuildServiceProvider();
    }
}
