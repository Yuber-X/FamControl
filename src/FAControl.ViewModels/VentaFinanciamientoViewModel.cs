using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAControl.Common;
using FAControl.Models;
using FAControl.Services;
using Serilog;

namespace FAControl.ViewModels;

/// <summary>Fila del calendario de plazos en pantalla.</summary>
public record PlazoFila(VentaPlazo Plazo, DateOnly Hoy)
{
    public long Id => Plazo.Id;
    public int Numero => Plazo.Numero;
    public string FechaTexto => Plazo.FechaVencimiento.ToString(Textos.FormatoFecha, Textos.CulturaRd);
    public decimal Monto => Plazo.Monto;
    public decimal MontoPagado => Plazo.MontoPagado;
    public decimal SaldoPendiente => Plazo.SaldoPendiente;
    public bool EstaPagado => Plazo.Estado == EstadoPlazo.Pagado;
    public bool EstaAtrasado => Plazo.EstaAtrasado(Hoy);
    public string EstadoTexto => Plazo.Estado switch
    {
        EstadoPlazo.Pagado => "Pagado",
        EstadoPlazo.Cancelado => "Cancelado",
        _ => EstaAtrasado ? "Atrasado" : "Pendiente"
    };
}

/// <summary>Fila del historial de abonos.</summary>
public record AbonoFila(VentaPlazoPago Pago)
{
    public string NumeroRecibo => Pago.NumeroRecibo;
    public string FechaTexto =>
        FechaNegocio.AUtcLocal(Pago.FechaPagoUtc).ToString(Textos.FormatoFecha, Textos.CulturaRd);
    public decimal Monto => Pago.Monto;
    public string MetodoTexto => Textos.De(Pago.MetodoPago);
    public string NotasTexto => Pago.Notas ?? "—";
}

/// <summary>
/// Financiamiento de una venta del dealer (016 — pedido 2026-07-25):
/// "Total por pagar > lo pendiente > cantidad de plazos > lo pagado",
/// cobro de plazos y emisión de la carta de compromiso / recibo de separación.
/// </summary>
public partial class VentaFinanciamientoViewModel : ObservableObject
{
    private readonly VentaPlazoService _plazos;
    private readonly VentaVehiculoService _ventas;
    private readonly AjustesLocales _ajustes;
    private readonly IDialogService _dialogos;
    private long _ventaId;
    private EstadoFinanciamiento? _estado;
    private FacturaVentaDatos? _datosVenta;

    public event Action? VolverSolicitado;
    /// <summary>La View abre el visor del documento (carta o recibo de separación).</summary>
    public event Action<CartaCompromisoImpresa>? CartaSolicitada;
    public event Action<ReciboSeparacionImpreso>? SeparacionSolicitada;

    public VentaFinanciamientoViewModel(VentaPlazoService plazos, VentaVehiculoService ventas,
        AjustesLocales ajustes, IDialogService dialogos)
    {
        _plazos = plazos;
        _ventas = ventas;
        _ajustes = ajustes;
        _dialogos = dialogos;

        Metodos =
        [
            new Opcion<MetodoPago>(MetodoPago.Efectivo, "Efectivo"),
            new Opcion<MetodoPago>(MetodoPago.Transferencia, "Transferencia"),
            new Opcion<MetodoPago>(MetodoPago.Cheque, "Cheque"),
            new Opcion<MetodoPago>(MetodoPago.Otro, "Otro")
        ];
        _metodoSeleccionado = Metodos[0];
    }

    public ObservableCollection<PlazoFila> Plazos { get; } = [];
    public ObservableCollection<AbonoFila> Abonos { get; } = [];
    public IReadOnlyList<Opcion<MetodoPago>> Metodos { get; }

    [ObservableProperty] private string _codigo = string.Empty;
    [ObservableProperty] private string _tipoTexto = string.Empty;
    [ObservableProperty] private string _clienteNombre = string.Empty;
    [ObservableProperty] private string _vehiculoDescripcion = string.Empty;

    // Los cuatro números que pidió el cliente, en su orden
    [ObservableProperty] private decimal _totalAPagar;
    [ObservableProperty] private decimal _pendiente;
    [ObservableProperty] private string _plazosTexto = string.Empty;
    [ObservableProperty] private decimal _pagado;

    [ObservableProperty] private decimal _precio;
    [ObservableProperty] private decimal _inicial;
    [ObservableProperty] private string _avisoTexto = string.Empty;
    [ObservableProperty] private bool _hayAviso;
    [ObservableProperty] private bool _esPlazos;
    [ObservableProperty] private bool _esSeparacion;
    [ObservableProperty] private bool _hayAbonos;

