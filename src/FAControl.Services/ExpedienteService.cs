using System.IO.Compression;
using FAControl.Common;
using FAControl.Data;
using FAControl.Models;
using Serilog;

namespace FAControl.Services;

/// <summary>
/// Expediente digital del contrato (018, pedido del cliente 2026-07-27):
/// guardar aquí todas las facturas, documentos e imágenes que el cliente entregó
/// para la compra, poder abrirlos con su app de Windows y bajarlos todos en un
/// ZIP para migrar cuando haga falta.
///
/// REGLAS DE PERMISO (las pidió el cliente tal cual):
///  * ver, abrir y guardar copia → cualquiera que tenga la pantalla;
///  * ELIMINAR y RE-UBICAR → solo Admin (son los que rompen un expediente).
///
/// SEGURIDAD: solo entran las extensiones de la lista blanca. Nada de .exe,
/// .bat, .ps1 ni .lnk: el expediente se abre con doble clic, y un ejecutable
/// disfrazado de "cédula" sería una puerta de entrada al equipo del cliente.
/// </summary>
public class ExpedienteService
{
    /// <summary>Lo que el cliente pidió poder guardar, más los formatos obvios de RD.</summary>
    public static readonly string[] ExtensionesPermitidas =
    [
        ".pdf",
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp",
        ".heic", ".heif",                       // fotos de iPhone
        ".doc", ".docx", ".rtf", ".odt",
        ".xls", ".xlsx", ".csv", ".ods",
        ".zip", ".rar", ".7z",
        ".txt"
    ];

    /// <summary>Tope por archivo. Un documento de compraventa no pesa más que esto.</summary>
    public const long MaxBytesPorArchivo = 50L * 1024 * 1024;

    private readonly DocumentoVentaRepository _documentos;
    private readonly AuditoriaService _auditoria;
    private readonly string _raiz;

    public ExpedienteService(DocumentoVentaRepository documentos, AuditoriaService auditoria,
        AjustesLocales ajustes)
    {
        _documentos = documentos;
        _auditoria = auditoria;
        _raiz = CarpetaRaiz(ajustes);
    }

    /// <summary>
    /// Carpeta donde viven los archivos. Configurable para poder apuntarla a
    /// una unidad con espacio (o a OneDrive); por defecto va junto al ejecutable.
    /// </summary>
    public static string CarpetaRaiz(AjustesLocales ajustes) =>
        string.IsNullOrWhiteSpace(ajustes.CarpetaExpedientes)
            ? Path.Combine(AppContext.BaseDirectory, "expedientes")
            : ajustes.CarpetaExpedientes;

    public string Raiz => _raiz;

    /// <summary>Ruta real del archivo en el disco.</summary>
    public string RutaAbsoluta(DocumentoVenta documento) =>
        Path.Combine(_raiz, documento.RutaRelativa.Replace('/', Path.DirectorySeparatorChar));

    public Task<IReadOnlyList<DocumentoVenta>> ObtenerAsync(DuenoExpediente dueno, CancellationToken ct = default)
    {
        ExigirVer();
        return _documentos.ObtenerDeAsync(dueno, ct);
    }

    /// <summary>La factura escaneada que reemplaza a la del sistema, si la hay (D6).</summary>
    public async Task<DocumentoVenta?> ObtenerFacturaEscaneadaAsync(long ventaId,
        CancellationToken ct = default)
    {
        ExigirVer();
        var documentos = await _documentos.ObtenerDeAsync(DuenoExpediente.DeVenta(ventaId), ct);
        return documentos.FirstOrDefault(d => d.Tipo == TipoDocumento.FacturaEscaneada);
    }

