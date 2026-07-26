using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAControl.Common;
using FAControl.Services;
using Serilog;

namespace FAControl.ViewModels;

/// <summary>
/// Configuración: cambio de contraseña, tamaño de texto (pedido de Yuber),
/// respaldo/restauración de la BD y exportación a Excel (manual + automática).
/// Las rutas de archivos las pide la View (SaveFileDialog/OpenFolderDialog).
/// </summary>
public partial class ConfiguracionViewModel : ObservableObject
{
    private readonly AuthService _auth;
    private readonly RespaldoService _respaldo;
    private readonly ExportacionService _exportacion;
    private readonly AjustesLocales _ajustes;
    private readonly IDialogService _dialogos;
    private readonly IAvisoVencidos _avisoVencidos;
    private readonly RecordatorioService _recordatorios;
    private readonly EmailService _email;
    private readonly NcfService _ncf;

    /// <summary>El shell escala la UI cuando cambia el tamaño de texto.</summary>
    public event Action<double>? EscalaCambiada;

    /// <summary>La App intercambia la paleta cuando se activa el modo noche.</summary>
    public event Action<bool>? TemaCambiado;

    public ConfiguracionViewModel(AuthService auth, RespaldoService respaldo,
        ExportacionService exportacion, AjustesLocales ajustes, IDialogService dialogos,
        IAvisoVencidos avisoVencidos, RecordatorioService recordatorios, EmailService email,
        NcfService ncf)
    {
        _auth = auth;
        _respaldo = respaldo;
        _exportacion = exportacion;
        _ajustes = ajustes;
        _dialogos = dialogos;
        _avisoVencidos = avisoVencidos;
        _recordatorios = recordatorios;
        _email = email;
        _ncf = ncf;

        Tamanos =
        [
            new Opcion<TamanoTexto>(TamanoTexto.Pequeno, "Pequeño"),
            new Opcion<TamanoTexto>(TamanoTexto.Mediano, "Mediano"),
            new Opcion<TamanoTexto>(TamanoTexto.Grande, "Grande")
        ];
        _tamanoSeleccionado = Tamanos.First(t => t.Valor == ajustes.TamanoTexto);
        // Campo y no propiedad: asignar la propiedad dispararía el evento y
        // volvería a guardar el ajuste apenas se abre Configuración.
        // El tema es POR MODO (DealControl arranca oscuro).
        _temaOscuro = ajustes.TemaOscuroDe(SesionActual.Modo);
        // La PasswordBox se ve vacía al volver (no se puede rellenar por seguridad):
        // este flag le avisa al usuario que YA hay una guardada.
        _hayAppPasswordGuardada = !string.IsNullOrWhiteSpace(ajustes.GmailAppPasswordCifrada);
        _exportActivo = ajustes.ExportAutomaticoActivo;
        _exportCadaDiasTexto = ajustes.ExportAutomaticoCadaDias.ToString();
        _exportCarpeta = ajustes.ExportAutomaticoCarpeta ?? string.Empty;
        _respaldoAutoActivo = ajustes.RespaldoAutomaticoActivo;
        _respaldoCadaTexto = ajustes.RespaldoAutomaticoCada.ToString();
        _respaldoUnidad = Unidades.FirstOrDefault(u => u.Valor == ajustes.RespaldoAutomaticoUnidad) ?? Unidades[0];
        _respaldoCarpeta = ajustes.RespaldoAutomaticoCarpeta ?? string.Empty;
        _avisoVencidosActivo = ajustes.AvisoVencidosActivo;
        _recordatoriosActivos = ajustes.RecordatoriosActivos;
        _recordatoriosAutomaticos = ajustes.RecordatoriosAutomaticos;
        _gmailRemitente = ajustes.GmailRemitente;
        _correoDueno = ajustes.CorreoDueno;
        _recordatorioDiasTexto = ajustes.RecordatorioDiasAntes.ToString();
        _negocioNombre = ajustes.NombreNegocio;
        _negocioPrestamista = ajustes.Prestamista;
        _negocioCiudad = ajustes.CiudadNegocio;
        _negocioTelefono = ajustes.TelefonoNegocio;
        _negocioEmail = ajustes.EmailNegocio;
        _negocioRnc = ajustes.RncNegocio;
        _comisionVendedorTexto = ajustes.PorcentajeComisionVendedor.ToString("0.##", Textos.CulturaRd);
        ActualizarUltimaExportacion();
        ActualizarUltimoRespaldo();
        ActualizarUltimoRecordatorio();
        ActualizarSilenciados();
    }

