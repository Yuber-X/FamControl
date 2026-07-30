using System.Diagnostics;
using System.IO;
using FAControl.Common;
using MySqlConnector;
using Serilog;

namespace FAControl.Services;

/// <summary>
/// Respaldo y restauración de la base de datos con las herramientas oficiales
/// de MySQL (mysqldump / mysql). La contraseña viaja por la variable de entorno
/// MYSQL_PWD — nunca por la línea de comandos (visible en el administrador de tareas).
/// </summary>
public class RespaldoService
{
    private readonly string _servidor;
    private readonly uint _puerto;
    private readonly string _usuario;
    private readonly string _password;
    private readonly string _baseDatos;

    /// <summary>
    /// Base del punto de venta (POS-500, 2026-07-30). El respaldo automático
    /// saca las DOS: el .sql de la suite no incluye las ventas del mostrador,
    /// que viven en otra base. Null = esta instalación no usa el POS.
    /// </summary>
    private readonly string? _baseDatosPos;

    public RespaldoService(string cadenaConexion, string? cadenaPos = null)
    {
        var builder = new MySqlConnectionStringBuilder(cadenaConexion);
        _servidor = builder.Server;
        _puerto = builder.Port;
        _usuario = builder.UserID;
        _password = builder.Password;
        _baseDatos = builder.Database;

        // Mismo servidor y credenciales; solo cambia el nombre de la base
        _baseDatosPos = string.IsNullOrWhiteSpace(cadenaPos)
            ? null
            : new MySqlConnectionStringBuilder(cadenaPos).Database;
    }

    /// <summary>Genera el respaldo .sql completo en la ruta indicada.</summary>
    public Task RespaldarAsync(string rutaDestino, CancellationToken ct = default) =>
        RespaldarBaseAsync(_baseDatos, rutaDestino, ct);

    private async Task RespaldarBaseAsync(string baseDatos, string rutaDestino,
        CancellationToken ct = default)
    {
        var mysqldump = BuscarHerramienta("mysqldump.exe");
        var info = CrearProceso(mysqldump,
            $"--host={_servidor} --port={_puerto} --user={_usuario} " +
            $"--single-transaction --routines --add-drop-table {baseDatos}");

        using var proceso = Process.Start(info)
            ?? throw new InvalidOperationException("No se pudo iniciar mysqldump.");

        var salida = await proceso.StandardOutput.ReadToEndAsync(ct);
        var errores = await proceso.StandardError.ReadToEndAsync(ct);
        await proceso.WaitForExitAsync(ct);

        if (proceso.ExitCode != 0)
            throw new InvalidOperationException($"mysqldump falló (código {proceso.ExitCode}): {errores}");

        await File.WriteAllTextAsync(rutaDestino, salida, ct);
        Log.Information("Respaldo generado en {Ruta} ({Bytes} bytes)", rutaDestino, salida.Length);
    }

    /// <summary>
    /// Respaldo automático si toca (cliente 2026-07-19): al arrancar, si está
    /// activo y pasó el intervalo elegido, genera un .sql en la carpeta destino.
    /// Nunca tumba la app: cualquier fallo solo se registra.
    /// Espeja el patrón del export automático a Excel.
    /// </summary>
    public async Task EjecutarAutomaticoSiTocaAsync(AjustesLocales ajustes)
    {
        try
        {
            if (!ajustes.RespaldoAutomaticoActivo || string.IsNullOrWhiteSpace(ajustes.RespaldoAutomaticoCarpeta))
                return;

            if (ajustes.UltimoRespaldoUtc is { } ultimo &&
                (DateTime.UtcNow - ultimo).TotalDays < ajustes.RespaldoIntervaloEnDias)
                return;

            Directory.CreateDirectory(ajustes.RespaldoAutomaticoCarpeta);
            var ruta = Path.Combine(ajustes.RespaldoAutomaticoCarpeta,
                $"FAControl_Respaldo_{DateTime.Now:yyyy-MM-dd_HHmm}.sql");

            await RespaldarAsync(ruta);

            // El punto de venta guarda sus ventas en OTRA base: si no se respalda
            // aparte, un restore deja el negocio sin las facturas del mostrador.
            var rutaPos = await RespaldarPuntoDeVentaSiHayAsync(ajustes.RespaldoAutomaticoCarpeta);

            // Los papeles del expediente valen tanto como los datos, y el .sql
            // no los trae (viven en disco). Van en su propio ZIP, al lado.
            var expedientes = ExpedienteService.RespaldarTodoEnZip(
                ExpedienteService.CarpetaRaiz(ajustes), ajustes.RespaldoAutomaticoCarpeta);

            ajustes.UltimoRespaldoUtc = DateTime.UtcNow;
            ajustes.Guardar();
            Log.Information("Respaldo automático completado: {Ruta}{Pos}{Expedientes}", ruta,
                rutaPos is null ? string.Empty : $" (+ punto de venta en {rutaPos})",
                expedientes is null ? string.Empty : $" (+ expedientes en {expedientes})");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Falló el respaldo automático");
        }
    }

