namespace FAControl.Models;

/// <summary>Para qué sirve el papel dentro del expediente.</summary>
public enum TipoDocumento
{
    Otro,
    /// <summary>La factura FIRMADA y escaneada: reemplaza en pantalla a la que emite el sistema.</summary>
    FacturaEscaneada,
    Contrato,
    Identificacion,
    /// <summary>Pagaré que la app archiva sola al imprimirlo (026).</summary>
    Pagare,
    /// <summary>Intimación de pago archivada sola al imprimirla (026).</summary>
    Intimacion
}

/// <summary>De qué cuelga un expediente (026).</summary>
public enum TipoExpediente
{
    /// <summary>Venta de vehículo (DealControl).</summary>
    Venta,
    /// <summary>Préstamo (PrestControl).</summary>
    Prestamo
}

/// <summary>
/// Dueño de un expediente: una venta del dealer o un préstamo.
///
/// Existe para que el expediente digital sirva a los dos módulos sin duplicar
/// nada. La carpeta lleva el tipo adelante justamente para que la venta 5 y el
/// préstamo 5 no compartan archivos.
/// </summary>
public readonly record struct DuenoExpediente(TipoExpediente Tipo, long Id)
{
    public static DuenoExpediente DeVenta(long ventaId) => new(TipoExpediente.Venta, ventaId);
    public static DuenoExpediente DePrestamo(long prestamoId) => new(TipoExpediente.Prestamo, prestamoId);

    /// <summary>Carpeta relativa donde van sus archivos.</summary>
    public string Carpeta => Tipo == TipoExpediente.Venta ? $"ventas/{Id}" : $"prestamos/{Id}";

    public string Descripcion => Tipo == TipoExpediente.Venta ? $"la venta {Id}" : $"el préstamo {Id}";
}

/// <summary>
/// Documento del expediente digital de una venta (018, pedido 2026-07-27):
/// facturas, contratos firmados, cédulas, fotos del vehículo, lo que el
/// cliente haya entregado. El archivo vive en disco; esto es su ficha.
/// </summary>
public class DocumentoVenta
{
    public long Id { get; set; }
    /// <summary>Venta dueña, o null si el expediente es de un préstamo (026).</summary>
    public long? VentaId { get; set; }
    /// <summary>Préstamo dueño, o null si el expediente es de una venta (026).</summary>
    public long? PrestamoId { get; set; }

    /// <summary>De qué cuelga este documento. Siempre hay uno y solo uno.</summary>
    public DuenoExpediente Dueno => VentaId is { } venta
        ? DuenoExpediente.DeVenta(venta)
        : DuenoExpediente.DePrestamo(PrestamoId ?? 0);
    /// <summary>Nombre original del archivo, tal como lo ve el usuario.</summary>
    public string Nombre { get; set; } = string.Empty;
    /// <summary>
    /// Ruta relativa a la carpeta de expedientes. Los documentos anteriores a
    /// 026 la tienen como '&lt;venta&gt;/&lt;id&gt;_&lt;nombre&gt;' y siguen
    /// abriéndose igual: la ruta se guarda, no se recalcula.
    /// </summary>
    public string RutaRelativa { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public long TamanoBytes { get; set; }
    public TipoDocumento Tipo { get; set; } = TipoDocumento.Otro;
    public string? Notas { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string? SubidoPor { get; set; }

    /// <summary>Tamaño legible: "1.4 MB".</summary>
    public string TamanoTexto => TamanoBytes switch
    {
        < 1024 => $"{TamanoBytes} B",
        < 1024 * 1024 => $"{TamanoBytes / 1024d:0.#} KB",
        _ => $"{TamanoBytes / (1024d * 1024d):0.#} MB"
    };

    /// <summary>Familia del archivo, para el ícono y para saber con qué se abre.</summary>
    public string Familia => Extension.ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif" or ".webp" or ".heic" or ".heif" => "Imagen",
        ".doc" or ".docx" or ".rtf" or ".odt" => "Word",
        ".xls" or ".xlsx" or ".csv" or ".ods" => "Excel",
        ".pdf" => "PDF",
        ".zip" or ".rar" or ".7z" => "Comprimido",
        ".txt" => "Texto",
        _ => "Archivo"
    };
}