    // ---------- Comprobante fiscal / secuencia NCF (cliente 2026-07-25) ----------
    // La empresa está legalizada ante la DGII. Acá se configura la secuencia
    // autorizada (prefijo B02/E32, próxima, fin de rango, vencimiento). Los
    // préstamos toman el siguiente número de forma atómica, o registran el
    // e-NCF generado en el Facturador Gratuito de la DGII.

    [ObservableProperty] private bool _ncfActivo;
    [ObservableProperty] private string _ncfPrefijo = "B02";
    [ObservableProperty] private string _ncfLargoTexto = "8";
    [ObservableProperty] private string _ncfProximaTexto = "1";
    [ObservableProperty] private string _ncfFinTexto = string.Empty;
    [ObservableProperty] private DateTime? _ncfVencimiento;
    [ObservableProperty] private string _ncfEstadoTexto = string.Empty;
    /// <summary>Solo el Admin ve/edita la secuencia.</summary>
    public bool PuedeConfigurarNcf => SesionActual.EsAdmin;

    /// <summary>La View lo llama al cargarse (la secuencia vive en la BD, no en ajustes.json).</summary>
    public async Task CargarNcfAsync()
    {
        try
        {
            var secuencia = await _ncf.ObtenerSecuenciaAsync();
            if (secuencia is null)
            {
                NcfActivo = false;
                NcfEstadoTexto = "Sin secuencia configurada. Los préstamos igual pueden registrar " +
                                 "un e-NCF generado en el Facturador Gratuito de la DGII.";
                return;
            }
            NcfActivo = secuencia.Activo;
            NcfPrefijo = secuencia.Prefijo;
            NcfLargoTexto = secuencia.Largo.ToString();
            NcfProximaTexto = secuencia.Proxima.ToString();
            NcfFinTexto = secuencia.FinRango?.ToString() ?? string.Empty;
            NcfVencimiento = secuencia.Vencimiento?.ToDateTime(TimeOnly.MinValue);
            ActualizarEstadoNcf(secuencia);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cargando la secuencia NCF");
            NcfEstadoTexto = "No se pudo cargar la configuración de comprobantes.";
        }
    }

    private void ActualizarEstadoNcf(FAControl.Models.NcfSecuencia secuencia)
    {
        if (!secuencia.Activo)
        {
            NcfEstadoTexto = "Secuencia desactivada.";
            return;
        }
        var hoy = FechaNegocio.Hoy;
        var proximo = secuencia.Formatear(secuencia.Proxima);
        if (secuencia.EstaVencida(hoy))
            NcfEstadoTexto = $"⚠ La secuencia venció el {secuencia.Vencimiento:dd/MM/yyyy}. Solicitá una nueva a la DGII.";
        else if (secuencia.EstaAgotada)
            NcfEstadoTexto = "⚠ La secuencia se agotó (fin del rango). Solicitá una nueva a la DGII.";
        else
        {
            NcfEstadoTexto = $"Próximo comprobante: {proximo}";
            if (secuencia.Restantes is { } restantes)
                NcfEstadoTexto += restantes <= 20
                    ? $" — ⚠ quedan solo {restantes}"
                    : $" — quedan {restantes}";
            if (secuencia.Vencimiento is { } v)
                NcfEstadoTexto += $" · vence {v:dd/MM/yyyy}";
        }
    }

