using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAControl.Common;
using FAControl.Services;
using Serilog;

namespace FAControl.ViewModels;

/// <summary>
/// Los cuatro códigos del launcher (pedido del cliente 2026-07-27):
///  1. prueba de 2 semanas;
///  2. habilitar el producto;
///  3. recuperar el acceso sin perder datos;
///  4. restablecer todo desde el inicio.
///
/// El 1 y el 2 se aplican al toque. El 3 y el 4 abren su propio panel porque
/// necesitan más datos y, en el caso del 4, doble confirmación: borra todo.
/// </summary>
public partial class CodigosViewModel : ObservableObject
{
    private readonly LicenciaService _licencias;
    private readonly RecuperacionService _recuperacion;
    private readonly IDialogService _dialogos;

    /// <summary>La licencia cambió (el launcher refresca su leyenda y su candado).</summary>
    public event Action? LicenciaCambiada;
    /// <summary>Terminó bien y no queda nada que hacer en la ventana.</summary>
    public event Action? CerrarSolicitado;

    public CodigosViewModel(LicenciaService licencias, RecuperacionService recuperacion,
        IDialogService dialogos)
    {
        _licencias = licencias;
        _recuperacion = recuperacion;
        _dialogos = dialogos;
        _estadoTexto = licencias.EstadoTexto;
    }

    /// <summary>
    /// True si la app se puede usar hoy (prueba viva o producto activado).
    /// El launcher lo consulta acá y no en LicenciaService: las Views no bajan
    /// a la capa de servicios (regla de dependencias del proyecto).
    /// </summary>
    public bool PermiteUsar => _licencias.PermiteUsar;

    /// <summary>Relee el estado de la licencia (el launcher lo llama al volver del diálogo).</summary>
    public void RefrescarEstado()
    {
        EstadoTexto = _licencias.EstadoTexto;
        OnPropertyChanged(nameof(PermiteUsar));
    }

    [ObservableProperty] private string _codigo = string.Empty;
    [ObservableProperty] private string _estadoTexto;
    [ObservableProperty] private string _mensaje = string.Empty;
    [ObservableProperty] private bool _esError;
    [ObservableProperty] private bool _ocupado;

    // Panel del código 3
    [ObservableProperty] private bool _modoRecuperacion;
    [ObservableProperty] private string _usuarioRecuperar = string.Empty;
    [ObservableProperty] private bool _crearComoProgramador;

    // Panel del código 4
    [ObservableProperty] private bool _modoRestablecer;
    [ObservableProperty] private string _carpetaRespaldo = string.Empty;

    /// <summary>La contraseña no va en una property observable: la toma el PasswordBox.</summary>
    public string PasswordNueva { get; set; } = string.Empty;

    /// <summary>La View limpia el PasswordBox cuando esto se dispara.</summary>
    public event Action? PasswordDebeLimpiarse;

    [RelayCommand]
    private void Validar()
    {
        Mensaje = string.Empty;
        EsError = false;

        var resultado = _licencias.Aplicar(Codigo);
        switch (resultado.Accion)
        {
            case AccionCodigo.RecuperarAcceso:
                ModoRecuperacion = true;
                ModoRestablecer = false;
                Mensaje = "Código válido. Indicá qué cuenta querés recuperar y su contraseña nueva. " +
                          "Los datos del negocio NO se tocan.";
                break;

            case AccionCodigo.RestablecerTodo:
                ModoRestablecer = true;
                ModoRecuperacion = false;
                EsError = true;   // se pinta en rojo: es la operación destructiva
                Mensaje = "Código válido. Esto BORRA todos los datos y deja el sistema como recién " +
                          "instalado. Antes se guarda un respaldo en la carpeta que elijas.";
                break;

            default:
                Mensaje = resultado.Mensaje;
                EsError = !resultado.Aceptado;
                EstadoTexto = _licencias.EstadoTexto;
                if (resultado.Aceptado)
                {
                    Codigo = string.Empty;
                    LicenciaCambiada?.Invoke();
                }
                break;
        }
    }

    /// <summary>Código 3: devolver el acceso sin perder un solo dato.</summary>
    [RelayCommand]
    private async Task RecuperarAsync()
    {
        try
        {
            Ocupado = true;
            EsError = false;
            var mensaje = await _recuperacion.RestablecerAccesoAsync(
                UsuarioRecuperar, PasswordNueva, CrearComoProgramador);

            PasswordNueva = string.Empty;
            PasswordDebeLimpiarse?.Invoke();
            Codigo = string.Empty;
            ModoRecuperacion = false;
            _dialogos.Informar("Acceso recuperado", mensaje);
            CerrarSolicitado?.Invoke();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            EsError = true;
            Mensaje = ex.Message;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error recuperando el acceso desde el launcher");
            EsError = true;
            Mensaje = $"No se pudo recuperar el acceso.\n\n{ex.Message}";
        }
        finally
        {
            Ocupado = false;
        }
    }

    /// <summary>Código 4: borrar todo. Doble confirmación y respaldo obligatorio.</summary>
    [RelayCommand]
    private async Task RestablecerAsync()
    {
        if (string.IsNullOrWhiteSpace(CarpetaRespaldo))
        {
            EsError = true;
            Mensaje = "Elegí primero la carpeta donde guardar el respaldo.";
            return;
        }

        // Primera confirmación: qué se pierde
        if (!_dialogos.Confirmar("Restablecer todo",
            "Se van a BORRAR todos los clientes, préstamos, vehículos, ventas, cobros y usuarios.\n\n" +
            $"Antes se guarda un respaldo en:\n{CarpetaRespaldo}\n\n¿Seguir?"))
            return;

        // Segunda: que no sea un clic apurado
        if (!_dialogos.Confirmar("Confirmación final",
            "Última oportunidad. Después de esto el sistema queda como recién instalado " +
            "y solo se puede volver atrás restaurando el respaldo.\n\n¿Restablecer todo ahora?"))
            return;

        try
        {
            Ocupado = true;
            EsError = false;
            var respaldo = await _recuperacion.RestablecerTodoAsync(CarpetaRespaldo);

            ModoRestablecer = false;
            Codigo = string.Empty;
            _dialogos.Informar("Sistema restablecido",
                $"Listo. El respaldo quedó en:\n{respaldo}\n\n" +
                "Al volver a entrar, FAControl va a pedir crear la cuenta inicial.");
            CerrarSolicitado?.Invoke();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            EsError = true;
            Mensaje = ex.Message;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error restableciendo el sistema desde el launcher");
            EsError = true;
            Mensaje = $"No se pudo restablecer.\n\nNo se borró nada.\n{ex.Message}";
        }
        finally
        {
            Ocupado = false;
        }
    }

    [RelayCommand]
    private void Cancelar()
    {
        ModoRecuperacion = false;
        ModoRestablecer = false;
        Mensaje = string.Empty;
        EsError = false;
        Codigo = string.Empty;
        PasswordNueva = string.Empty;
        PasswordDebeLimpiarse?.Invoke();
    }
}
