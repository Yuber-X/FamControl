using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAControl.Common;
using FAControl.Services;
using Serilog;

namespace FAControl.ViewModels;

/// <summary>
/// Los siete códigos del launcher (pedido del cliente 2026-07-29):
///  1. prueba de 2 semanas (toda la suite abierta);
///  2. activación total;
///  3. activación de PrestControl;
///  4. activación de DealControl;
///  5. activación de POS-500;
///  6. hacer respaldo y limpiar todo;
///  7. eliminar todo.
///
/// Del 1 al 5 se aplican al toque. El 6 y el 7 abren su propio panel: el 6
/// necesita la carpeta del respaldo, y el 7 —que no respalda nada— exige
/// escribir ELIMINAR además de las confirmaciones.
/// </summary>
public partial class CodigosViewModel : ObservableObject
{
    /// <summary>Lo que hay que escribir para habilitar el código 7.</summary>
    public const string PalabraDeConfirmacion = "ELIMINAR";

    private readonly LicenciaService _licencias;
    private readonly RecuperacionService _recuperacion;
    private readonly IDialogService _dialogos;

    /// <summary>La licencia cambió (el launcher refresca su leyenda y sus candados).</summary>
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
    /// True si la app se puede usar hoy (prueba viva, suite activada o algún
    /// producto comprado). El launcher lo consulta acá y no en LicenciaService:
    /// las Views no bajan a la capa de servicios (regla de dependencias).
    /// </summary>
    public bool PermiteUsar => _licencias.PermiteUsar;

    /// <summary>True si ESTE modo se puede abrir hoy (activación por modo, 2026-07-29).</summary>
    public bool PermiteModo(ModoApp modo) => _licencias.PermiteModo(modo);

    /// <summary>True si el cliente ya compró POS-500 (el launcher lo muestra distinto).</summary>
    public bool Pos500Comprado => _licencias.Pos500Comprado;

    /// <summary>Relee el estado de la licencia (el launcher lo llama al volver del diálogo).</summary>
    public void RefrescarEstado()
    {
        EstadoTexto = _licencias.EstadoTexto;
        OnPropertyChanged(nameof(PermiteUsar));
        OnPropertyChanged(nameof(Pos500Comprado));
    }

    [ObservableProperty] private string _codigo = string.Empty;
    [ObservableProperty] private string _estadoTexto;
    [ObservableProperty] private string _mensaje = string.Empty;
    [ObservableProperty] private bool _esError;
    [ObservableProperty] private bool _ocupado;

    // Panel del código 6 (respaldar y limpiar)
    [ObservableProperty] private bool _modoLimpiar;
    [ObservableProperty] private string _carpetaRespaldo = string.Empty;

    // Panel del código 7 (eliminar todo, sin respaldo)
    [ObservableProperty] private bool _modoEliminar;
    [ObservableProperty] private string _confirmacionEscrita = string.Empty;

    /// <summary>True cuando el usuario escribió la palabra exacta de confirmación.</summary>
    public bool ConfirmacionCorrecta =>
        ConfirmacionEscrita.Trim().Equals(PalabraDeConfirmacion, StringComparison.OrdinalIgnoreCase);

    partial void OnConfirmacionEscritaChanged(string value) =>
        OnPropertyChanged(nameof(ConfirmacionCorrecta));