    [RelayCommand]
    private async Task GuardarNcfAsync()
    {
        try
        {
            if (!int.TryParse(NcfLargoTexto, out var largo))
            {
                _dialogos.MostrarError("Comprobante fiscal", "El largo de la secuencia debe ser un número (8 tradicional, 10 e-CF).");
                return;
            }
            if (!long.TryParse(NcfProximaTexto, out var proxima))
            {
                _dialogos.MostrarError("Comprobante fiscal", "La próxima secuencia debe ser un número.");
                return;
            }
            long? fin = null;
            if (!string.IsNullOrWhiteSpace(NcfFinTexto))
            {
                if (!long.TryParse(NcfFinTexto, out var f))
                {
                    _dialogos.MostrarError("Comprobante fiscal", "El fin del rango debe ser un número (o vacío).");
                    return;
                }
                fin = f;
            }

            var secuencia = new FAControl.Models.NcfSecuencia
            {
                Prefijo = NcfPrefijo,
                Largo = largo,
                Proxima = proxima,
                FinRango = fin,
                Vencimiento = NcfVencimiento is { } v ? DateOnly.FromDateTime(v) : null,
                Activo = NcfActivo
            };
            await _ncf.GuardarSecuenciaAsync(secuencia);
            ActualizarEstadoNcf(secuencia);
            _dialogos.Informar("Comprobante fiscal", "Configuración de la secuencia guardada.");
        }
        catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException)
        {
            _dialogos.MostrarError("Comprobante fiscal", ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error guardando la secuencia NCF");
            _dialogos.MostrarError("Comprobante fiscal", $"No se pudo guardar.\n\n{ex.Message}");
        }
    }

    // ---------- Datos del negocio (cliente 2026-07-25) ----------
    // Aparecen en el pagaré, el recibo/factura y la intimación. El RNC y el
    // teléfono son los que exige la factura con comprobante fiscal.

    [ObservableProperty] private string _negocioNombre;
    [ObservableProperty] private string _negocioPrestamista;
    [ObservableProperty] private string _negocioCiudad;
    [ObservableProperty] private string _negocioTelefono;
    [ObservableProperty] private string _negocioEmail;
    [ObservableProperty] private string _negocioRnc;
    /// <summary>% de comisión del vendedor sobre el monto vendido (DealControl).</summary>
    [ObservableProperty] private string _comisionVendedorTexto;

    partial void OnComisionVendedorTextoChanged(string value) => GuardarAjustesNegocio();
    partial void OnNegocioNombreChanged(string value) => GuardarAjustesNegocio();
    partial void OnNegocioPrestamistaChanged(string value) => GuardarAjustesNegocio();
    partial void OnNegocioCiudadChanged(string value) => GuardarAjustesNegocio();
    partial void OnNegocioTelefonoChanged(string value) => GuardarAjustesNegocio();
    partial void OnNegocioEmailChanged(string value) => GuardarAjustesNegocio();
    partial void OnNegocioRncChanged(string value) => GuardarAjustesNegocio();

    private void GuardarAjustesNegocio()
    {
        // El nombre nunca queda vacío: es el acreedor del pagaré y el título del recibo
        if (!string.IsNullOrWhiteSpace(NegocioNombre))
            _ajustes.NombreNegocio = NegocioNombre.Trim();
        _ajustes.Prestamista = NegocioPrestamista?.Trim() ?? string.Empty;
        _ajustes.CiudadNegocio = NegocioCiudad?.Trim() ?? string.Empty;
        _ajustes.TelefonoNegocio = NegocioTelefono?.Trim() ?? string.Empty;
        _ajustes.EmailNegocio = NegocioEmail?.Trim() ?? string.Empty;
        _ajustes.RncNegocio = NegocioRnc?.Trim() ?? string.Empty;
        // Comisión: se ignora lo que no sea un número válido (el campo se está escribiendo)
        if (decimal.TryParse(ComisionVendedorTexto, System.Globalization.NumberStyles.Number,
                Textos.CulturaRd, out var comision) && comision >= 0m && comision <= 100m)
            _ajustes.PorcentajeComisionVendedor = comision;
        _ajustes.Guardar();
    }

    // ---------- Recordatorios por correo (cliente 2026-07-19) ----------

