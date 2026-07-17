using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

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

    /// <summary>
    /// Modo noche (pedido del cliente 2026-07-16). Es preferencia POR PC, no
    /// del negocio: cada terminal puede tener la suya.
    /// </summary>
    public bool TemaOscuro { get; set; }

    // Export automático a Excel (activable en Configuración)
    public bool ExportAutomaticoActivo { get; set; }
    public int ExportAutomaticoCadaDias { get; set; } = 30;
    public string? ExportAutomaticoCarpeta { get; set; }
    public DateTime? UltimaExportacionUtc { get; set; }

    // Respaldo automático de la BD (.sql). Pedido del cliente 2026-07-19:
    // cada N días o meses que el usuario elija. Apuntar la carpeta a la de
    // sincronización de OneDrive/Google Drive sube el respaldo a la nube solo.
    public bool RespaldoAutomaticoActivo { get; set; }
    public int RespaldoAutomaticoCada { get; set; } = 7;
    /// <summary>Unidad del intervalo: "dias" o "meses".</summary>
    public string RespaldoAutomaticoUnidad { get; set; } = "dias";
    public string? RespaldoAutomaticoCarpeta { get; set; }
    public DateTime? UltimoRespaldoUtc { get; set; }

    /// <summary>Días equivalentes del intervalo de respaldo (1 mes ≈ 30 días).</summary>
    public int RespaldoIntervaloEnDias =>
        Math.Max(1, RespaldoAutomaticoCada) * (RespaldoAutomaticoUnidad == "meses" ? 30 : 1);

    // Notificador de vencimientos al iniciar (pedido del cliente 2026-07-10)
    public bool AvisoVencidosActivo { get; set; } = true;
    /// <summary>Ids de clientes silenciados con "No volver a preguntar por este cliente".</summary>
    public List<long> AvisoVencidosSilenciados { get; set; } = [];

    // ---------- Datos del negocio (para el pagaré / contrato) ----------
    // Editables en Configuración. Aparecen en el encabezado del pagaré y como
    // el ACREEDOR al que el cliente debe pagar. Defaults: Familia Almonte.
    public string NombreNegocio { get; set; } = "Familia Almonte Auto Import SRL";
    /// <summary>Nombre del prestamista (acreedor que firma el pagaré).</summary>
    public string Prestamista { get; set; } = string.Empty;
    public string CiudadNegocio { get; set; } = string.Empty;
    public string TelefonoNegocio { get; set; } = string.Empty;
    public string EmailNegocio { get; set; } = string.Empty;
    /// <summary>RNC, opcional (comprobante fiscal — ver docs/NCF-DGII.md).</summary>
    public string RncNegocio { get; set; } = string.Empty;

    // ---------- Recordatorios por correo (Gmail) — cliente 2026-07-19 ----------
    public bool RecordatoriosActivos { get; set; }
    /// <summary>Cuenta Gmail que ENVÍA los recordatorios.</summary>
    public string GmailRemitente { get; set; } = string.Empty;
    /// <summary>
    /// Contraseña de APLICACIÓN de Gmail, cifrada con DPAPI (nunca texto plano).
    /// Se lee/escribe con GmailAppPassword; este campo es el blob persistido.
    /// </summary>
    public string GmailAppPasswordCifrada { get; set; } = string.Empty;
    /// <summary>Correo del dueño que recibe el resumen.</summary>
    public string CorreoDueno { get; set; } = string.Empty;
    /// <summary>Avisar cuando la cuota vence dentro de estos días.</summary>
    public int RecordatorioDiasAntes { get; set; } = 3;
    /// <summary>Enviar recordatorios automáticamente al abrir la aplicación.</summary>
    public bool RecordatoriosAutomaticos { get; set; }
    public DateTime? UltimoRecordatorioUtc { get; set; }

    /// <summary>Contraseña de app de Gmail en texto plano (cifra/descifra con DPAPI).</summary>
    [JsonIgnore]
    public string GmailAppPassword
    {
        get => Secreto.Revelar(GmailAppPasswordCifrada);
        set => GmailAppPasswordCifrada = Secreto.Proteger(value);
    }

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
