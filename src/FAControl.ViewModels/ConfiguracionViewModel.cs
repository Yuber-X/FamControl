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

    /// <summary>El shell escala la UI cuando cambia el tamaño de texto.</summary>
    public event Action<double>? EscalaCambiada;

    /// <summary>La App intercambia la paleta cuando se activa el modo noche.</summary>
    public event Action<bool>? TemaCambiado;

    public ConfiguracionViewModel(AuthService auth, RespaldoService respaldo,
        ExportacionService exportacion, AjustesLocales ajustes, IDialogService dialogos,
        IAvisoVencidos avisoVencidos)
    {
        _auth = auth;
        _respaldo = respaldo;
        _exportacion = exportacion;
        _ajustes = ajustes;
        _dialogos = dialogos;
        _avisoVencidos = avisoVencidos;

        Tamanos =
        [
            new Opcion<TamanoTexto>(TamanoTexto.Pequeno, "Pequeño"),
            new Opcion<TamanoTexto>(TamanoTexto.Mediano, "Mediano"),
            new Opcion<TamanoTexto>(TamanoTexto.Grande, "Grande")
        ];
        _tamanoSeleccionado = Tamanos.First(t => t.Valor == ajustes.TamanoTexto);
        // Campo y no propiedad: asignar la propiedad dispararía el evento y
        // volvería a guardar el ajuste apenas se abre Configuración.
        _temaOscuro = ajustes.TemaOscuro;
        _exportActivo = ajustes.ExportAutomaticoActivo;
        _exportCadaDiasTexto = ajustes.ExportAutomaticoCadaDias.ToString();
        _exportCarpeta = ajustes.ExportAutomaticoCarpeta ?? string.Empty;
        _respaldoAutoActivo = ajustes.RespaldoAutomaticoActivo;
        _respaldoCadaTexto = ajustes.RespaldoAutomaticoCada.ToString();
        _respaldoUnidad = Unidades.FirstOrDefault(u => u.Valor == ajustes.RespaldoAutomaticoUnidad) ?? Unidades[0];
        _respaldoCarpeta = ajustes.RespaldoAutomaticoCarpeta ?? string.Empty;
        _avisoVencidosActivo = ajustes.AvisoVencidosActivo;
        ActualizarUltimaExportacion();
        ActualizarUltimoRespaldo();
        ActualizarSilenciados();
    }

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
    /// Modo noche: se aplica en el momento y se recuerda por PC
    /// (pedido del cliente 2026-07-16).
    /// </summary>
    partial void OnTemaOscuroChanged(bool value)
    {
        _ajustes.TemaOscuro = value;
        _ajustes.Guardar();
        TemaCambiado?.Invoke(value);
    }

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