    [ObservableProperty] private bool _recordatoriosActivos;
    [ObservableProperty] private bool _recordatoriosAutomaticos;
    [ObservableProperty] private string _gmailRemitente;
    [ObservableProperty] private string _correoDueno;
    [ObservableProperty] private string _recordatorioDiasTexto;
    [ObservableProperty] private string _mensajeCorreo = string.Empty;
    /// <summary>True si ya hay un App Password guardado (la View lo indica).</summary>
    [ObservableProperty] private bool _hayAppPasswordGuardada;
    /// <summary>La contraseña de app llega de la View (PasswordBox no se bindea).</summary>
    public string GmailAppPassword { get; set; } = string.Empty;

    partial void OnRecordatoriosActivosChanged(bool value) => GuardarAjustesCorreo();
    partial void OnRecordatoriosAutomaticosChanged(bool value) => GuardarAjustesCorreo();
    partial void OnGmailRemitenteChanged(string value) => GuardarAjustesCorreo();
    partial void OnCorreoDuenoChanged(string value) => GuardarAjustesCorreo();
    partial void OnRecordatorioDiasTextoChanged(string value) => GuardarAjustesCorreo();

    private void GuardarAjustesCorreo()
    {
        _ajustes.RecordatoriosActivos = RecordatoriosActivos;
        _ajustes.RecordatoriosAutomaticos = RecordatoriosAutomaticos;
        _ajustes.GmailRemitente = GmailRemitente?.Trim() ?? string.Empty;
        _ajustes.CorreoDueno = CorreoDueno?.Trim() ?? string.Empty;
        if (int.TryParse(RecordatorioDiasTexto, out var dias) && dias >= 0)
            _ajustes.RecordatorioDiasAntes = dias;
        // La contraseña solo se guarda si el usuario escribió una (no la pisa con vacío)
        if (!string.IsNullOrEmpty(GmailAppPassword))
            _ajustes.GmailAppPassword = GmailAppPassword;
        _ajustes.Guardar();
    }

    /// <summary>
    /// Guarda la contraseña recién escrita (la View la pasa aparte). Se le quitan
    /// los ESPACIOS: Google muestra la contraseña de aplicación como "abcd efgh
    /// ijkl mnop" y si se pega con espacios, la autenticación falla.
    /// </summary>
    public void GuardarPasswordCorreo(string password)
    {
        GmailAppPassword = (password ?? string.Empty).Replace(" ", string.Empty);
        GuardarAjustesCorreo();
        HayAppPasswordGuardada = !string.IsNullOrWhiteSpace(_ajustes.GmailAppPasswordCifrada);
    }

    /// <summary>
    /// Traduce el error de SMTP a algo accionable. El 99% de las fallas de Gmail
    /// son de credenciales: contraseña NORMAL en vez de la de aplicación, cuenta
    /// sin verificación en 2 pasos, o espacios pegados.
    /// </summary>
    private static string MensajeErrorCorreo(Exception ex)
    {
        var m = ex.Message;
        var esAuth = m.Contains("5.7.0") || m.Contains("Authentication", StringComparison.OrdinalIgnoreCase)
            || m.Contains("not accepted", StringComparison.OrdinalIgnoreCase)
            || m.Contains("BadAuthentication", StringComparison.OrdinalIgnoreCase);
        if (esAuth)
            return "Gmail rechazó las credenciales. Revisá que: 1) uses una CONTRASEÑA DE " +
                   "APLICACIÓN de 16 caracteres (no tu contraseña normal de Gmail ni la de " +
                   "FAControl); 2) la cuenta remitente tenga la verificación en 2 pasos ACTIVADA; " +
                   "3) la pegues sin espacios. Generala en myaccount.google.com → Seguridad → " +
                   "Contraseñas de aplicaciones.";
        return $"No se pudo enviar: {m}";
    }

