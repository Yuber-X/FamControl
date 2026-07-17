using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAControl.Common;
using FAControl.Models;
using FAControl.Services;
using Serilog;

namespace FAControl.ViewModels;

/// <summary>Detalle de un préstamo: datos del contrato + tabla de cuotas con semáforo.</summary>
public partial class PrestamoDetalleViewModel : ObservableObject
{
    private readonly PrestamoService _prestamos;
    private readonly ClienteService _clientes;
    private readonly IDialogService _dialogos;
    private readonly AjustesLocales _ajustes;
    private long _prestamoId;

    public event Action<long>? CobrarSolicitado;
    public event Action? VolverSolicitado;
    /// <summary>La App abre la vista previa imprimible del préstamo.</summary>
    public event Action<PrestamoImpreso>? ImpresionSolicitada;
    /// <summary>La App abre la vista previa de la intimación de pago.</summary>
    public event Action<IntimacionImpresa>? IntimacionSolicitada;

    public PrestamoDetalleViewModel(PrestamoService prestamos, ClienteService clientes,
        IDialogService dialogos, AjustesLocales ajustes)
    {
        _prestamos = prestamos;
        _clientes = clientes;
        _dialogos = dialogos;
        _ajustes = ajustes;
    }

    public ObservableCollection<CuotaFila> Cuotas { get; } = [];

    [ObservableProperty] private string _codigo = string.Empty;
    [ObservableProperty] private string _clienteNombre = string.Empty;
    [ObservableProperty] private string _clienteCedula = "—";
    [ObservableProperty] private string _estadoTexto = string.Empty;
    [ObservableProperty] private EstadoPrestamo _estado;
    [ObservableProperty] private bool _esActivo;
    [ObservableProperty] private decimal _montoCapital;
    [ObservableProperty] private string _tasaTexto = string.Empty;
    [ObservableProperty] private string _modalidadTexto = string.Empty;
    [ObservableProperty] private string _metodoTexto = string.Empty;
    [ObservableProperty] private string _fechaInicioTexto = string.Empty;
    [ObservableProperty] private string _garantiaTexto = string.Empty;
    [ObservableProperty] private string _notasTexto = string.Empty;
    [ObservableProperty] private decimal _totalAPagar;
    [ObservableProperty] private decimal _totalPagado;
    [ObservableProperty] private decimal _saldoPendiente;
    [ObservableProperty] private string _progresoTexto = string.Empty;

    public async Task CargarAsync(long prestamoId)
    {
        try
        {
            _prestamoId = prestamoId;
            var prestamo = await _prestamos.ObtenerPorIdAsync(prestamoId)
                ?? throw new InvalidOperationException($"No existe el préstamo con id {prestamoId}.");
            var cliente = await _clientes.ObtenerPorIdAsync(prestamo.ClienteId);
            var cuotas = await _prestamos.ObtenerCuotasAsync(prestamoId);

            Codigo = prestamo.Codigo;
            ClienteNombre = cliente?.NombreCompleto ?? "(cliente eliminado)";
            ClienteCedula = string.IsNullOrWhiteSpace(cliente?.Cedula) ? "—" : cliente.Cedula;
            Estado = prestamo.Estado;
            EstadoTexto = Textos.De(prestamo.Estado);
            EsActivo = prestamo.Estado == EstadoPrestamo.Activo;
            MontoCapital = prestamo.MontoCapital;
            TasaTexto = $"{prestamo.TasaInteres:0.##}% mensual";
            ModalidadTexto = Textos.De(prestamo.Modalidad);
            MetodoTexto = Textos.De(prestamo.MetodoAmortizacion);
            FechaInicioTexto = prestamo.FechaInicio.ToString(Textos.FormatoFecha, Textos.CulturaRd);
            GarantiaTexto = string.IsNullOrWhiteSpace(prestamo.Garantia) ? "—" : prestamo.Garantia;
            NotasTexto = string.IsNullOrWhiteSpace(prestamo.Notas) ? "—" : prestamo.Notas;

            var hoy = FechaNegocio.Hoy;
            Cuotas.Clear();
            foreach (var cuota in cuotas)
                Cuotas.Add(new CuotaFila(cuota, CuotaEstadoCalculator.Calcular(cuota, hoy)));

            TotalAPagar = cuotas.Sum(c => c.MontoTotal);
            TotalPagado = cuotas.Sum(c => c.MontoPagado);
            SaldoPendiente = TotalAPagar - TotalPagado;
            ProgresoTexto = $"{cuotas.Count(c => c.Estado == EstadoCuota.Pagada)}/{cuotas.Count} cuotas pagadas";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cargando el detalle del préstamo {Id}", prestamoId);
            _dialogos.MostrarError("Detalle de préstamo", $"No se pudo cargar el préstamo.\n\n{ex.Message}");
        }
    }