    [RelayCommand]
    private void Validar()
    {
        Mensaje = string.Empty;
        EsError = false;

        var resultado = _licencias.Aplicar(Codigo);
        switch (resultado.Accion)
        {
            case AccionCodigo.RespaldarYLimpiar:
                ModoLimpiar = true;
                ModoEliminar = false;
                EsError = true;   // se pinta en rojo: es una operación destructiva
                Mensaje = "Código válido. Esto BORRA todos los datos y deja el sistema como recién " +
                          "instalado. Antes se guarda un respaldo en la carpeta que elijas.";
                break;

            case AccionCodigo.EliminarTodo:
                ModoEliminar = true;
                ModoLimpiar = false;
                EsError = true;
                Mensaje = "Código válido. Esto ELIMINA la base, los expedientes, los ajustes y la " +
                          "licencia. NO se guarda ningún respaldo. Es para retirar FAControl de esta PC.";
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

    /// <summary>Código 6: respaldo obligatorio y después la base queda vacía.</summary>
    [RelayCommand]
    private async Task LimpiarTodoAsync()
    {
        if (string.IsNullOrWhiteSpace(CarpetaRespaldo))
        {
            EsError = true;
            Mensaje = "Elegí primero la carpeta donde guardar el respaldo.";
            return;
        }

        // Primera confirmación: qué se pierde
        if (!_dialogos.Confirmar("Limpiar todo",
            "Se van a BORRAR todos los clientes, préstamos, vehículos, ventas, cobros y usuarios.\n\n" +
            $"Antes se guarda un respaldo en:\n{CarpetaRespaldo}\n\n¿Seguir?"))
            return;

        // Segunda: que no sea un clic apurado
        if (!_dialogos.Confirmar("Confirmación final",
            "Última oportunidad. Después de esto el sistema queda como recién instalado " +
            "y solo se puede volver atrás restaurando el respaldo.\n\n¿Limpiar todo ahora?"))
            return;

        try
        {
            Ocupado = true;
            EsError = false;
            var respaldo = await _recuperacion.RespaldarYLimpiarAsync(CarpetaRespaldo);

            ModoLimpiar = false;
            Codigo = string.Empty;
            _dialogos.Informar("Sistema limpio",
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
            Log.Error(ex, "Error limpiando el sistema desde el launcher");
            EsError = true;
            Mensaje = $"No se pudo limpiar.\n\nNo se borró nada.\n{ex.Message}";
        }
        finally
        {
            Ocupado = false;
        }
    }

    /// <summary>Código 7: eliminar todo sin respaldo. Palabra escrita + dos confirmaciones.</summary>
    [RelayCommand]
    private async Task EliminarTodoAsync()
    {
        if (!ConfirmacionCorrecta)
        {
            EsError = true;
            Mensaje = $"Escribí {PalabraDeConfirmacion} en mayúsculas para habilitar la operación.";
            return;
        }

        if (!_dialogos.Confirmar("Eliminar todo",
            "Se van a eliminar la base de datos, los expedientes escaneados, los ajustes de esta " +
            "PC y la licencia.\n\nNO se guarda ningún respaldo: esto NO se puede deshacer.\n\n¿Seguir?"))
            return;

        if (!_dialogos.Confirmar("Confirmación final",
            "Última oportunidad. Si lo que querés es empezar de cero conservando un respaldo, " +
            "cancelá y usá el código de \"respaldar y limpiar\".\n\n¿Eliminar todo ahora?"))
            return;

        try
        {
            Ocupado = true;
            EsError = false;
            var resumen = await _recuperacion.EliminarTodoAsync();

            ModoEliminar = false;
            Codigo = string.Empty;
            ConfirmacionEscrita = string.Empty;
            _dialogos.Informar("Instalación retirada",
                $"{resumen}.\n\nFAControl se va a cerrar. Al volver a abrirlo se comporta como " +
                "una instalación nueva.");
            CerrarSolicitado?.Invoke();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            EsError = true;
            Mensaje = ex.Message;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error eliminando la instalación desde el launcher");
            EsError = true;
            Mensaje = $"No se pudo eliminar todo.\n\n{ex.Message}";
        }
        finally
        {
            Ocupado = false;
        }
    }

    [RelayCommand]
    private void Cancelar()
    {
        ModoLimpiar = false;
        ModoEliminar = false;
        Mensaje = string.Empty;
        EsError = false;
        Codigo = string.Empty;
        ConfirmacionEscrita = string.Empty;
    }
}
