using System.Collections.ObjectModel;
using System.Globalization;
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
    /// <summary>Aviso de mercancia proxima a caducar: el equivalente del anterior en el POS.</summary>
    private readonly FAControl.Services.Pos.RecordatorioCaducidadService _caducidad;
    private readonly EmailService _email;
    private readonly NcfService _ncf;
    /// <summary>Configuracion del NEGOCIO del punto de venta (vive en la BD, no en ajustes.json).</summary>
    private readonly FAControl.Services.Pos.ConfiguracionNegocioService _negocioPos;

    /// <summary>El shell escala la UI cuando cambia el tamaño de texto.</summary>
    public event Action<double>? EscalaCambiada;

    /// <summary>La App intercambia la paleta cuando se activa el modo noche.</summary>
    public event Action<bool>? TemaCambiado;

    public ConfiguracionViewModel(AuthService auth, RespaldoService respaldo,
        ExportacionService exportacion, AjustesLocales ajustes, IDialogService dialogos,
        IAvisoVencidos avisoVencidos, RecordatorioService recordatorios,
        FAControl.Services.Pos.RecordatorioCaducidadService caducidad, EmailService email,
        NcfService ncf, FAControl.Services.Pos.ConfiguracionNegocioService negocioPos)
    {
        _auth = auth;
        _respaldo = respaldo;
        _exportacion = exportacion;
        _ajustes = ajustes;
        _dialogos = dialogos;
        _avisoVencidos = avisoVencidos;
        _recordatorios = recordatorios;
        _caducidad = caducidad;
        _email = email;
        _ncf = ncf;
        _negocioPos = negocioPos;

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
        _recordatorioDiasTexto = (SesionActual.Modo == ModoApp.Pos500
            ? ajustes.AvisoCaducidadDias
            : ajustes.RecordatorioDiasAntes).ToString();
        _negocioNombre = ajustes.NombreNegocio;
        _negocioPrestamista = ajustes.Prestamista;
        _negocioCiudad = ajustes.CiudadNegocio;
        _negocioTelefono = ajustes.TelefonoNegocio;
        _negocioEmail = ajustes.EmailNegocio;
        _negocioRnc = ajustes.RncNegocio;
        _comisionVendedorTexto = ajustes.PorcentajeComisionVendedor.ToString("0.##", Textos.CulturaRd);
        // Impresión del ticket (POS-500)
        _mostrarVistaPreviaTicket = ajustes.MostrarVistaPreviaTicket;
        _copiasTicketTexto = ajustes.CopiasTicket.ToString();
        _ticketEncabezado = ajustes.TicketEncabezado ?? string.Empty;
        _ticketPie = ajustes.TicketPie ?? string.Empty;
        ActualizarUltimaExportacion();
        ActualizarUltimoRespaldo();
        ActualizarUltimoRecordatorio();
        ActualizarSilenciados();
    }

    // ---------- ITBIS del punto de venta (pedido de Yuber 2026-07-31) ----------
    // "se necesita un checkbox para deshabilitar el uso del ITBIS, junto a un
    //  textbox para saber cuanto % de itbis se usara."
    //
    // El motor ya sabia apagarlo (ItbisTasaEfectiva devuelve 0 con el ITBIS en
    // OFF); lo que faltaba era la pantalla: al portar POS-500 a la suite no se
    // trajo su Configuracion del negocio.
    //
    // Esto NO vive en ajustes.json como el resto de esta pantalla: es del
    // NEGOCIO, no de la PC. Si el dueño apaga el ITBIS, se apaga en todas las
    // terminales, no solo en la caja donde lo toco.

    [ObservableProperty] private bool _itbisActivo = true;
    [ObservableProperty] private string _itbisTasaTexto = "18";
    [ObservableProperty] private string _itbisEstadoTexto = string.Empty;

    /// <summary>
    /// La seccion se ve solo en el punto de venta y solo al Admin: el impuesto
    /// es una decision del negocio, no de quien esta en la caja. Se resuelve
    /// aca y no con un MultiBinding en XAML porque es una regla, no una
    /// decoracion, y ademas no hay converter multi-valor en el proyecto.
    /// </summary>
    public bool PuedeConfigurarItbis => EsPos500 && SesionActual.EsAdmin;

    /// <summary>La View lo llama al cargarse (la configuracion vive en la BD).</summary>
    public async Task CargarItbisAsync()
    {
        if (!EsPos500)
            return;
        try
        {
            await _negocioPos.CargarAsync();
            var cfg = _negocioPos.Actual;
            _recargando = true;
            try
            {
                ItbisActivo = cfg.ItbisActivo;
                ItbisTasaTexto = cfg.ItbisTasa.ToString("0.##", Textos.CulturaRd);
            }
            finally { _recargando = false; }
            ActualizarEstadoItbis();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cargando la configuracion de ITBIS");
            ItbisEstadoTexto = "No se pudo leer la configuración del ITBIS.";
        }
    }

    private void ActualizarEstadoItbis() =>
        ItbisEstadoTexto = ItbisActivo
            ? $"Se cobra {ItbisTasaTexto}% de ITBIS en cada venta y sale detallado en el ticket."
            : "El ITBIS está APAGADO: las ventas no lo cobran y el ticket no lo muestra.";

    partial void OnItbisActivoChanged(bool value) => ActualizarEstadoItbis();
    partial void OnItbisTasaTextoChanged(string value) => ActualizarEstadoItbis();

    /// <summary>
    /// Guarda el ITBIS. Con boton y no al vuelo como el resto de esta pantalla:
    /// cambia lo que se le cobra al cliente en TODAS las terminales, no es una
    /// preferencia de esta PC.
    /// </summary>
    [RelayCommand]
    private async Task GuardarItbisAsync()
    {
        try
        {
            if (!decimal.TryParse(ItbisTasaTexto, NumberStyles.Number, Textos.CulturaRd, out var tasa)
                || tasa is < 0m or > 100m)
            {
                _dialogos.MostrarError("ITBIS", "La tasa debe ser un número entre 0 y 100 (en RD son 18).");
                return;
            }

            var cfg = _negocioPos.Actual;
            cfg.ItbisActivo = ItbisActivo;
            cfg.ItbisTasa = tasa;
            await _negocioPos.GuardarAsync(cfg);

            ActualizarEstadoItbis();
            _dialogos.Informar("ITBIS", ItbisActivo
                ? $"Listo: de ahora en más se cobra {tasa:0.##}% de ITBIS."
                : "Listo: el ITBIS quedó apagado. Las ventas nuevas no lo cobran.");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or ArgumentException)
        {
            _dialogos.MostrarError("ITBIS", ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error guardando la configuracion de ITBIS");
            _dialogos.MostrarError("ITBIS", $"No se pudo guardar.\n\n{ex.Message}");
        }
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

    // ---------- Textos de la sección de correo, según la estancia ----------
    // Pedido del cliente (2026-07-30): en el punto de venta el correo no va a
    // los clientes —no deben nada— sino al dueño, avisando de la mercancía que
    // se está por vencer. La sección es la misma; cambia a quién le escribe y
    // de qué.

    /// <summary>
    /// La comision del vendedor SOLO la usa el reporte de DealControl
    /// (ReporteDealService la aplica sobre el monto vendido). En PrestControl y
    /// en el punto de venta no la lee nadie, asi que mostrarla ahi es ofrecer
    /// una perilla que no hace nada — verificado el 2026-07-31 a pedido de Yuber.
    /// </summary>
    public bool MuestraComisionVendedor => SesionActual.Modo == ModoApp.DealerControl;

    public string RecordatoriosTitulo => EsPos500
        ? "Aviso de caducidad por correo (Gmail)"
        : "Recordatorios por correo (Gmail)";

    public string RecordatoriosDescripcion => EsPos500
        ? "Le envía UN correo al dueño con los productos ya caducados y los que están por caducar, " +
          "con las unidades y el valor en riesgo. A los clientes no se les escribe: en el punto de " +
          "venta no queda nada por cobrar. Si no hay nada que avisar, no se manda correo."
        : "Envía un recordatorio de pago a cada cliente con cuota por vencer o vencida, y un resumen " +
          "al dueño. El correo de cada cliente se pone en su ficha (pantalla Clientes).";

    public string RecordatoriosActivarTexto => EsPos500
        ? "Activar el aviso de caducidad por correo"
        : "Activar recordatorios por correo";

    public string RecordatoriosDiasEtiqueta => EsPos500
        ? "Avisar (días antes de caducar)"
        : "Avisar (días antes)";

    public string RecordatoriosBotonTexto => EsPos500
        ? "Enviar aviso ahora"
        : "Enviar recordatorios ahora";

    /// <summary>En el POS el destinatario es el único: no es "el del resumen", es EL correo.</summary>
    public string CorreoDuenoEtiqueta => EsPos500
        ? "Correo del dueño (destinatario)"
        : "Correo del dueño (resumen)";

    /// <summary>
    /// Reevalúa todo lo que depende de la estancia activa. Hay secciones que
    /// solo existen en un modo (las de préstamos no van en el punto de venta y
    /// viceversa) y textos que cambian de significado.
    /// </summary>
    private void NotificarCambioDeEstancia()
    {
        OnPropertyChanged(nameof(EsPos500));
        OnPropertyChanged(nameof(RecordatoriosTitulo));
        OnPropertyChanged(nameof(RecordatoriosDescripcion));
        OnPropertyChanged(nameof(RecordatoriosActivarTexto));
        OnPropertyChanged(nameof(RecordatoriosDiasEtiqueta));
        OnPropertyChanged(nameof(RecordatoriosBotonTexto));
        OnPropertyChanged(nameof(CorreoDuenoEtiqueta));
        OnPropertyChanged(nameof(PuedeConfigurarItbis));
        OnPropertyChanged(nameof(MuestraComisionVendedor));

        // Los "días antes" salen de un ajuste distinto en cada estancia: hay que
        // releerlo, si no la caja queda mostrando el número de la anterior.
        // El guardado se suspende mientras tanto: escribir la propiedad dispara
        // el guardado, y guardaría el número de la estancia vieja en el ajuste
        // de la nueva.
        _recargando = true;
        try
        {
            RecordatorioDiasTexto = (EsPos500
                ? _ajustes.AvisoCaducidadDias
                : _ajustes.RecordatorioDiasAntes).ToString();
        }
        finally { _recargando = false; }
    }

    /// <summary>True mientras se recargan campos desde los ajustes (no guardar).</summary>
    private bool _recargando;

    partial void OnRecordatoriosActivosChanged(bool value) => GuardarAjustesCorreo();
    partial void OnRecordatoriosAutomaticosChanged(bool value) => GuardarAjustesCorreo();
    partial void OnGmailRemitenteChanged(string value) => GuardarAjustesCorreo();
    partial void OnCorreoDuenoChanged(string value) => GuardarAjustesCorreo();
    partial void OnRecordatorioDiasTextoChanged(string value) => GuardarAjustesCorreo();

    private void GuardarAjustesCorreo()
    {
        if (_recargando)
            return;

        _ajustes.RecordatoriosActivos = RecordatoriosActivos;
        _ajustes.RecordatoriosAutomaticos = RecordatoriosAutomaticos;
        _ajustes.GmailRemitente = GmailRemitente?.Trim() ?? string.Empty;
        _ajustes.CorreoDueno = CorreoDueno?.Trim() ?? string.Empty;
        // Los "días antes" apuntan a cosas distintas según la estancia: acá se
        // avisa de cuotas por vencer, en el POS de mercancía por caducar. Es la
        // misma idea ("avisame N días antes"), así que se reusa el mismo campo
        // en pantalla y se guarda en el ajuste que corresponde. Dos perillas
        // separadas para lo mismo terminarían contradiciéndose.
        if (int.TryParse(RecordatorioDiasTexto, out var dias) && dias >= 0)
        {
            if (EsPos500)
                _ajustes.AvisoCaducidadDias = dias;
            else
                _ajustes.RecordatorioDiasAntes = dias;
        }
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

    /// <summary>
    /// Envía los recordatorios AHORA (manual). QUÉ se envía depende de la
    /// estancia (pedido del cliente 2026-07-30):
    ///  * En el punto de venta, UN correo al dueño con los productos caducados
    ///    o por caducar. Ahí el cliente no debe nada; lo que corre riesgo es la
    ///    mercancía.
    ///  * En las demás, el recordatorio de cuota a cada cliente más el resumen
    ///    al dueño, que es lo que empuja a cobrar.
    /// </summary>
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

            if (EsPos500)
            {
                var caducidad = await _caducidad.EnviarAsync();
                ActualizarUltimoRecordatorio();
                MensajeCorreo = caducidad.Total == 0
                    ? caducidad.Detalle
                    : $"{caducidad.Caducados} caducado(s) y {caducidad.PorCaducar} por caducar. " +
                      caducidad.Detalle;
                return;
            }

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

    // ---------- Impresión del ticket (POS-500, 2026-07-30) ----------
    // Preferencias POR PC: la impresora del mostrador es de ESA terminal.
    // Esta sección es la que faltaba portar del POS-500, y es la que resuelve
    // el "me manda a guardar un PDF": sin impresora elegida, Windows usa la
    // predeterminada del sistema — que en muchas PC es "Microsoft Print to PDF".

    /// <summary>Solo tiene sentido dentro del punto de venta.</summary>
    public bool EsPos500 => SesionActual.Modo == ModoApp.Pos500;

    /// <summary>Impresoras instaladas. La llena la View: enumerarlas es cosa de UI.</summary>
    public ObservableCollection<string> Impresoras { get; } = [];

    [ObservableProperty] private string? _impresoraSeleccionada;
    [ObservableProperty] private bool _mostrarVistaPreviaTicket;
    [ObservableProperty] private string _copiasTicketTexto = "1";
    [ObservableProperty] private string _ticketEncabezado = string.Empty;
    [ObservableProperty] private string _ticketPie = string.Empty;

    /// <summary>
    /// Carga la lista de impresoras que le pasa la View y marca la guardada.
    /// Si la guardada ya no está (se desinstaló, se cambió de PC), queda en
    /// "la predeterminada de Windows" en vez de apuntar a algo que no existe.
    /// </summary>
    public void EstablecerImpresoras(IEnumerable<string> instaladas)
    {
        Impresoras.Clear();
        Impresoras.Add(ImpresoraPredeterminadaDelSistema);
        foreach (var nombre in instaladas.OrderBy(n => n))
            Impresoras.Add(nombre);

        var guardada = _ajustes.ImpresoraPredeterminada;
        ImpresoraSeleccionada = !string.IsNullOrWhiteSpace(guardada) && Impresoras.Contains(guardada)
            ? guardada
            : ImpresoraPredeterminadaDelSistema;

        NotificarCambioDeEstancia();
    }

    /// <summary>Opción "usar la que Windows tenga como predeterminada".</summary>
    public const string ImpresoraPredeterminadaDelSistema = "(la predeterminada de Windows)";

    partial void OnImpresoraSeleccionadaChanged(string? value)
    {
        _ajustes.ImpresoraPredeterminada =
            value is null || value == ImpresoraPredeterminadaDelSistema ? null : value;
        _ajustes.Guardar();
    }

    partial void OnMostrarVistaPreviaTicketChanged(bool value)
    {
        _ajustes.MostrarVistaPreviaTicket = value;
        _ajustes.Guardar();
    }

    partial void OnCopiasTicketTextoChanged(string value)
    {
        // Entre 1 y 3: más copias de un ticket no tiene sentido y gasta papel
        if (int.TryParse(value, out var copias) && copias is >= 1 and <= 3)
        {
            _ajustes.CopiasTicket = copias;
            _ajustes.Guardar();
        }
    }

    partial void OnTicketEncabezadoChanged(string value)
    {
        _ajustes.TicketEncabezado = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        _ajustes.Guardar();
    }

    partial void OnTicketPieChanged(string value)
    {
        _ajustes.TicketPie = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        _ajustes.Guardar();
    }

    // ---------- Arranque directo (cliente 2026-07-29) ----------
    // Se prende marcando la casilla del launcher; acá se puede prender para la
    // estancia activa y, sobre todo, APAGAR para volver a ver el launcher.

    [ObservableProperty] private bool _arranqueDirectoActivo;
    [ObservableProperty] private string _arranqueDirectoTexto = string.Empty;

    partial void OnArranqueDirectoActivoChanged(bool value)
    {
        _ajustes.FijarArranqueDirecto(value ? SesionActual.Modo : null);
        _ajustes.Guardar();
        ActualizarArranqueTexto();
    }

    /// <summary>Re-sincroniza la casilla con el modo activo (al cambiar de estancia).</summary>
    public void SincronizarArranque()
    {
        ArranqueDirectoActivo = _ajustes.ArranqueDirecto == SesionActual.Modo;
        ActualizarArranqueTexto();
        // Hay secciones que solo existen en un modo (las de préstamos no van en
        // el punto de venta y viceversa): al cambiar de estancia se reevalúan.
        NotificarCambioDeEstancia();
    }

    private void ActualizarArranqueTexto() =>
        ArranqueDirectoTexto = _ajustes.ArranqueDirecto is { } modo
            ? $"Al abrir FAControl entra directo a {IdentidadModo.De(modo).Nombre}, sin la pantalla " +
              "de inicio. Al cerrar sesión sí vuelve a aparecer, para poder cambiar de estancia."
            : "Al abrir FAControl se muestra la pantalla de inicio con los modos disponibles.";

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
