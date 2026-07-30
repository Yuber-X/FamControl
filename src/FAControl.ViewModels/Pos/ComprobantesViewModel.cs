// Portado de POS500.ViewModels el 2026-07-30 al integrar el punto de venta a la
// suite. Usa el SesionActual, los permisos y el IDialogService de FAControl.
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAControl.Common;
using FAControl.Models.Pos;
using FAControl.Services.Pos;
using Serilog;

namespace FAControl.ViewModels.Pos;

/// <summary>Fila de la lista de comprobantes.</summary>
public record ComprobanteFila(FacturaResumen Factura)
{
    public long Id => Factura.Id;
    public string Numero => Factura.NumeroFactura;
    public string FechaTexto => FechaNegocio.AUtcLocal(Factura.FechaEmisionUtc).ToString("dd/MM/yyyy hh:mm tt");
    public string ClienteTexto => Factura.NombreCliente ?? "Consumidor final";
    public string Cajero => Factura.NombreCajero;
    public decimal Total => Factura.Total;
    public string MetodoTexto => Factura.MetodoPago switch
    {
        MetodoPagoFactura.Efectivo => "Efectivo",
        MetodoPagoFactura.Tarjeta => "Tarjeta",
        MetodoPagoFactura.Transferencia => "Transferencia",
        _ => "Mixto"
    };
    public EstadoFactura Estado => Factura.Estado;
    public string EstadoTexto => Factura.Estado == EstadoFactura.Anulada ? "Anulada" : "Emitida";
    public bool EstaAnulada => Factura.Estado == EstadoFactura.Anulada;
}

/// <summary>
/// Buscar comprobante: búsqueda por número/cliente y rango de fechas,
/// reimpresión del ticket original y anulación (con permiso).
/// El alcance (propias vs todas) lo impone FacturaService, no la UI.
/// </summary>
public partial class ComprobantesViewModel : ObservableObject, IPaginaAsincrona
{
    private readonly FacturaService _facturas;
    private readonly IDialogService _dialogos;

    /// <summary>La App abre el ticket para reimprimir.</summary>
    public event Action<VentaResultado>? ReimpresionSolicitada;

    public ComprobantesViewModel(FacturaService facturas, IDialogService dialogos)
    {
        _facturas = facturas;
        _dialogos = dialogos;

        var hoy = FechaNegocio.Hoy;
        _desde = hoy.AddDays(-7).ToDateTime(TimeOnly.MinValue);
        _hasta = hoy.ToDateTime(TimeOnly.MinValue);
    }

    public ObservableCollection<ComprobanteFila> Filas { get; } = [];

    [ObservableProperty] private string _textoBusqueda = string.Empty;
    [ObservableProperty] private DateTime? _desde;
    [ObservableProperty] private DateTime? _hasta;
    [ObservableProperty] private string _contadorTexto = string.Empty;
    [ObservableProperty] private bool _ocupado;

    public bool PuedeAnular => FacturaService.PuedeAnular;
    public string AlcanceTexto => FacturaService.PuedeVerTodos
        ? "Mostrando comprobantes de todos los cajeros"
        : "Mostrando solo tus comprobantes";

    public async Task RefrescarAsync()
    {
        OnPropertyChanged(nameof(PuedeAnular));
        OnPropertyChanged(nameof(AlcanceTexto));
        await BuscarAsync();
    }

    [RelayCommand]
    private async Task BuscarAsync()
    {
        try
        {
            Ocupado = true;
            var filtro = new FiltroComprobantes(
                string.IsNullOrWhiteSpace(TextoBusqueda) ? null : TextoBusqueda.Trim(),
                Desde is { } d ? DateOnly.FromDateTime(d) : null,
                Hasta is { } h ? DateOnly.FromDateTime(h) : null,
                UsuarioId: null);   // el service fuerza el alcance según permisos

            var resultados = await _facturas.BuscarAsync(filtro);

            Filas.Clear();
            foreach (var factura in resultados)
                Filas.Add(new ComprobanteFila(factura));

            ContadorTexto = Filas.Count == 0
                ? "No hay comprobantes con esos filtros"
                : $"{Filas.Count} comprobante(s)";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error buscando comprobantes");
            _dialogos.MostrarError("Comprobantes", $"No se pudo buscar.\n\n{ex.Message}");
        }
        finally
        {
            Ocupado = false;
        }
    }

    [RelayCommand]
    private async Task ReimprimirAsync(ComprobanteFila? fila)
    {
        if (fila is null)
            return;

        try
        {
            var factura = await _facturas.ObtenerCompletaAsync(fila.Id);
            if (factura is null)
            {
                _dialogos.MostrarError("Comprobantes", "La factura ya no existe.");
                return;
            }
            ReimpresionSolicitada?.Invoke(FacturaService.AVentaResultado(factura));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error reimprimiendo la factura {Id}", fila.Id);
            _dialogos.MostrarError("Reimprimir", ex.Message);
        }
    }

    [RelayCommand]
    private async Task AnularAsync(ComprobanteFila? fila)
    {
        if (fila is null)
            return;
        if (fila.EstaAnulada)
        {
            _dialogos.MostrarError("Anular", $"La factura {fila.Numero} ya está anulada.");
            return;
        }

        // Doble confirmación: es irreversible y devuelve stock al inventario
        if (!_dialogos.Confirmar("Anular factura",
                $"¿Anular la factura {fila.Numero} por {fila.Total:N2}?\n\n" +
                "Los productos vuelven al inventario. La factura NO se borra: " +
                "queda en el historial marcada como anulada."))
            return;

        var motivo = _dialogos.PedirTexto("Motivo de la anulación",
            "Escribe por qué se anula (queda en el historial):");
        if (string.IsNullOrWhiteSpace(motivo))
            return;

        try
        {
            Ocupado = true;
            await _facturas.AnularAsync(fila.Id, motivo);
            _dialogos.Informar("Factura anulada",
                $"La factura {fila.Numero} quedó anulada y el stock fue devuelto.");
            await BuscarAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error anulando la factura {Id}", fila.Id);
            _dialogos.MostrarError("Anular", ex.Message);
        }
        finally
        {
            Ocupado = false;
        }
    }
}