    /// <summary>
    /// Copia el archivo al expediente. El original del usuario NO se mueve ni
    /// se borra: si el expediente se corrompe, su archivo sigue donde estaba.
    /// </summary>
    public async Task<DocumentoVenta> AgregarAsync(DuenoExpediente dueno, string rutaOrigen,
        TipoDocumento tipo = TipoDocumento.Otro, string? notas = null, CancellationToken ct = default)
    {
        ExigirVer();

        if (!File.Exists(rutaOrigen))
            throw new InvalidOperationException($"No encuentro el archivo:\n{rutaOrigen}");

        var info = new FileInfo(rutaOrigen);
        var extension = info.Extension.ToLowerInvariant();
        ValidarExtension(extension);

        if (info.Length > MaxBytesPorArchivo)
            throw new InvalidOperationException(
                $"'{info.Name}' pesa {info.Length / (1024d * 1024d):0.#} MB y el máximo es " +
                $"{MaxBytesPorArchivo / (1024 * 1024)} MB. Comprimilo o subilo en partes.");

        // 1. Ficha primero: su id da el nombre único del archivo en disco
        var id = await _documentos.CrearAsync(dueno, info.Name, "pendiente", extension,
            info.Length, tipo, notas, SesionActual.HaySesionActiva ? SesionActual.Id : null, ct);

        var relativa = $"{dueno.Carpeta}/{id}_{LimpiarNombre(info.Name)}";
        var destino = Path.Combine(_raiz, relativa.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(destino)!);

        try
        {
            File.Copy(rutaOrigen, destino, overwrite: true);
        }
        catch
        {
            // Sin archivo no hay documento: se borra la ficha para no dejar huérfanos
            await _documentos.EliminarAsync(id, ct);
            throw;
        }

        await _documentos.ActualizarRutaAsync(id, relativa, ct);
        await _auditoria.RegistrarAsync(AccionAuditoria.Crear, DbNames.Documento, id,
            $"Documento '{info.Name}' agregado al expediente de {dueno.Descripcion}", ct);
        Log.Information("Expediente {Dueno}: documento {Id} ({Nombre}) agregado",
            dueno.Descripcion, id, info.Name);

        return await _documentos.ObtenerPorIdAsync(id, ct)
            ?? throw new InvalidOperationException("El documento se guardó pero no se pudo releer.");
    }

    /// <summary>Guarda una copia donde el usuario quiera (botón "Guardar").</summary>
    public void GuardarCopia(DocumentoVenta documento, string rutaDestino)
    {
        ExigirVer();
        var origen = RutaAbsoluta(documento);
        if (!File.Exists(origen))
            throw new InvalidOperationException(ArchivoPerdido(documento));
        File.Copy(origen, rutaDestino, overwrite: true);
    }

    /// <summary>
    /// Ruta lista para abrir con la app de Windows que corresponda. Vuelve a
    /// validar la extensión: aunque la base diga otra cosa, aquí no se abre nada
    /// que no esté en la lista blanca.
    /// </summary>
    public string RutaParaAbrir(DocumentoVenta documento)
    {
        ExigirVer();
        var ruta = RutaAbsoluta(documento);
        ValidarExtension(Path.GetExtension(ruta).ToLowerInvariant());
        if (!File.Exists(ruta))
            throw new InvalidOperationException(ArchivoPerdido(documento));
        return ruta;
    }

    /// <summary>ELIMINAR — exclusivo del Admin (pedido del cliente).</summary>
    public async Task EliminarAsync(long documentoId, CancellationToken ct = default)
    {
        ExigirAdmin("eliminar documentos del expediente");

        var documento = await _documentos.ObtenerPorIdAsync(documentoId, ct)
            ?? throw new InvalidOperationException("El documento ya no existe.");

        // El archivo se conserva en disco: el soft delete es reversible por
        // soporte, borrarlo del disco no lo sería.
        await _documentos.EliminarAsync(documentoId, ct);
        await _auditoria.RegistrarAsync(AccionAuditoria.Eliminar, DbNames.Documento, documentoId,
            $"Documento '{documento.Nombre}' quitado del expediente de {documento.Dueno.Descripcion}", ct);
    }

    /// <summary>
    /// RE-UBICAR — exclusivo del Admin: el papel era de otro cliente y hubo
    /// confusión. Mueve el archivo a la carpeta del expediente destino.
    /// </summary>
    public async Task MoverAsync(long documentoId, DuenoExpediente destino, CancellationToken ct = default)
    {
        ExigirAdmin("re-ubicar documentos");

        var documento = await _documentos.ObtenerPorIdAsync(documentoId, ct)
            ?? throw new InvalidOperationException("El documento ya no existe.");
        if (documento.Dueno == destino)
            throw new InvalidOperationException("El documento ya está en ese expediente.");

        var relativaNueva = $"{destino.Carpeta}/{documento.Id}_{LimpiarNombre(documento.Nombre)}";
        var origen = RutaAbsoluta(documento);
        var rutaNueva = Path.Combine(_raiz, relativaNueva.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(rutaNueva)!);

        if (File.Exists(origen))
            File.Move(origen, rutaNueva, overwrite: true);

        await _documentos.MoverAsync(documentoId, destino, relativaNueva, ct);
        await _auditoria.RegistrarAsync(AccionAuditoria.Modificar, DbNames.Documento, documentoId,
            $"Documento '{documento.Nombre}' movido de {documento.Dueno.Descripcion} a {destino.Descripcion}", ct);
    }