    // Cobro de un plazo
    [ObservableProperty] private PlazoFila? _plazoSeleccionado;
    [ObservableProperty] private string _montoAbonoTexto = string.Empty;
    [ObservableProperty] private Opcion<MetodoPago> _metodoSeleccionado;
    [ObservableProperty] private string _notasAbono = string.Empty;
    [ObservableProperty] private string _mensajeCobro = string.Empty;

    public bool PuedeCobrar => SesionActual.TienePermiso(Permisos.Ventas);

    partial void OnPlazoSeleccionadoChanged(PlazoFila? value)
    {
        MensajeCobro = string.Empty;
        // Sugerir lo que falta del plazo: es lo que se cobra el 99% de las veces
        MontoAbonoTexto = value is null || value.SaldoPendiente <= 0m
            ? string.Empty
            : value.SaldoPendiente.ToString("0.##", Textos.CulturaRd);
    }

    public async Task CargarAsync(long ventaId)
    {
        try
        {
            _ventaId = ventaId;
            var estado = await _plazos.ObtenerEstadoAsync(ventaId);
            var datos = await _ventas.ObtenerFacturaAsync(ventaId);
            _estado = estado;
            _datosVenta = datos;

            Codigo = estado.Codigo;
            EsPlazos = estado.Tipo == TipoVenta.Plazos;
            EsSeparacion = estado.Tipo == TipoVenta.Separacion;
            TipoTexto = estado.Tipo switch
            {
                TipoVenta.Plazos => "Venta financiada por plazos",
                TipoVenta.Separacion => "Separación / apartado",
                _ => "Venta al contado"
            };
            ClienteNombre = datos.ClienteNombre;
            VehiculoDescripcion = datos.VehiculoDescripcion;

            Precio = estado.Precio;
            Inicial = estado.Inicial;
            TotalAPagar = estado.TotalAPlazos;
            Pendiente = estado.Pendiente;
            Pagado = estado.Pagado;
            PlazosTexto = estado.CantidadPlazos == 0
                ? "Sin plazos"
                : $"{estado.PlazosPagados} de {estado.CantidadPlazos} pagados" +
                  (estado.PlazosAtrasados > 0 ? $" · {estado.PlazosAtrasados} atrasado(s)" : "");

            var hoy = FechaNegocio.Hoy;
            Plazos.Clear();
            foreach (var plazo in estado.Plazos)
                Plazos.Add(new PlazoFila(plazo, hoy));

            var abonos = await _plazos.ObtenerPagosAsync(ventaId);
            Abonos.Clear();
            foreach (var abono in abonos)
                Abonos.Add(new AbonoFila(abono));
            HayAbonos = Abonos.Count > 0;

            ActualizarAviso(estado, hoy);
            MensajeCobro = string.Empty;
            OnPropertyChanged(nameof(PuedeCobrar));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cargando el financiamiento de la venta {Id}", ventaId);
            _dialogos.MostrarError("Financiamiento", $"No se pudo cargar.\n\n{ex.Message}");
        }
    }

    private void ActualizarAviso(EstadoFinanciamiento estado, DateOnly hoy)
    {
        if (estado.SeparacionVencida(hoy))
        {
            AvisoTexto = $"⚠ La separación venció el {estado.FechaLimite:dd/MM/yyyy}. " +
                         "El cliente ya no tiene derecho sobre el vehículo: liberalo o acordá una prórroga.";
            HayAviso = true;
        }
        else if (estado.Tipo == TipoVenta.Separacion && estado.FechaLimite is { } limite)
        {
            var dias = limite.DayNumber - hoy.DayNumber;
            AvisoTexto = $"Separación válida hasta el {limite:dd/MM/yyyy} " +
                         (dias <= 3 ? $"— ⚠ quedan {dias} día(s)." : $"({dias} días restantes).");
            HayAviso = true;
        }
        else if (estado.PlazosAtrasados > 0)
        {
            AvisoTexto = $"⚠ {estado.PlazosAtrasados} plazo(s) atrasado(s): {estado.Pendiente:N2} DOP pendientes.";
            HayAviso = true;
        }
        else if (estado.EstaSaldada && estado.CantidadPlazos > 0)
        {
            AvisoTexto = "✓ Venta saldada: todos los plazos están pagados.";
            HayAviso = true;
        }
        else
        {
            AvisoTexto = string.Empty;
            HayAviso = false;
        }
    }

    [RelayCommand]
    private void Volver() => VolverSolicitado?.Invoke();

