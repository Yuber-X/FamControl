using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
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
    /// <summary>
    /// Techo para respaldar y restaurar. Sin esto, un MySQL que no responde
    /// deja la ventana congelada para siempre y el usuario mata la app —
    /// justo en medio de una restauracion, que es cuando peor cae.
    /// </summary>
    private static readonly TimeSpan TiempoLimite = TimeSpan.FromMinutes(15);

    private readonly string _servidor;
    private readonly uint _puerto;
    private readonly string _usuario;
    private readonly string _password;
    private readonly string _baseDatos;

    public RespaldoService(string cadenaConexion)
    {
        var builder = new MySqlConnectionStringBuilder(cadenaConexion);
        _servidor = builder.Server;
        _puerto = builder.Port;
        _usuario = builder.UserID;
        _password = builder.Password;
        _baseDatos = builder.Database;
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
            // Sin esto, el juego de caracteres lo decide el my.ini de CADA PC.
            // Si no es utf8mb4, los acentos del respaldo salen rotos y el
            // destrozo recien se ve al restaurar, con los nombres ya cargados.
            "--default-character-set=utf8mb4 " +
            $"--single-transaction --routines --add-drop-table {baseDatos}");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TiempoLimite);

        using var proceso = Process.Start(info)
            ?? throw new InvalidOperationException("No se pudo iniciar mysqldump.");

        // stderr se lee desde YA y en paralelo: esperar al final cuelga a los
        // dos procesos cuando mysqldump llena el buffer del pipe (4 KB).
        var leerErrores = proceso.StandardError.ReadToEndAsync(cts.Token);

        // Se escribe a un archivo temporal y se renombra al final. Si mysqldump
        // muere a la mitad, lo que queda es un .sql truncado con toda la cara de
        // un respaldo bueno — y eso solo se descubre el dia que hay que usarlo.
        // Ademas se copia el flujo de bytes tal cual, sin cargar el volcado
        // entero en memoria.
        var parcial = rutaDestino + ".parcial";
        try
        {
            await using (var destino = File.Create(parcial))
                await proceso.StandardOutput.BaseStream.CopyToAsync(destino, cts.Token);

            var errores = await leerErrores;
            await proceso.WaitForExitAsync(cts.Token);

            if (proceso.ExitCode != 0)
                throw new InvalidOperationException(
                    $"mysqldump falló (código {proceso.ExitCode}):\n\n{errores.Trim()}");

            var bytes = new FileInfo(parcial).Length;
            if (bytes == 0)
                throw new InvalidOperationException(
                    "mysqldump terminó bien pero no escribió nada. El respaldo no sirve.");

            File.Move(parcial, rutaDestino, overwrite: true);
            Log.Information("Respaldo generado en {Ruta} ({Bytes} bytes)", rutaDestino, bytes);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            MatarProceso(proceso);
            throw new InvalidOperationException(
                $"El respaldo pasó de {TiempoLimite.TotalMinutes:0} minutos y se canceló. " +
                "Revisá que MySQL esté respondiendo.");
        }
        finally
        {
            // Un .parcial sobreviviente es basura: o se renombro, o fallo.
            if (File.Exists(parcial))
                try { File.Delete(parcial); } catch (IOException) { /* da igual */ }
        }
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

            // Los papeles del expediente valen tanto como los datos, y el .sql
            // no los trae (viven en disco). Van en su propio ZIP, al lado.
            var expedientes = ExpedienteService.RespaldarTodoEnZip(
                ExpedienteService.CarpetaRaiz(ajustes), ajustes.RespaldoAutomaticoCarpeta);

            ajustes.UltimoRespaldoUtc = DateTime.UtcNow;
            ajustes.Guardar();
            Log.Information("Respaldo automático completado: {Ruta}{Expedientes}", ruta,
                expedientes is null ? string.Empty : $" (+ expedientes en {expedientes})");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Falló el respaldo automático");
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

        await ValidarQueParezcaSqlAsync(rutaArchivo, ct);

        var mysql = BuscarHerramienta("mysql.exe");
        var info = CrearProceso(mysql,
            $"--host={_servidor} --port={_puerto} --user={_usuario} " +
            // Mismo motivo que en el respaldo: sin esto los acentos dependen
            // del my.ini de la PC donde se restaura.
            $"--default-character-set=utf8mb4 {_baseDatos}");
        info.RedirectStandardInput = true;
        // Solo se puede fijar con la entrada ya redirigida. Sin BOM: mysql.exe
        // se tragaria los tres bytes del BOM como si fueran SQL.
        info.StandardInputEncoding = new System.Text.UTF8Encoding(false);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TiempoLimite);

        using var proceso = Process.Start(info)
            ?? throw new InvalidOperationException("No se pudo iniciar mysql.");

        // Se empieza a leer stderr YA, en paralelo. Si se esperara al final:
        //  - un stderr largo llena el buffer del pipe (4 KB), mysql.exe se
        //    bloquea escribiendo, nosotros nos bloqueamos escribiendo stdin, y
        //    la app se cuelga;
        //  - y si mysql.exe muere antes, nunca se llega a leerlo.
        var leerErrores = proceso.StandardError.ReadToEndAsync(cts.Token);
        var leerSalida = proceso.StandardOutput.ReadToEndAsync(cts.Token);

        // mysql.exe corta en el PRIMER error del script y cierra la tuberia.
        // Escribir en una tuberia cerrada tira IOException ("se esta cerrando la
        // canalizacion"), que es el SINTOMA. El motivo real lo escribio en
        // stderr, asi que aca se traga la excepcion y se deja hablar al proceso.
        var cortoAntesDeTiempo = false;
        try
        {
            // Se copian los BYTES del archivo tal cual, sin decodificar a texto
            // y volver a codificar.
            //
            // Antes se leia linea por linea y se escribia con WriteLineAsync, y
            // ahi StandardInput usa la codificacion de consola de Windows
            // (CP850/CP1252 en español), no UTF-8. mysqldump escribe UTF-8, asi
            // que cada acento se re-codificaba mal y mysql.exe cortaba el texto
            // en el primer byte invalido: "José Ángel" entraba como "Jos".
            // Silencioso y sin error, que es lo peor que puede pasar con datos.
            await using var origen = File.OpenRead(rutaArchivo);
            await origen.CopyToAsync(proceso.StandardInput.BaseStream, cts.Token);
            await proceso.StandardInput.BaseStream.FlushAsync(cts.Token);
        }
        catch (IOException)
        {
            cortoAntesDeTiempo = true;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            MatarProceso(proceso);
            throw new InvalidOperationException(
                $"La restauración pasó de {TiempoLimite.TotalMinutes:0} minutos y se canceló. " +
                "⚠️ La base puede haber quedado a medio restaurar: volvé a restaurar " +
                "el mismo archivo antes de operar.");
        }

        try { proceso.StandardInput.Close(); }
        catch (IOException) { /* ya estaba cerrada */ }

        var errores = await leerErrores;
        await leerSalida;
        await proceso.WaitForExitAsync(cts.Token);

        if (proceso.ExitCode != 0 || cortoAntesDeTiempo)
        {
            var detalle = string.IsNullOrWhiteSpace(errores)
                ? "MySQL no dio detalles. Probá el archivo a mano:\n" +
                  $"mysql -u {_usuario} -p {_baseDatos} < \"{rutaArchivo}\""
                : errores.Trim();
            Log.Error("Falló la restauración desde {Ruta} (código {Codigo}): {Errores}",
                rutaArchivo, proceso.ExitCode, detalle);
            throw new InvalidOperationException(
                $"MySQL rechazó el archivo de respaldo:\n\n{detalle}");
        }

        Log.Information("Base de datos restaurada desde {Ruta}", rutaArchivo);
    }

    /// <summary>
    /// Rechaza de entrada lo que ni siquiera parece SQL, ANTES de tocar la base.
    /// Sale barato y evita el caso feo: elegir un .xlsx o un .zip por error y
    /// que la app arranque una restauracion destructiva para morir a la mitad.
    /// No distingue un respaldo bueno de uno malo — eso lo dice MySQL.
    /// </summary>
    private static async Task ValidarQueParezcaSqlAsync(string ruta, CancellationToken ct)
    {
        if (new FileInfo(ruta).Length == 0)
            throw new InvalidOperationException(
                "El archivo está vacío: no hay nada que restaurar.");

        // Alcanza con el principio: un volcado de mysqldump abre con sus
        // comentarios y sus SET, y cualquier script util nombra alguna tabla.
        var buffer = new char[8 * 1024];
        int leidos;
        using (var lector = new StreamReader(ruta))
            leidos = await lector.ReadAsync(buffer, ct);

        var cabecera = new string(buffer, 0, leidos);
        if (!Regex.IsMatch(cabecera, @"\b(CREATE\s+TABLE|INSERT\s+INTO|DROP\s+TABLE|SET\s|USE\s)",
                RegexOptions.IgnoreCase))
            throw new InvalidOperationException(
                "Este archivo no parece un respaldo de FAControl.\n\n" +
                "Un respaldo es un archivo .sql generado desde Configuración → " +
                "Respaldar ahora. Revisá que no hayas elegido un Excel, un .zip " +
                "de expedientes o un archivo a medio bajar.");
    }

    /// <summary>Cierra el proceso sin dejarlo colgado. Nunca propaga.</summary>
    private static void MatarProceso(Process proceso)
    {
        try
        {
            if (!proceso.HasExited)
                proceso.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "No se pudo cerrar el proceso de respaldo");
        }
    }

    private ProcessStartInfo CrearProceso(string ejecutable, string argumentos)
    {
        var info = new ProcessStartInfo(ejecutable, argumentos)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
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