    /// <summary>
    /// Respalda la base del punto de venta, si esta instalación la tiene. No
    /// tumba el respaldo principal: si el POS falla o ni siquiera está creado,
    /// queda anotado y se sigue. Devuelve la ruta, o null si no había nada.
    /// </summary>
    private async Task<string?> RespaldarPuntoDeVentaSiHayAsync(string carpeta)
    {
        if (_baseDatosPos is null)
            return null;

        try
        {
            var ruta = Path.Combine(carpeta, $"POS500_Respaldo_{DateTime.Now:yyyy-MM-dd_HHmm}.sql");
            await RespaldarBaseAsync(_baseDatosPos, ruta);
            return ruta;
        }
        catch (Exception ex)
        {
            // Lo más común: el cliente no compró el POS y la base no existe
            Log.Warning(ex, "No se respaldó la base del punto de venta ({Base})", _baseDatosPos);
            return null;
        }
    }

    /// <summary>
    /// Restaura la BD desde un archivo .sql. DESTRUCTIVO: reemplaza los datos
    /// actuales — el llamador DEBE confirmar dos veces y sugerir respaldo previo.
    /// </summary>
    public async Task RestaurarAsync(string rutaArchivo, CancellationToken ct = default)
    {
        if (!File.Exists(rutaArchivo))
            throw new FileNotFoundException("No se encontró el archivo de respaldo.", rutaArchivo);

        var mysql = BuscarHerramienta("mysql.exe");
        var info = CrearProceso(mysql,
            $"--host={_servidor} --port={_puerto} --user={_usuario} {_baseDatos}");
        info.RedirectStandardInput = true;

        using var proceso = Process.Start(info)
            ?? throw new InvalidOperationException("No se pudo iniciar mysql.");

        using (var lector = File.OpenText(rutaArchivo))
        {
            string? linea;
            while ((linea = await lector.ReadLineAsync(ct)) is not null)
                await proceso.StandardInput.WriteLineAsync(linea);
        }
        proceso.StandardInput.Close();

        var errores = await proceso.StandardError.ReadToEndAsync(ct);
        await proceso.WaitForExitAsync(ct);

        if (proceso.ExitCode != 0)
            throw new InvalidOperationException($"mysql falló (código {proceso.ExitCode}): {errores}");

        Log.Information("Base de datos restaurada desde {Ruta}", rutaArchivo);
    }

    private ProcessStartInfo CrearProceso(string ejecutable, string argumentos)
    {
        var info = new ProcessStartInfo(ejecutable, argumentos)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8
        };
        info.EnvironmentVariables["MYSQL_PWD"] = _password;
        return info;
    }

    /// <summary>Busca la herramienta en el PATH y en las rutas típicas de instalación.</summary>
    public static string BuscarHerramienta(string nombreExe)
    {
        // 1) PATH
        var rutas = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(';');
        foreach (var ruta in rutas)
        {
            var candidato = Path.Combine(ruta.Trim(), nombreExe);
            if (File.Exists(candidato))
                return candidato;
        }

        // 2) Instalaciones típicas de MySQL Server en Windows
        foreach (var programas in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        })
        {
            var baseMySql = Path.Combine(programas, "MySQL");
            if (!Directory.Exists(baseMySql))
                continue;
            foreach (var carpeta in Directory.GetDirectories(baseMySql, "MySQL Server*")
                         .OrderByDescending(c => c))
            {
                var candidato = Path.Combine(carpeta, "bin", nombreExe);
                if (File.Exists(candidato))
                    return candidato;
            }
        }

        throw new FileNotFoundException(
            $"No se encontró {nombreExe}. Verificá que MySQL Server esté instalado " +
            "o agregá su carpeta bin al PATH.");
    }
}