    [RelayCommand]
    private void Cobrar() => CobrarSolicitado?.Invoke(_prestamoId);

    /// <summary>
    /// Imprime el estado del préstamo con su tabla de amortización
    /// (pedido del cliente 2026-07-16). Siempre pasa por vista previa.
    /// </summary>
    [RelayCommand]
    private void Imprimir()
    {
        if (Cuotas.Count == 0)
        {
            _dialogos.Informar("Imprimir préstamo", "Este préstamo todavía no tiene cuotas que imprimir.");
            return;
        }

        ImpresionSolicitada?.Invoke(new PrestamoImpreso(
            Codigo, ClienteNombre, ClienteCedula, MontoCapital, TasaTexto, ModalidadTexto,
            MetodoTexto, FechaInicioTexto, GarantiaTexto, TotalAPagar, TotalPagado,
            SaldoPendiente, EstadoTexto, ProgresoTexto, SesionActual.Nombre,
            [.. Cuotas.Select(c => new CuotaImpresa(
                c.Numero, c.FechaTexto, c.Capital, c.Interes,
                c.MontoTotal, c.SaldoDespues, c.SemaforoTexto))]));
    }

    /// <summary>
    /// Genera la intimación de pago (requerimiento formal previo a lo judicial)
    /// para las cuotas vencidas. Ver docs/INTIMACION-Y-MANDAMIENTO.md.
    /// </summary>
    [RelayCommand]
    private void ImprimirIntimacion()
    {
        var vencidas = Cuotas.Where(c => c.EstaVencida).ToList();
        if (vencidas.Count == 0)
        {
            _dialogos.Informar("Intimación de pago",
                "Este préstamo no tiene cuotas vencidas, así que no procede una intimación de pago.");
            return;
        }

        IntimacionSolicitada?.Invoke(new IntimacionImpresa(
            _ajustes.NombreNegocio, _ajustes.Prestamista, _ajustes.CiudadNegocio,
            _ajustes.TelefonoNegocio, _ajustes.RncNegocio,
            ClienteNombre, ClienteCedula, Codigo, MontoCapital, SaldoPendiente,
            [.. vencidas.Select(c => new IntimacionCuota(c.Numero, c.FechaTexto, c.SaldoPendiente))],
            _ajustes.PlazoIntimacionDias));
    }

    [RelayCommand]
    private void Volver() => VolverSolicitado?.Invoke();

    [RelayCommand]
    private async Task CancelarPrestamoAsync()
    {
        if (!_dialogos.Confirmar("Cancelar préstamo",
            $"¿Cancelar el préstamo {Codigo} de {ClienteNombre}?\n\n" +
            "Las cuotas sin pagar quedarán canceladas. Esta acción no se puede deshacer."))
            return;

        try
        {
            await _prestamos.CancelarAsync(_prestamoId, "Cancelado desde el detalle del préstamo");
            _dialogos.Informar("Préstamo cancelado", $"El préstamo {Codigo} fue cancelado.");
            await CargarAsync(_prestamoId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cancelando el préstamo {Id}", _prestamoId);
            _dialogos.MostrarError("Cancelar préstamo", $"No se pudo cancelar el préstamo.\n\n{ex.Message}");
        }
    }
}
