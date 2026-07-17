using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
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

        Log.Information("FAControl iniciando");

        // Sin base de datos operativa no se muestra ninguna ventana:
        // diagnóstico con mensajes claros (y creación automática si falta)
        if (!await PrepararBaseDatosAsync())
        {
            Shutdown();
            return;
        }

        var login = _servicios.GetRequiredService<LoginWindow>();
        var loginVm = _servicios.GetRequiredService<LoginViewModel>();

        loginVm.LoginExitoso += (_, _) =>
        {
            var shell = _servicios.GetRequiredService<MainWindow>();
            var ajustes = _servicios.GetRequiredService<FAControl.Common.AjustesLocales>();

            // Tamaño de texto guardado + reacción a cambios desde Configuración
            shell.AplicarEscala(ajustes.FactorEscala);
            _servicios.GetRequiredService<ConfiguracionViewModel>().EscalaCambiada += shell.AplicarEscala;

            MainWindow = shell;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            shell.Show();
            login.Close();
            _ = _servicios.GetRequiredService<MainViewModel>().InicializarAsync();

            // Export automático a Excel (si está activo y toca) — en segundo plano
            _ = _servicios.GetRequiredService<ExportacionService>().EjecutarAutomaticoSiTocaAsync(ajustes);

            // Aviso de clientes pasados de fecha (una vez por arranque + cambio de día)
            _servicios.GetRequiredService<NotificadorVencidos>().Iniciar();
        };

        login.Show();
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

        // Services
        servicios.AddSingleton<AuditoriaService>();
        servicios.AddSingleton<AuthService>();
        servicios.AddSingleton<UsuarioService>();
        servicios.AddSingleton<AmortizacionService>();
        servicios.AddSingleton<ClienteService>();
        servicios.AddSingleton<PrestamoService>();
        servicios.AddSingleton<PagoService>();
        servicios.AddSingleton<DashboardService>();
        servicios.AddSingleton<ReporteService>();
        servicios.AddSingleton<ExportacionService>();
        servicios.AddSingleton(sp =>
            new RespaldoService(sp.GetRequiredService<ConexionFactory>().CadenaConexion));
        servicios.AddSingleton(FAControl.Common.AjustesLocales.Cargar());
        servicios.AddSingleton<FAControl.Common.IDialogService, DialogService>();
        servicios.AddSingleton<NotificadorVencidos>();
        // MISMA instancia: el guard "ya avisé hoy" vive en ella, y Configuración
        // necesita resetearlo en el notificador real, no en una copia.
        servicios.AddSingleton<FAControl.Common.IAvisoVencidos>(
            sp => sp.GetRequiredService<NotificadorVencidos>());

        // ViewModels
        servicios.AddSingleton<LoginViewModel>();
        servicios.AddSingleton<UsuariosViewModel>();
        servicios.AddSingleton<ClientesViewModel>();
        servicios.AddSingleton<ClienteFichaViewModel>();
        servicios.AddSingleton<ClienteFormViewModel>();
        servicios.AddSingleton<PrestamosViewModel>();
        servicios.AddSingleton<PrestamoNuevoViewModel>();
        servicios.AddSingleton<PrestamoDetalleViewModel>();
        servicios.AddSingleton<CobrosViewModel>();
        servicios.AddSingleton<PanelViewModel>();
        servicios.AddSingleton<ReportesViewModel>();
        servicios.AddSingleton<HistorialViewModel>();
        servicios.AddSingleton<ConfiguracionViewModel>();
        servicios.AddSingleton<MainViewModel>();

        // Views
        servicios.AddSingleton<LoginWindow>();
        servicios.AddSingleton<MainWindow>();

        return servicios.BuildServiceProvider();
    }
}