    /// <summary>Envía un correo de prueba al dueño (o al remitente) para verificar la config.</summary>
    [RelayCommand]
    private async Task EnviarPruebaCorreoAsync()
    {
        MensajeCorreo = string.Empty;
        var destino = string.IsNullOrWhiteSpace(_ajustes.CorreoDueno)
            ? _ajustes.GmailRemitente
            : _ajustes.CorreoDueno;
        if (string.IsNullOrWhiteSpace(destino))
        {
            MensajeCorreo = "Configurá al menos la cuenta de Gmail o el correo del dueño.";
            return;
        }
        try
        {
            Ocupado = true;
            await _email.EnviarAsync(destino, "Prueba de FAControl",
                "Este es un correo de prueba de FAControl. Si lo recibiste, la configuración es correcta.");
            MensajeCorreo = $"Correo de prueba enviado a {destino}.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Falló el correo de prueba");
            MensajeCorreo = MensajeErrorCorreo(ex);
        }
        finally
        {
            Ocupado = false;
        }
    }

    /// <summary>Envía los recordatorios AHORA (manual).</summary>
    [RelayCommand]
    private async Task EnviarRecordatoriosAsync()
    {
        MensajeCorreo = string.Empty;
        if (!_email.EstaConfigurado)
        {
            MensajeCorreo = "Configurá la cuenta de Gmail y la contraseña de aplicación primero.";
            return;
        }
        try
        {
            Ocupado = true;
            var r = await _recordatorios.EnviarAsync();
            ActualizarUltimoRecordatorio();
            MensajeCorreo =
                $"{r.CorreosACliente} recordatorio(s) a clientes" +
                (r.SinEmail > 0 ? $" ({r.SinEmail} sin email)" : "") +
                (r.ResumenAlDueno ? ", resumen al dueño enviado" : "") + ".";
            if (r.Detalle.StartsWith("Con errores"))
                MensajeCorreo += "\n" + r.Detalle;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Falló el envío manual de recordatorios");
            MensajeCorreo = MensajeErrorCorreo(ex);
        }
        finally
        {
            Ocupado = false;
        }
    }

    [ObservableProperty] private string _ultimoRecordatorioTexto = string.Empty;

    private void ActualizarUltimoRecordatorio() =>
        UltimoRecordatorioTexto = _ajustes.UltimoRecordatorioUtc is { } fecha
            ? $"Último envío: {FechaNegocio.AUtcLocal(fecha):dd/MM/yyyy hh:mm tt}"
            : "Aún no se han enviado recordatorios.";

    public IReadOnlyList<Opcion<string>> Unidades { get; } =
    [
        new Opcion<string>("dias", "días"),
        new Opcion<string>("meses", "meses")
    ];

    // ---------- Aviso de vencimientos ----------

    [ObservableProperty] private bool _avisoVencidosActivo;
    [ObservableProperty] private string _silenciadosTexto = string.Empty;
    [ObservableProperty] private bool _haySilenciados;

    partial void OnAvisoVencidosActivoChanged(bool value)
    {
        _ajustes.AvisoVencidosActivo = value;
        _ajustes.Guardar();
    }

    [RelayCommand]
    private async Task RestablecerSilenciadosAsync()
    {
        if (!_dialogos.Confirmar("Restablecer avisos",
            "Los clientes silenciados volverán a aparecer en el aviso de vencimientos. ¿Continuar?"))
            return;

        var restablecidos = _ajustes.AvisoVencidosSilenciados.Count;
        _ajustes.AvisoVencidosSilenciados.Clear();
        _ajustes.Guardar();
        ActualizarSilenciados();

        if (!_ajustes.AvisoVencidosActivo)
        {
            _dialogos.Informar("Avisos restablecidos",
                $"Se restablecieron {restablecidos} cliente(s), pero el aviso de vencimientos " +
                "está desactivado: no volverá a aparecer hasta que lo actives arriba.");
            return;
        }

        // Vuelve a mostrar el aviso EN EL MOMENTO. Sin esto, el guard de
        // "ya avisé hoy" del notificador lo posterga hasta mañana y el botón
        // parece no hacer nada (bug reportado por el cliente 2026-07-16).
        await _avisoVencidos.RevisarAhoraAsync();
    }

    private void ActualizarSilenciados()
    {
        var cantidad = _ajustes.AvisoVencidosSilenciados.Count;
        HaySilenciados = cantidad > 0;
        SilenciadosTexto = cantidad switch
        {
            0 => "Ningún cliente silenciado.",
            1 => "1 cliente silenciado (no aparece en el aviso).",
            _ => $"{cantidad} clientes silenciados (no aparecen en el aviso)."
        };
    }

    // ---------- Apariencia ----------

    public IReadOnlyList<Opcion<TamanoTexto>> Tamanos { get; }

    [ObservableProperty] private Opcion<TamanoTexto> _tamanoSeleccionado;
    [ObservableProperty] private bool _temaOscuro;

    partial void OnTamanoSeleccionadoChanged(Opcion<TamanoTexto> value)
    {
        _ajustes.TamanoTexto = value.Valor;
        _ajustes.Guardar();
        EscalaCambiada?.Invoke(_ajustes.FactorEscala);
    }

    /// <summary>
    /// Modo noche: se aplica en el momento y se recuerda POR MODO y por PC
    /// (DealControl arranca oscuro — Yuber 2026-07-18).
    /// </summary>
    partial void OnTemaOscuroChanged(bool value)
    {
        _ajustes.FijarTemaOscuro(SesionActual.Modo, value);
        _ajustes.Guardar();
        TemaCambiado?.Invoke(value);
    }

    /// <summary>
    /// Re-sincroniza el toggle con el tema del modo activo (al cambiar de
    /// estancia). Se llama en EstablecerModo, antes de que App se suscriba a
    /// TemaCambiado, así que no re-aplica el tema (ya lo puso App al entrar);
    /// a lo sumo materializa el default del modo en el ajuste, sin efecto visible.
    /// </summary>
    public void SincronizarTema() => TemaOscuro = _ajustes.TemaOscuroDe(SesionActual.Modo);

    // ---------- Cambio de contraseña ----------

    [ObservableProperty] private string _mensajePassword = string.Empty;
    [ObservableProperty] private bool _passwordCambiada;

    /// <summary>Las contraseñas llegan de PasswordBox (nunca se guardan en propiedades).</summary>
    public async Task CambiarPasswordAsync(string actual, string nueva, string confirmacion)
    {
        try
        {
            PasswordCambiada = false;
            if (nueva != confirmacion)
            {
                MensajePassword = "La nueva contraseña y su confirmación no coinciden.";
                return;
            }

            await _auth.CambiarPasswordAsync(actual, nueva);
            MensajePassword = string.Empty;
            PasswordCambiada = true;
            _dialogos.Informar("Contraseña", "La contraseña se cambió correctamente.");
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            MensajePassword = ex.Message;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cambiando la contraseña");
            _dialogos.MostrarError("Contraseña", $"No se pudo cambiar la contraseña.\n\n{ex.Message}");
        }
    }

    // ---------- Respaldo / restauración ----------

    [ObservableProperty] private bool _ocupado;

    public async Task RespaldarAsync(string ruta)
    {
        try
        {
            Ocupado = true;
            await _respaldo.RespaldarAsync(ruta);
            _dialogos.Informar("Respaldo", $"Respaldo generado en:\n{ruta}");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error generando el respaldo");
            _dialogos.MostrarError("Respaldo", $"No se pudo generar el respaldo.\n\n{ex.Message}");
        }
        finally
        {
            Ocupado = false;
        }
    }

    public async Task RestaurarAsync(string ruta)
    {
        // Doble confirmación: es DESTRUCTIVO
        if (!_dialogos.Confirmar("Restaurar base de datos",
            "Restaurar REEMPLAZA todos los datos actuales por los del archivo.\n\n" +
            "¿Seguro que querés continuar?"))
            return;
        if (!_dialogos.Confirmar("Confirmación final",
            "Última confirmación: los datos actuales se perderán si no tenés respaldo.\n\n¿Restaurar ahora?"))
            return;

        try
        {
            Ocupado = true;
            await _respaldo.RestaurarAsync(ruta);
            _dialogos.Informar("Restaurar",
                "Base de datos restaurada. Cerrá y volvé a abrir FAControl para recargar todo.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error restaurando la base de datos");
            _dialogos.MostrarError("Restaurar", $"No se pudo restaurar.\n\n{ex.Message}");
        }
        finally
        {
            Ocupado = false;
        }
    }

    // ---------- Exportación a Excel ----------

    [ObservableProperty] private bool _exportActivo;
    [ObservableProperty] private string _exportCadaDiasTexto;
    [ObservableProperty] private string _exportCarpeta;
    [ObservableProperty] private string _ultimaExportacionTexto = string.Empty;

    partial void OnExportActivoChanged(bool value) => GuardarAjustesExport();
    partial void OnExportCadaDiasTextoChanged(string value) => GuardarAjustesExport();
    partial void OnExportCarpetaChanged(string value) => GuardarAjustesExport();

    private void GuardarAjustesExport()
    {
        _ajustes.ExportAutomaticoActivo = ExportActivo;
        if (int.TryParse(ExportCadaDiasTexto, out var dias) && dias >= 1)
            _ajustes.ExportAutomaticoCadaDias = dias;
        _ajustes.ExportAutomaticoCarpeta = string.IsNullOrWhiteSpace(ExportCarpeta) ? null : ExportCarpeta;
        _ajustes.Guardar();
    }

    // ---------- Respaldo automático (cliente 2026-07-19) ----------

    [ObservableProperty] private bool _respaldoAutoActivo;
    [ObservableProperty] private string _respaldoCadaTexto;
    [ObservableProperty] private Opcion<string> _respaldoUnidad;
    [ObservableProperty] private string _respaldoCarpeta;
    [ObservableProperty] private string _ultimoRespaldoTexto = string.Empty;

    partial void OnRespaldoAutoActivoChanged(bool value) => GuardarAjustesRespaldoAuto();
    partial void OnRespaldoCadaTextoChanged(string value) => GuardarAjustesRespaldoAuto();
    partial void OnRespaldoUnidadChanged(Opcion<string> value) => GuardarAjustesRespaldoAuto();
    partial void OnRespaldoCarpetaChanged(string value) => GuardarAjustesRespaldoAuto();

    private void GuardarAjustesRespaldoAuto()
    {
        _ajustes.RespaldoAutomaticoActivo = RespaldoAutoActivo;
        if (int.TryParse(RespaldoCadaTexto, out var cada) && cada >= 1)
            _ajustes.RespaldoAutomaticoCada = cada;
        _ajustes.RespaldoAutomaticoUnidad = RespaldoUnidad?.Valor ?? "dias";
        _ajustes.RespaldoAutomaticoCarpeta = string.IsNullOrWhiteSpace(RespaldoCarpeta) ? null : RespaldoCarpeta;
        _ajustes.Guardar();
    }

    private void ActualizarUltimoRespaldo() =>
        UltimoRespaldoTexto = _ajustes.UltimoRespaldoUtc is { } fecha
            ? $"Último respaldo automático: {FechaNegocio.AUtcLocal(fecha):dd/MM/yyyy hh:mm tt}"
            : "Aún no se ha hecho un respaldo automático.";

    public async Task ExportarAhoraAsync(string ruta)
    {
        try
        {
            Ocupado = true;
            await _exportacion.ExportarAsync(ruta);
            _ajustes.UltimaExportacionUtc = DateTime.UtcNow;
            _ajustes.Guardar();
            ActualizarUltimaExportacion();
            _dialogos.Informar("Exportar", $"Datos exportados a:\n{ruta}");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error exportando a Excel");
            _dialogos.MostrarError("Exportar", $"No se pudo exportar.\n\n{ex.Message}");
        }
        finally
        {
            Ocupado = false;
        }
    }

    private void ActualizarUltimaExportacion() =>
        UltimaExportacionTexto = _ajustes.UltimaExportacionUtc is { } ultima
            ? $"Última exportación: {FechaNegocio.AUtcLocal(ultima):dd/MM/yyyy hh:mm tt}"
            : "Aún no se ha exportado.";
}