    [RelayCommand]
    private async Task CobrarAsync()
    {
        MensajeCobro = string.Empty;
        if (PlazoSeleccionado is not { } plazo)
        {
            MensajeCobro = "Elegí el plazo que vas a cobrar.";
            return;
        }
        if (!decimal.TryParse(MontoAbonoTexto, NumberStyles.Number, Textos.CulturaRd, out var monto) || monto <= 0m)
        {
            MensajeCobro = "Ingresá un monto válido mayor que cero (ej. 25,000).";
            return;
        }

        try
        {
            var recibo = await _plazos.CobrarPlazoAsync(plazo.Id, monto,
                MetodoSeleccionado.Valor, NotasAbono);
            NotasAbono = string.Empty;
            await CargarAsync(_ventaId);
            _dialogos.Informar("Cobro registrado",
                $"Abono de {monto:N2} DOP al plazo #{plazo.Numero}.\nRecibo {recibo}.");
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or UnauthorizedAccessException)
        {
            MensajeCobro = ex.Message;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cobrando el plazo {Id}", plazo.Id);
            _dialogos.MostrarError("Cobro", $"No se pudo registrar el cobro.\n\n{ex.Message}");
        }
    }

    /// <summary>Carta de compromiso con el calendario pactado (solo ventas por plazos).</summary>
    [RelayCommand]
    private void VerCarta()
    {
        if (_estado is not { } estado || _datosVenta is not { } d)
            return;
        if (estado.Tipo != TipoVenta.Plazos)
        {
            _dialogos.Informar("Carta de compromiso",
                "La carta de compromiso aplica a las ventas financiadas por plazos.");
            return;
        }

        CartaSolicitada?.Invoke(new CartaCompromisoImpresa(
            NegocioNombre: _ajustes.NombreNegocio,
            NegocioRnc: _ajustes.RncNegocio,
            NegocioTelefono: _ajustes.TelefonoNegocio,
            NegocioCiudad: _ajustes.CiudadNegocio,
            Codigo: estado.Codigo,
            FechaTexto: FechaNegocio.AUtcLocal(d.FechaVentaUtc).ToString(Textos.FormatoFecha, Textos.CulturaRd),
            Precio: estado.Precio,
            Inicial: estado.Inicial,
            TotalAPlazos: estado.TotalAPlazos,
            ClienteNombre: d.ClienteNombre,
            ClienteCedula: d.ClienteCedula ?? "—",
            ClienteDireccion: d.ClienteDireccion ?? "—",
            ClienteTelefono: d.ClienteTelefono ?? "—",
            VehiculoDescripcion: d.VehiculoDescripcion,
            Vin: d.Vin ?? "—",
            Placa: d.Placa ?? "—",
            Matricula: d.Matricula ?? "—",
            Color: d.Color ?? "—",
            AnioTexto: d.Anio?.ToString(Textos.CulturaRd) ?? "—",
            Plazos: [.. Plazos.Select(p => new PlazoImpreso(p.Numero, p.FechaTexto, p.Monto, p.EstadoTexto))],
            EmitidoPor: SesionActual.Nombre));
    }

    /// <summary>Recibo de separación con la fecha límite de derecho (15 días).</summary>
    [RelayCommand]
    private void VerReciboSeparacion()
    {
        if (_estado is not { } estado || _datosVenta is not { } d)
            return;
        if (estado.Tipo != TipoVenta.Separacion)
        {
            _dialogos.Informar("Recibo de separación",
                "El recibo de separación aplica a las ventas registradas como separación/apartado.");
            return;
        }

        var limite = estado.FechaLimite ?? FechaNegocio.Hoy;
        var emision = DateOnly.FromDateTime(FechaNegocio.AUtcLocal(d.FechaVentaUtc));
        SeparacionSolicitada?.Invoke(new ReciboSeparacionImpreso(
            NegocioNombre: _ajustes.NombreNegocio,
            NegocioRnc: _ajustes.RncNegocio,
            NegocioTelefono: _ajustes.TelefonoNegocio,
            NegocioCiudad: _ajustes.CiudadNegocio,
            Codigo: estado.Codigo,
            FechaTexto: emision.ToString(Textos.FormatoFecha, Textos.CulturaRd),
            Precio: estado.Precio,
            Adelanto: estado.Inicial,
            Pendiente: Math.Max(0m, estado.Precio - estado.Inicial),
            FechaLimiteTexto: limite.ToString(Textos.FormatoFecha, Textos.CulturaRd),
            DiasDerecho: Math.Max(0, limite.DayNumber - emision.DayNumber),
            ClienteNombre: d.ClienteNombre,
            ClienteCedula: d.ClienteCedula ?? "—",
            ClienteTelefono: d.ClienteTelefono ?? "—",
            VehiculoDescripcion: d.VehiculoDescripcion,
            Vin: d.Vin ?? "—",
            Placa: d.Placa ?? "—",
            Color: d.Color ?? "—",
            AnioTexto: d.Anio?.ToString(Textos.CulturaRd) ?? "—",
            EmitidoPor: SesionActual.Nombre));
    }
}
