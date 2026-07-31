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

    /// <summary>
    /// Papeles listos para emitir tras registrar la venta (033). La View los
    /// RETIRA con <see cref="TomarPapelesPendientes"/> cuando ya esta en
    /// pantalla.
    ///
    /// Es un buzon y no un evento a proposito: cuando se registra una venta, la
    /// pantalla de financiamiento todavia no existe —se crea recien al navegar—,
    /// asi que un evento disparado durante la carga no tendria a nadie
    /// escuchando y los papeles se perderian en silencio.
    /// </summary>
    private PapelesDeVenta? _papelesPendientes;

    /// <summary>
    /// Devuelve los papeles a emitir y VACIA el buzon, para que no se vuelvan a
    /// imprimir cada vez que la pantalla entra en el arbol visual.
    /// </summary>
    public PapelesDeVenta? TomarPapelesPendientes()
    {
        var papeles = _papelesPendientes;
        _papelesPendientes = null;
        return papeles;
    }

    public VentaFinanciamientoViewModel(VentaPlazoService plazos, VentaVehiculoService ventas,
        AjustesLocales ajustes, IDialogService dialogos, ExpedienteViewModel expediente)
    {
        _plazos = plazos;
        _ventas = ventas;
        _ajustes = ajustes;
        _dialogos = dialogos;
        Expediente = expediente;

        Metodos =
        [
            new Opcion<MetodoPago>(MetodoPago.Efectivo, "Efectivo"),
            new Opcion<MetodoPago>(MetodoPago.Transferencia, "Transferencia"),
            new Opcion<MetodoPago>(MetodoPago.Cheque, "Cheque"),
            new Opcion<MetodoPago>(MetodoPago.Otro, "Otro")
        ];
        _metodoSeleccionado = Metodos[0];
    }

    /// <summary>
    /// Expediente digital de ESTE contrato (018, pedido 2026-07-27): las
    /// facturas, documentos e imágenes que entregó el cliente para la compra.
    /// </summary>
    public ExpedienteViewModel Expediente { get; }

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

    /// <summary>
    /// Cómo se va a repartir el monto que se está escribiendo (pedido de Yuber
    /// 2026-07-31: "si tiene que pagar 66,666.67 y paga 70,000, mostrar en otro
    /// textbox lo abonado"). Se calcula antes de cobrar para que el cajero vea
    /// qué pasa con el excedente ANTES de confirmar, no después.
    /// </summary>
    [ObservableProperty] private string _repartoTexto = string.Empty;

    partial void OnPlazoSeleccionadoChanged(PlazoFila? value)
    {
        MensajeCobro = string.Empty;
        // Sugerir lo que falta del plazo: es lo que se cobra el 99% de las veces
        MontoAbonoTexto = value is null || value.SaldoPendiente <= 0m
            ? string.Empty
            : value.SaldoPendiente.ToString("0.##", Textos.CulturaRd);
        ActualizarReparto();
    }

    partial void OnMontoAbonoTextoChanged(string value) => ActualizarReparto();

    private void ActualizarReparto()
    {
        RepartoTexto = string.Empty;
        if (PlazoSeleccionado is not { } plazo || _estado is null)
            return;
        if (!decimal.TryParse(MontoAbonoTexto, NumberStyles.Number, Textos.CulturaRd, out var monto)
            || monto <= 0m)
            return;

        var aEste = Math.Min(monto, plazo.SaldoPendiente);
        var excedente = monto - aEste;

        if (excedente <= 0m)
        {
            RepartoTexto = aEste >= plazo.SaldoPendiente
                ? $"Salda el plazo #{plazo.Numero}."
                : $"Abono parcial: al plazo #{plazo.Numero} le quedarían " +
                  $"{plazo.SaldoPendiente - aEste:N2} DOP.";
            return;
        }

        // Se reparte de más: se muestra a dónde va y qué queda del siguiente
        var siguientes = _estado.Plazos
            .Where(p => p.Numero > plazo.Numero && p.Estado == EstadoPlazo.Pendiente)
            .OrderBy(p => p.Numero)
            .ToList();
        var deudaSiguiente = siguientes.Sum(p => p.SaldoPendiente);

        if (excedente > deudaSiguiente)
        {
            RepartoTexto = $"⚠ Son {monto:N2} y todo lo que falta de la venta es " +
                           $"{plazo.SaldoPendiente + deudaSiguiente:N2} DOP.";
            return;
        }

        var proximo = siguientes.FirstOrDefault();
        RepartoTexto = $"Salda el plazo #{plazo.Numero} y baja {excedente:N2} DOP a los siguientes." +
            (proximo is null ? string.Empty
             : $" Al plazo #{proximo.Numero} le quedarían " +
               $"{Math.Max(0m, proximo.SaldoPendiente - excedente):N2} DOP.");
    }

    /// <param name="reciénRegistrada">
    /// True cuando se acaba de registrar la venta: ademas de mostrar la
    /// pantalla, se emiten e imprimen los papeles y se archivan solos.
    /// </param>
    public async Task CargarAsync(long ventaId, bool reciénRegistrada = false)
    {
        try
        {
            _ventaId = ventaId;
            var estado = await _plazos.ObtenerEstadoAsync(ventaId);
            var datos = await _ventas.ObtenerFacturaAsync(ventaId);
            _estado = estado;
            _datosVenta = datos;

            // ¿Está cancelada? El cartel manda: explica por qué los plazos
            // aparecen cancelados y cuánta plata se devolvió (028).
            var cancelacion = await _ventas.ObtenerCancelacionAsync(ventaId);
            EstaCancelada = cancelacion is not null;
            CancelacionTexto = cancelacion is { } c
                ? $"VENTA CANCELADA — {c.Motivo}. De {estado.RecibidoTotal:N2} DOP cobrados, " +
                  $"el negocio retuvo {c.Retenido:N2} ({c.Porcentaje:0.##}%) y se devolvieron " +
                  $"{c.Devuelto:N2} DOP."
                : string.Empty;

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

            await Expediente.CargarAsync(ventaId);

            // Recien registrada: se emiten los papeles y se archivan solos, para
            // que el usuario quede parado en esta pantalla con lo suyo ya hecho
            // y solo le falte subir lo que trajo el cliente.
            if (reciénRegistrada)
                EmitirPapeles();

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
            var abono = await _plazos.CobrarPlazoAsync(plazo.Id, monto,
                MetodoSeleccionado.Valor, NotasAbono);
            NotasAbono = string.Empty;
            await CargarAsync(_ventaId);

            // Si el excedente bajó a los plazos siguientes hay que DECIRLO: el
            // cajero necesita saber cuánto le queda por pagar al cliente.
            var recibos = string.Join(", ", abono.Recibos);
            var detalle = abono.TocoVariosPlazos
                ? $"Abono de {abono.Aplicado:N2} DOP desde el plazo #{plazo.Numero}. " +
                  $"El excedente bajó a los plazos siguientes.\n\nRecibos: {recibos}"
                : $"Abono de {abono.Aplicado:N2} DOP al plazo #{plazo.Numero}.\nRecibo {recibos}.";
            detalle += abono.VentaSaldada
                ? "\n\nLa venta quedó SALDADA."
                : $"\n\nQueda por pagar: {abono.SaldoRestante:N2} DOP.";

            _dialogos.Informar("Cobro registrado", detalle);
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

    // ---------- Cancelación: el cliente devolvió el vehículo (028) ----------

    /// <summary>
    /// La View pone acá cómo pedirle al usuario el motivo y el porcentaje.
    /// Es una FUNCIÓN y no un evento porque devuelve un valor: null significa
    /// que se arrepintió, y entonces no se cancela nada.
    /// </summary>
    public Func<string, decimal, decimal, bool, (string Motivo, decimal Porcentaje, bool Fijar)?>?
        CancelacionSolicitada { get; set; }

    /// <summary>
    /// La View abre el formulario de corrección (033) y devuelve lo confirmado,
    /// o null si el usuario se arrepintió. Propiedad y no `event` porque
    /// DEVUELVE un valor: un evento con retorno da CS0079.
    /// </summary>
    public Func<VentaParaEditar, EdicionVenta?>? EdicionSolicitada { get; set; }

    /// <summary>Cancelar mueve plata y devuelve un vehículo: es de Admin.</summary>
    public bool PuedeCancelar => SesionActual.EsAdmin;

    /// <summary>
    /// Muestra el botón "Editar" (033). El Admin lo tiene siempre; a los demás
    /// se los habilita él con el permiso ventas_editar, igual que en préstamos.
    /// </summary>
    public bool PuedeEditar => SesionActual.EsAdmin || SesionActual.TienePermiso(Permisos.VentasEditar);

    /// <summary>Ya cancelada: se muestra el cartel y se esconde el botón.</summary>
    [ObservableProperty] private bool _estaCancelada;
    [ObservableProperty] private string _cancelacionTexto = string.Empty;

    [RelayCommand]
    private async Task CancelarVentaAsync()
    {
        if (_estado is not { } estado || CancelacionSolicitada is null)
            return;

        var respuesta = CancelacionSolicitada(estado.Codigo, estado.RecibidoTotal,
            _ajustes.RetencionCancelacionPorcentaje, _ajustes.RetencionCancelacionFija);
        if (respuesta is not { } datos)
            return;

        try
        {
            var resultado = await _plazos.CancelarVentaAsync(
                new CancelacionVenta(_ventaId, datos.Motivo, datos.Porcentaje));

            // El porcentaje queda propuesto para la próxima si así lo pidió
            if (datos.Fijar)
            {
                _ajustes.RetencionCancelacionPorcentaje = datos.Porcentaje;
                _ajustes.RetencionCancelacionFija = true;
                _ajustes.Guardar();
            }

            await CargarAsync(_ventaId);
            _dialogos.Informar("Venta cancelada",
                $"La venta {estado.Codigo} quedó cancelada y el vehículo volvió al inventario.\n\n" +
                $"Cobrado: {resultado.Cobrado:N2} DOP\n" +
                $"Se queda el negocio: {resultado.Retenido:N2} DOP ({resultado.RetencionPorcentaje:0.##}%)\n" +
                $"A devolver al cliente: {resultado.Devuelto:N2} DOP");
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
                                      or UnauthorizedAccessException)
        {
            _dialogos.MostrarError("Cancelar la venta", ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cancelando la venta {Id}", _ventaId);
            _dialogos.MostrarError("Cancelar la venta", $"No se pudo cancelar.\n\n{ex.Message}");
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

    /// <summary>
    /// Corrige la venta (033). Antes de abrir el formulario le pregunta al
    /// servicio hasta dónde se puede editar, para que la pantalla no ofrezca
    /// cambiar montos que después van a ser rechazados.
    /// </summary>
    [RelayCommand]
    private async Task EditarAsync()
    {
        if (EdicionSolicitada is null || _estado is not { } estado || _datosVenta is not { } d)
            return;

        try
        {
            var permitido = await _plazos.ConsultarEdicionPermitidaAsync(_ventaId);

            var cambios = EdicionSolicitada(new VentaParaEditar(
                _ventaId, estado.Codigo, estado.Tipo, estado.Precio, estado.Inicial,
                d.MetodoPago, d.Notas, permitido, PrevisualizarCalendario));
            if (cambios is null)
                return;   // se arrepintió

            await _plazos.EditarVentaAsync(cambios);
            await CargarAsync(_ventaId);
            _dialogos.Informar("Venta corregida",
                $"La venta {estado.Codigo} quedó corregida." +
                (permitido.Todo && estado.Tipo == TipoVenta.Plazos
                    ? "\n\nEl calendario de plazos se rehizo con los datos nuevos."
                    : ""));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            _dialogos.MostrarError("Corregir venta", ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error corrigiendo la venta {Id}", _ventaId);
            _dialogos.MostrarError("Corregir venta", $"No se pudo corregir la venta.\n\n{ex.Message}");
        }
    }

    /// <summary>
    /// Cómo queda el calendario con lo que el usuario está tipeando. Usa el
    /// MISMO cálculo que persiste el servicio, así lo que se ve en pantalla es
    /// exactamente lo que se va a guardar.
    /// </summary>
    private string PrevisualizarCalendario(decimal precio, decimal inicial)
    {
        if (inicial > precio)
            return "La inicial no puede ser mayor que el precio de venta.";

        var saldo = precio - inicial;
        if (_estado is not { } estado || estado.Tipo != TipoVenta.Plazos)
            return $"Saldo tras lo recibido: {saldo.ToString("N2", Textos.CulturaRd)} DOP.";

        if (Plazos.Count == 0)
            return "Esta venta no tiene plazos cargados.";

        try
        {
            // Se conservan la cantidad, la fecha del primero y el intervalo: es
            // lo que se pactó con el cliente y no es lo que se está corrigiendo.
            var cadaDias = Plazos.Count > 1
                ? Math.Max(1, Plazos[1].Plazo.FechaVencimiento.DayNumber
                            - Plazos[0].Plazo.FechaVencimiento.DayNumber)
                : 30;
            var nuevos = VentaPlazoService.CalcularPlazos(precio,
                new PlanPlazos(inicial, Plazos.Count, Plazos[0].Plazo.FechaVencimiento, cadaDias));

            return $"{nuevos.Count} plazo(s) de {nuevos[0].Monto.ToString("N2", Textos.CulturaRd)} DOP " +
                   $"(el último, {nuevos[^1].Monto.ToString("N2", Textos.CulturaRd)}). " +
                   $"Total a plazos: {saldo.ToString("N2", Textos.CulturaRd)} DOP.";
        }
        catch (ArgumentException ex)
        {
            // El cálculo valida los rangos; se muestra su mensaje tal cual en
            // vez de inventar uno propio que diga otra cosa.
            return ex.Message;
        }
    }

    /// <summary>
    /// Junta los papeles que corresponden a ESTA venta y se los pasa a la View
    /// para imprimir y archivar (033).
    ///
    /// CUALES SON, segun como se pacto:
    ///   Contado     -> factura.
    ///   Por plazos  -> factura + carta de compromiso.
    ///   Separacion  -> factura + recibo de separacion.
    /// Es la misma cuenta que ya usaba CantidadDocumentos en Contratos, asi que
    /// lo que se archiva coincide con lo que esa pantalla dice que hay.
    /// </summary>
    private void EmitirPapeles()
    {
        if (_estado is not { } estado || _datosVenta is not { } d)
            return;

        var factura = new FacturaVentaImpresa(
            NegocioNombre: _ajustes.NombreNegocio,
            NegocioRnc: _ajustes.RncNegocio,
            NegocioTelefono: _ajustes.TelefonoNegocio,
            NegocioCiudad: _ajustes.CiudadNegocio,
            Codigo: d.Codigo,
            FechaTexto: FechaNegocio.AUtcLocal(d.FechaVentaUtc).ToString(Textos.FormatoFecha, Textos.CulturaRd),
            Precio: d.Precio,
            MetodoTexto: Textos.De(d.MetodoPago),
            Notas: d.Notas,
            VendedorNombre: d.VendedorNombre,
            ClienteNombre: d.ClienteNombre,
            ClienteCedula: d.ClienteCedula ?? "—",
            ClienteTelefono: d.ClienteTelefono ?? "—",
            ClienteDireccion: d.ClienteDireccion ?? "—",
            VehiculoDescripcion: d.VehiculoDescripcion,
            Vin: d.Vin ?? "—",
            Placa: d.Placa ?? "—",
            Matricula: d.Matricula ?? "—",
            Color: d.Color ?? "—",
            AnioTexto: d.Anio?.ToString(Textos.CulturaRd) ?? "—");

        CartaCompromisoImpresa? carta = null;
        ReciboSeparacionImpreso? separacion = null;

        if (estado.Tipo == TipoVenta.Plazos)
        {
            carta = new CartaCompromisoImpresa(
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
                EmitidoPor: SesionActual.Nombre);
        }
        else if (estado.Tipo == TipoVenta.Separacion)
        {
            var limite = estado.FechaLimite ?? FechaNegocio.Hoy;
            var emision = DateOnly.FromDateTime(FechaNegocio.AUtcLocal(d.FechaVentaUtc));
            separacion = new ReciboSeparacionImpreso(
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
                EmitidoPor: SesionActual.Nombre);
        }

        _papelesPendientes = new PapelesDeVenta(_ventaId, d.Codigo, factura, carta, separacion);
    }

    /// <summary>Recarga el expediente tras archivar los papeles recien emitidos.</summary>
    public Task RefrescarExpedienteAsync() => Expediente.CargarAsync(_ventaId);
}

/// <summary>
/// Los papeles a emitir al registrar una venta (033). Van juntos porque se
/// imprimen y se archivan de una sola pasada: cual de los dos contratos viene
/// depende de como se pacto la venta, y nunca vienen los dos.
/// </summary>
public record PapelesDeVenta(
    long VentaId,
    string Codigo,
    FacturaVentaImpresa Factura,
    CartaCompromisoImpresa? Carta,
    ReciboSeparacionImpreso? Separacion);

/// <summary>
/// Lo que el diálogo de corrección de una venta necesita. Incluye el delegado
/// de vista previa porque la capa de Views NO referencia Services (lo impide el
/// grafo de proyectos, a propósito): el cálculo baja del ViewModel.
/// </summary>
public record VentaParaEditar(
    long VentaId,
    string Codigo,
    TipoVenta Tipo,
    decimal Precio,
    decimal Inicial,
    MetodoPago Metodo,
    string? Notas,
    EdicionVentaPermitida Permitido,
    /// <summary>(precio, inicial) → cómo queda el calendario, en una línea.</summary>
    Func<decimal, decimal, string> Previsualizar);
