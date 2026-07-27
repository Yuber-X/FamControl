namespace FAControl.Models;

/// <summary>Para qué sirve el papel dentro del expediente.</summary>
public enum TipoDocumento
{
    Otro,
    /// <summary>La factura FIRMADA y escaneada: reemplaza en pantalla a la que emite el sistema.</summary>
    FacturaEscaneada,
    Contrato,
    Identificacion
}

/// <summary>
/// Documento del expediente digital de una venta (018, pedido 2026-07-27):
/// facturas, contratos firmados, cédulas, fotos del vehículo, lo que el
/// cliente haya entregado. El archivo vive en disco; esto es su ficha.
/// </summary>
public class DocumentoVenta
{
    public long Id { get; set; }
    public long VentaId { get; set; }
    /// <summary>Nombre original del archivo, tal como lo ve el usuario.</summary>
    public string Nombre { get; set; } = string.Empty;
    /// <summary>Ruta relativa a la carpeta de expedientes ('&lt;venta&gt;/&lt;id&gt;_&lt;nombre&gt;').</summary>
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
