using System.IO;
using System.Text.Json;

namespace FAControl.Common;

/// <summary>Tamaño de texto de la interfaz (pedido de Yuber 2026-07-10).</summary>
public enum TamanoTexto
{
    Pequeno,
    Mediano,
    Grande
}

/// <summary>
/// Preferencias locales del equipo (NO van a la base de datos: son por PC).
/// Se persisten como JSON junto al ejecutable.
/// </summary>
public class AjustesLocales
{
    public TamanoTexto TamanoTexto { get; set; } = TamanoTexto.Pequeno;

    // Export automático a Excel (activable en Configuración)
    public bool ExportAutomaticoActivo { get; set; }
    public int ExportAutomaticoCadaDias { get; set; } = 30;
    public string? ExportAutomaticoCarpeta { get; set; }
    public DateTime? UltimaExportacionUtc { get; set; }

    // Notificador de vencimientos al iniciar (pedido del cliente 2026-07-10)
    public bool AvisoVencidosActivo { get; set; } = true;
    /// <summary>Ids de clientes silenciados con "No volver a preguntar por este cliente".</summary>
    public List<long> AvisoVencidosSilenciados { get; set; } = [];

    private static readonly string Ruta = Path.Combine(AppContext.BaseDirectory, "ajustes.json");
    private static readonly JsonSerializerOptions Opciones = new() { WriteIndented = true };

    /// <summary>Factor de escala de la UI según el tamaño elegido.</summary>
    public double FactorEscala => TamanoTexto switch
    {
        TamanoTexto.Mediano => 1.12,
        TamanoTexto.Grande => 1.25,
        _ => 1.0
    };

    public static AjustesLocales Cargar()
    {
        try
        {
            if (File.Exists(Ruta))
                return JsonSerializer.Deserialize<AjustesLocales>(File.ReadAllText(Ruta)) ?? new AjustesLocales();
        }
        catch (Exception)
        {
            // Archivo corrupto → se regenera con defaults (no es dato crítico)
        }
        return new AjustesLocales();
    }

    public void Guardar() => File.WriteAllText(Ruta, JsonSerializer.Serialize(this, Opciones));
}
