// Portado de POS500.ViewModels el 2026-07-30 al integrar el punto de venta a la
// suite. Usa el SesionActual, los permisos y el IDialogService de FAControl.
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAControl.Common;
using FAControl.Models.Pos;
using FAControl.Services.Pos;
using Serilog;

namespace FAControl.ViewModels.Pos;

/// <summary>Formulario de cliente (nuevo y edición). Errores de validación inline.</summary>
public partial class ClienteFormViewModel : ObservableObject
{
    private readonly ClienteService _servicio;
    private readonly IDialogService _dialogos;
    private long? _clienteId; // null = nuevo

    public event Action<long>? Guardado;
    public event Action? Cancelado;

    public ClienteFormViewModel(ClienteService servicio, IDialogService dialogos)
    {
        _servicio = servicio;
        _dialogos = dialogos;
    }

    [ObservableProperty] private string _titulo = "Nuevo cliente";
    [ObservableProperty] private string _cedula = string.Empty;
    [ObservableProperty] private string _nombre = string.Empty;
    [ObservableProperty] private string _telefono = string.Empty;
    [ObservableProperty] private string _direccion = string.Empty;
    [ObservableProperty] private string _notas = string.Empty;
    [ObservableProperty] private string _mensajeError = string.Empty;
    [ObservableProperty] private bool _ocupado;

    public void PrepararNuevo()
    {
        _clienteId = null;
        Titulo = "Nuevo cliente";
        Cedula = Nombre = Telefono = Direccion = Notas = string.Empty;
        MensajeError = string.Empty;
    }

    public async Task PrepararEdicionAsync(long clienteId)
    {
        var cliente = await _servicio.ObtenerPorIdAsync(clienteId)
            ?? throw new InvalidOperationException("El cliente no existe o fue eliminado.");

        _clienteId = clienteId;
        Titulo = $"Editar cliente — {cliente.Nombre}";
        Cedula = cliente.Cedula ?? string.Empty;
        Nombre = cliente.Nombre;
        Telefono = cliente.Telefono ?? string.Empty;
        Direccion = cliente.Direccion ?? string.Empty;
        Notas = cliente.Notas ?? string.Empty;
        MensajeError = string.Empty;
    }

    [RelayCommand]
    private async Task GuardarAsync()
    {
        var datos = new ClienteDatos(Cedula, Nombre, Telefono, Direccion, Notas);
        try
        {
            Ocupado = true;
            MensajeError = string.Empty;

            long id;
            if (_clienteId is null)
            {
                id = await _servicio.CrearAsync(datos);
                _dialogos.Informar("Cliente creado", $"{datos.Nombre} se registró correctamente.");
            }
            else
            {
                id = _clienteId.Value;
                await _servicio.ActualizarAsync(id, datos);
            }

            Guardado?.Invoke(id);
        }
        catch (ArgumentException ex)
        {
            MensajeError = ex.Message;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error guardando el cliente");
            _dialogos.MostrarError("Guardar cliente", $"No se pudo guardar el cliente.\n\n{ex.Message}");
        }
        finally
        {
            Ocupado = false;
        }
    }

    [RelayCommand]
    private void Cancelar() => Cancelado?.Invoke();
}
