using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAControl.Common;
using FAControl.Models;
using FAControl.Services;
using Serilog;

namespace FAControl.ViewModels;

/// <summary>
/// Pantalla del expediente de UN contrato de PrestControl (2026-08-01).
///
/// Pedido de Yuber: "coloca un btn en cada fila llamada 'Archivos', al darle
/// click deberiamos entrar a la pantalla 'expediente del cliente'... agregar en
/// esa misma pantalla un btn que lo lleve a 'detalles del cliente' para cuando
/// el usuario desee ir de manera rapida a ver mas detalles."
///
/// Es una PAGINA, no una ventana: se navega hacia ella y se vuelve con el
/// botón de atrás, como el resto de la aplicación. La ventana suelta que había
/// antes fue justamente lo que se pidió sacar.
///
/// El expediente en sí es el mismo control compartido que usan DealControl y
/// Alquileres; aquí solo se le da marco, contexto del contrato y una salida
/// rápida a la ficha del cliente.
/// </summary>
public partial class ExpedienteContratoViewModel : ObservableObject
{
    private readonly PrestamoService _prestamos;
    private readonly ClienteService _clientes;
    private readonly IDialogService _dialogos;
    private long _clienteId;

    /// <summary>Vuelve al almacén de contratos.</summary>
    public event Action? VolverSolicitado;

    /// <summary>Salto rápido a la ficha del cliente (el botón que se pidió).</summary>
    public event Action<long>? FichaClienteSolicitada;

    public ExpedienteContratoViewModel(PrestamoService prestamos, ClienteService clientes,
        IDialogService dialogos, ExpedienteViewModel expediente)
    {
        _prestamos = prestamos;
        _clientes = clientes;
        _dialogos = dialogos;
        Expediente = expediente;
    }

    /// <summary>El expediente de este contrato. Mismo control que el resto de la suite.</summary>
    public ExpedienteViewModel Expediente { get; }

    [ObservableProperty] private string _codigo = string.Empty;
    [ObservableProperty] private string _clienteNombre = string.Empty;
    [ObservableProperty] private string _clienteCedula = "—";
    [ObservableProperty] private string _clienteTelefono = "—";
    [ObservableProperty] private string _resumenTexto = string.Empty;

    public async Task CargarAsync(long prestamoId)
    {
        try
        {
            var prestamo = await _prestamos.ObtenerPorIdAsync(prestamoId)
                ?? throw new InvalidOperationException($"No existe el préstamo con id {prestamoId}.");
            _clienteId = prestamo.ClienteId;
            var cliente = await _clientes.ObtenerPorIdAsync(prestamo.ClienteId);

            Codigo = prestamo.Codigo;
            ClienteNombre = cliente?.NombreCompleto ?? "(cliente eliminado)";
            ClienteCedula = string.IsNullOrWhiteSpace(cliente?.Cedula) ? "—" : cliente.Cedula;
            ClienteTelefono = string.IsNullOrWhiteSpace(cliente?.Telefono) ? "—" : cliente.Telefono!;
            ResumenTexto =
                $"{prestamo.MontoCapital.ToString("N2", Textos.CulturaRd)} DOP · " +
                $"{prestamo.PlazoCuotas} cuota(s) {Textos.De(prestamo.Modalidad).ToLowerInvariant()} · " +
                $"{Textos.De(prestamo.Estado)}";

            await Expediente.CargarAsync(DuenoExpediente.DePrestamo(prestamoId));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cargando el expediente del préstamo {Id}", prestamoId);
            _dialogos.MostrarError("Expediente", $"No se pudo abrir el expediente.\n\n{ex.Message}");
        }
    }

    [RelayCommand]
    private void Volver() => VolverSolicitado?.Invoke();

    /// <summary>
    /// Lleva a la ficha del cliente sin pasar por la lista de Clientes. Es el
    /// atajo que se pidió: desde los papeles, ir a ver de quién son.
    /// </summary>
    [RelayCommand]
    private void VerCliente()
    {
        if (_clienteId > 0)
            FichaClienteSolicitada?.Invoke(_clienteId);
    }
}
