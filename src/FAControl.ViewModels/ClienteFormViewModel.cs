using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAControl.Common;
using FAControl.Models;
using FAControl.Services;
using Serilog;

namespace FAControl.ViewModels;

/// <summary>
/// Formulario de cliente (nuevo y edición comparten la misma pantalla).
/// Los errores de validación se muestran inline; los de infraestructura, en diálogo.
/// </summary>
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
    [ObservableProperty] private string _apellido = string.Empty;
    [ObservableProperty] private string _telefono = string.Empty;
    [ObservableProperty] private string _direccion = string.Empty;
    [ObservableProperty] private string _email = string.Empty;
    [ObservableProperty] private string _notas = string.Empty;
    [ObservableProperty] private string _mensajeError = string.Empty;
    [ObservableProperty] private bool _ocupado;

    public void PrepararNuevo()
    {
        _clienteId = null;
        Titulo = "Nuevo cliente";
        Cedula = Nombre = Apellido = Telefono = Direccion = Email = Notas = string.Empty;
        MensajeError = string.Empty;
    }

    public async Task PrepararEdicionAsync(long clienteId)
    {
        var cliente = await _servicio.ObtenerPorIdAsync(clienteId)
            ?? throw new InvalidOperationException("El cliente no existe o fue eliminado.");

        _clienteId = clienteId;
        Titulo = $"Editar cliente — {cliente.NombreCompleto}";
        Cedula = cliente.Cedula;
        Nombre = cliente.Nombre;
        Apellido = cliente.Apellido;
        Telefono = cliente.Telefono ?? string.Empty;
        Direccion = cliente.Direccion ?? string.Empty;
        Email = cliente.Email ?? string.Empty;
        Notas = cliente.Notas ?? string.Empty;
        MensajeError = string.Empty;
    }

    [RelayCommand]
    private async Task GuardarAsync()
    {
        var datos = new ClienteDatos(Cedula, Nombre, Apellido, Telefono, Direccion, Email, Notas);
        try
        {
            Ocupado = true;
            MensajeError = string.Empty;

            long id;
            if (_clienteId is null)
            {
                id = await _servicio.CrearAsync(datos);
                _dialogos.Informar("Cliente creado", $"{datos.Nombre} {datos.Apellido} se registró correctamente.");
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
            // Validación de negocio: inline, sin diálogo
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