    /// <summary>
    /// Baja TODO el expediente en un ZIP (pedido: "descargarlo todo en un
    /// archivo zip o rar para migrar en cualquier momento").
    /// </summary>
    public async Task<int> ExportarZipAsync(DuenoExpediente dueno, string rutaZip, CancellationToken ct = default)
    {
        ExigirVer();

        var documentos = await _documentos.ObtenerDeAsync(dueno, ct);
        if (documentos.Count == 0)
            throw new InvalidOperationException("Este expediente todavía no tiene documentos.");

        if (File.Exists(rutaZip))
            File.Delete(rutaZip);

        var agregados = 0;
        using (var zip = ZipFile.Open(rutaZip, ZipArchiveMode.Create))
        {
            var usados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var documento in documentos)
            {
                var ruta = RutaAbsoluta(documento);
                if (!File.Exists(ruta))
                {
                    Log.Warning("Expediente {Dueno}: falta el archivo de {Documento}", dueno.Descripcion, documento.Id);
                    continue;
                }

                // Dentro del ZIP van con su nombre real; si se repite, se numera
                var nombre = documento.Nombre;
                var intento = 1;
                while (!usados.Add(nombre))
                {
                    nombre = $"{Path.GetFileNameWithoutExtension(documento.Nombre)} ({++intento})" +
                             documento.Extension;
                }

                zip.CreateEntryFromFile(ruta, nombre, CompressionLevel.Optimal);
                agregados++;
            }
        }

        if (agregados == 0)
        {
            File.Delete(rutaZip);
            throw new InvalidOperationException(
                "Ninguno de los archivos del expediente está en el disco. Restaura un respaldo.");
        }

        Log.Information("Expediente {Dueno}: {Cantidad} documentos exportados a {Zip}",
            dueno.Descripcion, agregados, rutaZip);
        return agregados;
    }

    /// <summary>
    /// Copia TODA la carpeta de expedientes a un ZIP. Lo usa el respaldo
    /// automático: el .sql solo trae la base, y los papeles del cliente valen
    /// tanto como los datos (pedido del cliente 2026-07-27).
    /// </summary>
    /// <returns>Ruta del ZIP, o null si todavía no hay ningún expediente.</returns>
    public static string? RespaldarTodoEnZip(string raiz, string carpetaDestino)
    {
        if (!Directory.Exists(raiz) || !Directory.EnumerateFileSystemEntries(raiz).Any())
            return null;   // no hay expedientes todavía: nada que respaldar

        var zip = Path.Combine(carpetaDestino,
            $"FAControl_expedientes_{DateTime.Now:yyyy-MM-dd_HHmm}.zip");
        if (File.Exists(zip))
            File.Delete(zip);

        ZipFile.CreateFromDirectory(raiz, zip, CompressionLevel.Optimal, includeBaseDirectory: false);
        return zip;
    }

    // ---------- Puertas ----------

    private static void ExigirVer()
    {
        if (!SesionActual.TienePermiso(Permisos.Ventas) && !SesionActual.EsAdmin)
            throw new UnauthorizedAccessException("No tienes permiso para ver el expediente del contrato.");
    }

    private static void ExigirAdmin(string accion)
    {
        if (!SesionActual.EsAdmin)
            throw new UnauthorizedAccessException($"Solo un administrador puede {accion}.");
    }

    private static void ValidarExtension(string extension)
    {
        if (!ExtensionesPermitidas.Contains(extension))
            throw new InvalidOperationException(
                $"El tipo de archivo '{extension}' no se permite en el expediente.\n\n" +
                "Se aceptan documentos (PDF, Word, Excel), imágenes (incluidas las de iPhone) " +
                "y comprimidos (ZIP, RAR).");
    }

    /// <summary>Saca del nombre lo que rompe una ruta de Windows.</summary>
    private static string LimpiarNombre(string nombre)
    {
        var limpio = string.Concat(nombre.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return limpio.Length > 120 ? limpio[^120..] : limpio;
    }

    private static string ArchivoPerdido(DocumentoVenta documento) =>
        $"El archivo '{documento.Nombre}' ya no está en la carpeta de expedientes.\n\n" +
        "Puede que se haya movido a mano o que falte restaurar el respaldo de expedientes.";
}
