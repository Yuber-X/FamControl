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
    /// Modo noche POR MODO (pedido de Yuber 2026-07-18): cada estancia recuerda
    /// su propio tema. Default: **DealControl arranca oscuro**, el resto claro.
    /// Editable en Configuración. Preferencia POR PC, no del negocio.
    /// La clave es <c>ModoApp.ToString()</c>.
    /// </summary>
    public Dictionary<string, bool> TemaOscuroPorModo { get; set; } = new();

    /// <summary>
    /// Arranque directo (pedido del cliente 2026-07-29): nombre del ModoApp que
    /// la app abre sola al iniciar, sin mostrar el launcher. Vacío = mostrar el
    /// launcher. Se marca en el launcher y se apaga en Configuración → Arranque.
    ///
    /// Solo aplica al ARRANQUE: si el usuario cierra sesión, vuelve a ver el
    /// launcher en esa misma corrida. Si no, quedaría encerrado en un modo sin
    /// forma de cambiar de estancia.
    /// </summary>
    public string ArranqueDirectoModo { get; set; } = string.Empty;

    /// <summary>El modo de arranque directo, o null si está apagado o guardado con basura.</summary>
    public ModoApp? ArranqueDirecto =>
        Enum.TryParse<ModoApp>(ArranqueDirectoModo, out var modo) ? modo : null;

    /// <summary>Fija el arranque directo (null = apagarlo). Llamar a <see cref="Guardar"/> aparte.</summary>
    public void FijarArranqueDirecto(ModoApp? modo) =>
        ArranqueDirectoModo = modo?.ToString() ?? string.Empty;

    /// <summary>Tema del modo: usa el default por modo si el usuario no lo cambió.</summary>
    public bool TemaOscuroDe(ModoApp modo) =>
        TemaOscuroPorModo.TryGetValue(modo.ToString(), out var oscuro)
            ? oscuro
            : modo == ModoApp.DealerControl;   // DealControl arranca en modo noche

    /// <summary>Fija (y recuerda) el tema del modo. Llamar a <see cref="Guardar"/> aparte.</summary>
    public void FijarTemaOscuro(ModoApp modo, bool oscuro) =>
        TemaOscuroPorModo[modo.ToString()] = oscuro;

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
    /// <summary>Plazo (días) que se concede en la intimación de pago antes de la vía legal.</summary>
    public int PlazoIntimacionDias { get; set; } = 15;

    /// <summary>
    /// % de comisión del vendedor sobre el monto vendido (DealControl, 2026-07-25).
    /// Lo define el negocio; 0 = no se calculan comisiones en el reporte.
    /// </summary>
    public decimal PorcentajeComisionVendedor { get; set; }

    // ---------- Punto de venta (POS-500, integrado 2026-07-30) ----------
    // Preferencias POR PC: la impresora y el aviso al iniciar dependen de la
    // terminal, no del negocio. Lo del negocio (ITBIS, moneda, numeración de
    // facturas) vive en la tabla configuracion_negocio de pos500_db.

    /// <summary>Avisar al entrar al POS sobre productos próximos a caducar.</summary>
    public bool AvisoCaducidadActivo { get; set; } = true;
    /// <summary>Cuántos días antes de la caducidad empieza el aviso.</summary>
    public int AvisoCaducidadDias { get; set; } = 30;
    /// <summary>Avisar al entrar al POS sobre productos con pocas existencias.</summary>
    public bool AvisoStockBajoActivo { get; set; } = true;
    /// <summary>Desde cuántas unidades para abajo se considera stock bajo.</summary>
    public int AvisoStockBajoUmbral { get; set; } = 10;
    /// <summary>Ids de productos silenciados con "no volver a avisarme por este".</summary>
    public List<long> AvisoProductosSilenciados { get; set; } = [];

    /// <summary>Impresora del ticket. Vacío = la predeterminada de Windows.</summary>
    public string? ImpresoraPredeterminada { get; set; }
    public int CopiasTicket { get; set; } = 1;
    /// <summary>
    /// Mostrar la vista previa antes de imprimir el ticket. Apagado por pedido
    /// de Yuber (2026-07-12): con un cliente esperando, el ticket sale directo.
    /// Al REIMPRIMIR desde Comprobantes siempre se muestra, sin importar esto.
    /// </summary>
    public bool MostrarVistaPreviaTicket { get; set; }
    public string? TicketEncabezado { get; set; }
    public string? TicketPie { get; set; } = "Gracias por su compra";

    /// <summary>
    /// Carpeta donde se guardan los archivos del expediente digital (018).
    /// Vacío = junto al ejecutable (&lt;app&gt;\expedientes). Se puede apuntar a
    /// otra unidad si el disco del sistema queda corto.
    /// </summary>
    public string CarpetaExpedientes { get; set; } = string.Empty;

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

    /// <summary>
    /// Borra el archivo de ajustes (código 7 — eliminar todo). Los valores en
    /// memoria no se tocan: la app se está por cerrar de todos modos.
    /// </summary>
    public static void Borrar()
    {
        if (File.Exists(Ruta))
            File.Delete(Ruta);
    }
}
