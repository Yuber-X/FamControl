using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAControl.Common;
using FAControl.Models;
using FAControl.Services;
using Serilog;

namespace FAControl.ViewModels;

/// <summary>
/// Detalle completo de un alquiler (031 — pedido del cliente 2026-07-30:
/// "agregar un 'ver detalles' para ver mas detalles completos sobre esos
/// alquileres").
///
/// Sigue el molde de Financiamiento de venta, como pidió, pero SIN mezclar
/// datos: los alquileres tienen su propia tabla, su propio expediente y sus
/// propias reglas. Es otra pantalla, no la misma con un filtro.
///
/// Los dos botones que pidió:
///  * EDITAR — corrige errores de digitación, solo mientras el contrato sigue
///    abierto.
///  * CERRAR — uno solo para devolución y cancelación ("con un solo btn seria
///    suficiente"), que pregunta cuál de las dos es. No son lo mismo: devuelto
///    es plata ganada, cancelado puede ser plata a devolver.
/// </summary>
public partial class AlquilerDetalleViewModel : ObservableObject
{
    private readonly AlquilerService _alquileres;
    private readonly VehiculoService _vehiculos;
    private readonly ClienteService _clientes;
    private readonly IDialogService _dialogos;
    private long _alquilerId;
    /// <summary>Inicio del contrato: el diálogo de cierre cuenta los días reales desde acá.</summary>
    private DateOnly _fechaInicio;

    public event Action? VolverSolicitado;

    /// <summary>
    /// La View abre el diálogo de cierre y devuelve lo elegido, o null si se
    /// arrepintió. Propiedad y no `event` porque DEVUELVE un valor.
    /// </summary>
    public Func<CierreAlquilerPedido, CierreAlquilerDatos?>? CierreSolicitado { get; set; }

    /// <summary>Ídem para la corrección.</summary>
    public Func<AlquilerParaEditar, EdicionAlquiler?>? EdicionSolicitada { get; set; }

    /// <summary>Ídem para la renovación (039).</summary>
    public Func<RenovacionAlquilerPedido, RenovacionAlquiler?>? RenovacionSolicitada { get; set; }

    public AlquilerDetalleViewModel(AlquilerService alquileres, VehiculoService vehiculos,
        ClienteService clientes, IDialogService dialogos, ExpedienteViewModel expediente)
    {
        _alquileres = alquileres;
        _vehiculos = vehiculos;
        _clientes = clientes;
        _dialogos = dialogos;
        Expediente = expediente;
        _metodoCobro = MetodosPago[0];
    }

    /// <summary>
    /// Expediente del alquiler: el contrato firmado, la licencia del conductor,
    /// fotos del estado del auto al salir y al volver. Misma pantalla que en
    /// préstamos y ventas.
    /// </summary>
    public ExpedienteViewModel Expediente { get; }

    // ---------- Cobros del alquiler (034) ----------
    // Su propio grid y su propia forma de cobrar, como se pidio. En la practica
    // el alquiler se paga en dos veces —adelanto al retirar, resto al
    // devolver— y antes no habia donde anotarlo.

    public ObservableCollection<AlquilerPagoFila> Cobros { get; } = [];

    /// <summary>
    /// Las cuotas MENSUALES del periodo (037). Un alquiler no se cobra de una:
    /// se cobra mes a mes hasta el dia pactado.
    /// </summary>
    public ObservableCollection<CuotaAlquilerFila> Calendario { get; } = [];
    [ObservableProperty] private bool _hayCalendario;

    [ObservableProperty] private decimal _montoACobrar;
    [ObservableProperty] private decimal _cobrado;
    [ObservableProperty] private decimal _pendiente;
    [ObservableProperty] private bool _estaSaldado;
    [ObservableProperty] private string _saldoAFavorTexto = string.Empty;
    [ObservableProperty] private bool _haySaldoAFavor;
    [ObservableProperty] private bool _sinCobros = true;

    /// <summary>Lo que el usuario esta tipeando para cobrar.</summary>
    [ObservableProperty] private string _montoCobroTexto = string.Empty;
    [ObservableProperty] private Opcion<MetodoPago> _metodoCobro;
    [ObservableProperty] private string _notaCobro = string.Empty;

    public IReadOnlyList<Opcion<MetodoPago>> MetodosPago { get; } =
    [
        new Opcion<MetodoPago>(MetodoPago.Efectivo, "Efectivo"),
        new Opcion<MetodoPago>(MetodoPago.Transferencia, "Transferencia"),
        new Opcion<MetodoPago>(MetodoPago.Cheque, "Cheque"),
        new Opcion<MetodoPago>(MetodoPago.Otro, "Otro")
    ];

    /// <summary>
    /// Se puede cobrar mientras quede algo por cobrar y el contrato no este
    /// cancelado. Un alquiler ya devuelto SI admite cobro: es justo cuando se
    /// cobra el saldo.
    /// </summary>
    public bool PuedeCobrar => !EstaSaldado && !FueCancelado
                               && SesionActual.TienePermiso(Permisos.Alquileres);

    /// <summary>
    /// Ya se cobro todo. Se muestra un cartel EN LUGAR del formulario: antes el
    /// formulario simplemente desaparecia y parecia que la pantalla se habia
    /// roto (reportado 2026-08-01). Editar y Cerrar NO dependen de esto.
    /// </summary>
    public bool MostrarSaldado => EstaSaldado && !FueCancelado;

    [ObservableProperty] private string _codigo = string.Empty;
    [ObservableProperty] private string _clienteNombre = string.Empty;
    [ObservableProperty] private string _clienteCedula = "—";
    [ObservableProperty] private string _clienteTelefono = "—";
    [ObservableProperty] private string _vehiculoDescripcion = string.Empty;
    [ObservableProperty] private string _vehiculoMatricula = "—";
    [ObservableProperty] private string _periodoTexto = string.Empty;
    [ObservableProperty] private string _estadoTexto = string.Empty;
    [ObservableProperty] private decimal _tarifaDia;
    [ObservableProperty] private int _dias;
    [ObservableProperty] private decimal _montoTotal;
    [ObservableProperty] private string _notasTexto = string.Empty;
    [ObservableProperty] private bool _tieneNotas;
    [ObservableProperty] private string _registradoPor = "—";

    /// <summary>Estado del contrato: manda qué botones se ven.</summary>
    [ObservableProperty] private bool _estaActivo;

    /// <summary>Cartel del cierre, cuando ya está cerrado.</summary>
    [ObservableProperty] private bool _estaCerrado;
    [ObservableProperty] private string _cierreTitulo = string.Empty;
    [ObservableProperty] private string _cierreTexto = string.Empty;
    [ObservableProperty] private bool _fueCancelado;

    /// <summary>Aviso de atraso mientras el contrato sigue abierto.</summary>
    [ObservableProperty] private bool _estaAtrasado;
    [ObservableProperty] private string _atrasoTexto = string.Empty;

    // ---------- Renovaciones (039) ----------
    // Cuando se cumple el plazo, el auto vuelve al inventario (Cerrar) o el
    // cliente sigue con él (Renovar). Cada renovación es un tramo con su tarifa.

    public ObservableCollection<RenovacionFila> Renovaciones { get; } = [];
    [ObservableProperty] private bool _hayRenovaciones;

    /// <summary>
    /// La tarifa que rige hoy: la del último tramo. TarifaDia se queda con la
    /// original a propósito, para no perder a qué precio salió el contrato.
    /// </summary>
    [ObservableProperty] private decimal _tarifaVigente;

    /// <summary>True cuando la vigente ya no es la original: se muestran las dos.</summary>
    [ObservableProperty] private bool _tarifaCambio;

    /// <summary>
    /// Editar y cerrar los tiene el Admin, o quien él habilite con el permiso
    /// alquileres_editar. Cerrar libera un vehículo y puede implicar devolver
    /// dinero: no es una acción de cualquiera.
    /// </summary>
    public bool PuedeGestionar =>
        SesionActual.EsAdmin || SesionActual.TienePermiso(Permisos.AlquileresEditar);

    /// <summary>
    /// Editar y cerrar solo tienen sentido con el contrato abierto. Se resuelve
    /// acá y no con un MultiBinding en XAML: es una regla, no una decoración, y
    /// además en WPF `Style` no se puede fijar dos veces (MC3024).
    /// </summary>
    public bool PuedeOperar => PuedeGestionar && EstaActivo;

    partial void OnEstaActivoChanged(bool value) => OnPropertyChanged(nameof(PuedeOperar));

    public async Task CargarAsync(long alquilerId)
    {
        try
        {
            _alquilerId = alquilerId;
            var alquiler = await _alquileres.ObtenerPorIdAsync(alquilerId)
                ?? throw new InvalidOperationException($"No existe el alquiler con id {alquilerId}.");

            var vehiculo = await _vehiculos.ObtenerPorIdAsync(alquiler.VehiculoId);
            var cliente = await _clientes.ObtenerPorIdAsync(alquiler.ClienteId);

            Codigo = alquiler.Codigo;
            ClienteNombre = cliente?.NombreCompleto ?? "(cliente eliminado)";
            ClienteCedula = string.IsNullOrWhiteSpace(cliente?.Cedula) ? "—" : cliente.Cedula;
            ClienteTelefono = string.IsNullOrWhiteSpace(cliente?.Telefono) ? "—" : cliente.Telefono!;
            VehiculoDescripcion = vehiculo?.Descripcion ?? "(vehículo eliminado)";
            VehiculoMatricula = string.IsNullOrWhiteSpace(vehiculo?.Placa) ? "—" : vehiculo.Placa!;

            PeriodoTexto =
                $"{alquiler.FechaInicio.ToString(Textos.FormatoFecha, Textos.CulturaRd)} → " +
                $"{alquiler.FechaFin.ToString(Textos.FormatoFecha, Textos.CulturaRd)}";
            _fechaInicio = alquiler.FechaInicio;
            TarifaDia = alquiler.TarifaDia;
            Dias = alquiler.Dias;
            MontoTotal = alquiler.MontoTotal;
            EstadoTexto = Textos.De(alquiler.Estado);
            EstaActivo = alquiler.Estado == EstadoAlquiler.Activo;
            TieneNotas = !string.IsNullOrWhiteSpace(alquiler.Notas);
            NotasTexto = alquiler.Notas ?? string.Empty;

            await CargarRenovacionesAsync(alquiler);
            ArmarCierre(alquiler);
            ArmarAtraso(alquiler);

            await CargarCobrosAsync();

            OnPropertyChanged(nameof(PuedeGestionar));
            OnPropertyChanged(nameof(PuedeOperar));
            await Expediente.CargarAsync(DuenoExpediente.DeAlquiler(alquilerId));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cargando el detalle del alquiler {Id}", alquilerId);
            _dialogos.MostrarError("Alquiler", $"No se pudo cargar el alquiler.\n\n{ex.Message}");
        }
    }

    /// <summary>Cómo terminó el contrato, en palabras y con la plata.</summary>
    private void ArmarCierre(Alquiler alquiler)
    {
        EstaCerrado = alquiler.Estado != EstadoAlquiler.Activo;
        if (!EstaCerrado)
        {
            CierreTitulo = string.Empty;
            CierreTexto = string.Empty;
            return;
        }

        FueCancelado = alquiler.Estado == EstadoAlquiler.Cancelado;
        var cuando = alquiler.CerradoAtUtc is { } c
            ? FechaNegocio.AUtcLocal(c).ToString(Textos.FormatoFecha, Textos.CulturaRd)
            : "—";

        CierreTitulo = FueCancelado
            ? $"Alquiler cancelado el {cuando}"
            : $"Vehículo devuelto el " +
              (alquiler.FechaDevolucion?.ToString(Textos.FormatoFecha, Textos.CulturaRd) ?? cuando);

        // El motivo NO se inventa cuando falta (contratos cerrados antes de que
        // existiera el campo): decir "no indicado" es la verdad.
        var motivo = string.IsNullOrWhiteSpace(alquiler.CerradoMotivo)
            ? "no indicado"
            : alquiler.CerradoMotivo!;
        var quien = string.IsNullOrWhiteSpace(alquiler.CerradoPorNombre)
            ? ""
            : $" · Lo cerró {alquiler.CerradoPorNombre}";

        if (FueCancelado)
        {
            CierreTexto = $"Motivo: {motivo}{quien}. " +
                          "El contrato no llegó a correr, así que no cuenta como ingreso.";
            return;
        }

        var reales = alquiler.DiasReales ?? alquiler.Dias;
        var final = alquiler.MontoFinal ?? alquiler.MontoTotal;
        var comparacion = reales == alquiler.Dias
            ? $"{reales} día(s), tal como se pactó"
            : reales > alquiler.Dias
                ? $"{reales} día(s) usados sobre {alquiler.Dias} pactados — devolvió {reales - alquiler.Dias} día(s) tarde"
                : $"{reales} día(s) usados sobre {alquiler.Dias} pactados — devolvió antes";

        CierreTexto = $"{comparacion}. Corresponde cobrar " +
                      $"{final.ToString("N2", Textos.CulturaRd)} DOP " +
                      $"(pactado {alquiler.MontoTotal.ToString("N2", Textos.CulturaRd)}). " +
                      $"Motivo: {motivo}{quien}.";
    }

    /// <summary>Aviso cuando el contrato sigue abierto y ya pasó la fecha pactada.</summary>
    private void ArmarAtraso(Alquiler alquiler)
    {
        if (alquiler.Estado != EstadoAlquiler.Activo)
        {
            EstaAtrasado = false;
            return;
        }

        var dias = FechaNegocio.Hoy.DayNumber - alquiler.FechaFin.DayNumber;
        EstaAtrasado = dias > 0;
        if (!EstaAtrasado)
            return;

        // Lo que ya corresponde cobrar de más, para que el mostrador lo sepa
        // ANTES de que el cliente aparezca con el auto.
        // A la tarifa VIGENTE (039): si el contrato se renovó a otro precio, los
        // días de más van a ese precio, no al del primer tramo.
        var extra = Math.Round(TarifaVigente * dias, 2, MidpointRounding.AwayFromZero);
        AtrasoTexto = $"El vehículo tenía que volver hace {dias} día(s). " +
                      $"Al día de hoy corresponden {extra.ToString("N2", Textos.CulturaRd)} DOP de más " +
                      $"({TarifaVigente.ToString("N2", Textos.CulturaRd)} por día). " +
                      "Si el cliente lo trae, cerrá el contrato y el vehículo vuelve al inventario; " +
                      "si sigue con él, renovalo con la fecha nueva.";
    }

    [RelayCommand]
    private void Volver() => VolverSolicitado?.Invoke();

    /// <summary>
    /// Cierra el alquiler. UN solo botón para las dos formas de terminar; el
    /// diálogo pregunta cuál es, porque para la plata no significan lo mismo.
    /// </summary>
    [RelayCommand]
    private async Task CerrarAsync()
    {
        if (CierreSolicitado is null || !EstaActivo)
            return;

        try
        {
            var datos = CierreSolicitado(new CierreAlquilerPedido(
                _alquilerId, Codigo, VehiculoDescripcion, _fechaInicio, TarifaVigente, Dias, MontoTotal));
            if (datos is null)
                return;   // se arrepintió

            var r = await _alquileres.CerrarAsync(datos);
            await CargarAsync(_alquilerId);

            var mensaje = r.Tipo == CierreAlquiler.Cancelado
                ? $"El alquiler {r.Codigo} quedó cancelado y el vehículo volvió al inventario."
                : $"El alquiler {r.Codigo} quedó cerrado y el vehículo volvió al inventario.\n\n" +
                  $"Días usados: {r.DiasReales} (pactados {r.DiasPactados}).\n" +
                  $"Corresponde cobrar: {r.MontoFinal.ToString("N2", Textos.CulturaRd)} DOP" +
                  (r.Diferencia == 0m
                      ? "."
                      : r.DevolvioTarde
                          ? $" — {r.Diferencia.ToString("N2", Textos.CulturaRd)} DOP más de lo pactado."
                          : $" — {(-r.Diferencia).ToString("N2", Textos.CulturaRd)} DOP menos de lo pactado.");
            _dialogos.Informar("Alquiler cerrado", mensaje);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            _dialogos.MostrarError("Cerrar alquiler", ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cerrando el alquiler {Id}", _alquilerId);
            _dialogos.MostrarError("Cerrar alquiler", $"No se pudo cerrar el alquiler.\n\n{ex.Message}");
        }
    }

    /// <summary>
    /// El cliente sigue con el auto (039): corre la fecha de devolución, al
    /// mismo precio o a uno nuevo.
    ///
    /// Es la otra mitad de lo que pasa al cumplirse el plazo. La primera —el
    /// auto vuelve y queda disponible— ya la hacía Cerrar.
    /// </summary>
    [RelayCommand]
    private async Task RenovarAsync()
    {
        if (RenovacionSolicitada is null || !EstaActivo)
            return;

        try
        {
            var alquiler = await _alquileres.ObtenerPorIdAsync(_alquilerId);
            if (alquiler is null)
            {
                _dialogos.MostrarError("Renovar alquiler", "El alquiler ya no existe.");
                return;
            }

            var datos = RenovacionSolicitada(new RenovacionAlquilerPedido(
                _alquilerId, Codigo, VehiculoDescripcion, ClienteNombre,
                alquiler.FechaFin, TarifaVigente, CalcularTramo));
            if (datos is null)
                return;   // se arrepintió

            var r = await _alquileres.RenovarAsync(datos);
            await CargarAsync(_alquilerId);

            var tarifa = r.CambioLaTarifa
                ? $"a {r.TarifaDia.ToString("N2", Textos.CulturaRd)} DOP por día (tarifa nueva)"
                : $"a la misma tarifa de {r.TarifaDia.ToString("N2", Textos.CulturaRd)} DOP por día";
            _dialogos.Informar("Alquiler renovado",
                $"El alquiler {r.Codigo} sigue hasta el " +
                $"{r.FechaFinNueva.ToString(Textos.FormatoFecha, Textos.CulturaRd)}.\n\n" +
                $"Se agregaron {r.DiasAgregados} día(s) {tarifa}: " +
                $"{r.MontoAgregado.ToString("N2", Textos.CulturaRd)} DOP.\n" +
                $"El contrato queda en {r.DiasTotales} día(s) por " +
                $"{r.MontoTotal.ToString("N2", Textos.CulturaRd)} DOP.");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            _dialogos.MostrarError("Renovar alquiler", ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error renovando el alquiler {Id}", _alquilerId);
            _dialogos.MostrarError("Renovar alquiler", $"No se pudo renovar el alquiler.\n\n{ex.Message}");
        }
    }

    /// <summary>
    /// Días y monto del tramo nuevo, para la vista previa del diálogo. Cuenta
    /// desde el día SIGUIENTE al fin actual, igual que el servicio: el último
    /// día pactado ya está cobrado en el tramo anterior.
    /// </summary>
    private static (int Dias, decimal Monto) CalcularTramo(DateOnly finActual, DateOnly finNuevo,
        decimal tarifa)
    {
        var dias = Math.Max(0, finNuevo.DayNumber - finActual.DayNumber);
        return (dias, Math.Round(tarifa * dias, 2, MidpointRounding.AwayFromZero));
    }

    /// <summary>Los tramos del contrato y cuál tarifa rige hoy.</summary>
    private async Task CargarRenovacionesAsync(Alquiler alquiler)
    {
        var renovaciones = await _alquileres.ObtenerRenovacionesAsync(alquiler.Id);

        Renovaciones.Clear();
        foreach (var r in renovaciones)
            Renovaciones.Add(new RenovacionFila(r));
        HayRenovaciones = Renovaciones.Count > 0;

        TarifaVigente = AlquilerService.TarifaVigente(alquiler, renovaciones);
        TarifaCambio = TarifaVigente != alquiler.TarifaDia;
    }

    /// <summary>Corrige errores de digitación mientras el contrato sigue abierto.</summary>
    [RelayCommand]
    private async Task EditarAsync()
    {
        if (EdicionSolicitada is null)
            return;

        try
        {
            var alquiler = await _alquileres.ObtenerPorIdAsync(_alquilerId);
            if (alquiler is null)
            {
                _dialogos.MostrarError("Corregir alquiler", "El alquiler ya no existe.");
                return;
            }

            var cambios = EdicionSolicitada(new AlquilerParaEditar(
                _alquilerId, Codigo, alquiler, CalcularTotal));
            if (cambios is null)
                return;

            await _alquileres.EditarAsync(cambios);
            await CargarAsync(_alquilerId);
            _dialogos.Informar("Alquiler corregido",
                $"El alquiler {Codigo} quedó corregido: {Dias} día(s) por " +
                $"{MontoTotal.ToString("N2", Textos.CulturaRd)} DOP.");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            _dialogos.MostrarError("Corregir alquiler", ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error corrigiendo el alquiler {Id}", _alquilerId);
            _dialogos.MostrarError("Corregir alquiler", $"No se pudo corregir el alquiler.\n\n{ex.Message}");
        }
    }

    /// <summary>
    /// Días y total con lo que el usuario está tipeando, para la vista previa
    /// del formulario. Usa la MISMA cuenta que persiste el servicio, así lo que
    /// se ve es lo que se guarda.
    /// </summary>
    private static (int Dias, decimal Total) CalcularTotal(DateOnly inicio, DateOnly fin, decimal tarifa)
    {
        var dias = AlquilerService.CalcularDias(inicio, fin);
        return (dias, Math.Round(tarifa * dias, 2, MidpointRounding.AwayFromZero));
    }

    /// <summary>Relee los cobros y los totales.</summary>
    private async Task CargarCobrosAsync()
    {
        var estado = await _alquileres.ObtenerEstadoCobroAsync(_alquilerId);

        MontoACobrar = estado.MontoACobrar;
        Cobrado = estado.Cobrado;
        Pendiente = estado.Pendiente;
        EstaSaldado = estado.EstaSaldado;

        // Cobrado de mas: pasa cuando el contrato se cierra por menos dias de
        // los pactados y el cliente ya habia pagado el total. No se toca la
        // plata sola: se avisa y el dueño decide.
        HaySaldoAFavor = estado.SaldoAFavor > 0m;
        SaldoAFavorTexto = HaySaldoAFavor
            ? $"El cliente pagó {estado.SaldoAFavor.ToString("N2", Textos.CulturaRd)} DOP de más. " +
              "Queda a su favor: acordá con él si se le devuelve o se le descuenta del próximo alquiler."
            : string.Empty;

        Cobros.Clear();
        foreach (var p in estado.Pagos)
            Cobros.Add(new AlquilerPagoFila(p));
        SinCobros = Cobros.Count == 0;

        var hoy = FechaNegocio.Hoy;
        Calendario.Clear();
        foreach (var c in estado.Calendario)
            Calendario.Add(new CuotaAlquilerFila(c, hoy));
        HayCalendario = Calendario.Count > 0;

        OnPropertyChanged(nameof(PuedeCobrar));
        OnPropertyChanged(nameof(MostrarSaldado));
    }

    /// <summary>
    /// Comprobante fiscal del cobro (pedido 2026-08-24). Mismo par que en
    /// PrestControl: o se pega el e-NCF del Facturador Gratuito de la DGII, o
    /// el switch toma el siguiente de la secuencia de ESTA estancia (030).
    /// </summary>
    [ObservableProperty] private string _ncfTexto = string.Empty;
    [ObservableProperty] private bool _ncfDeSecuencia;

    /// <summary>Al prender el switch el NCF escrito se borra: el servicio lo ignora.</summary>
    partial void OnNcfDeSecuenciaChanged(bool value)
    {
        if (value)
            NcfTexto = string.Empty;
    }

    /// <summary>Registra un cobro contra el alquiler.</summary>
    [RelayCommand]
    private async Task CobrarAsync()
    {
        if (!decimal.TryParse(MontoCobroTexto, NumberStyles.Number, Textos.CulturaRd, out var monto)
            || monto <= 0m)
        {
            _dialogos.MostrarError("Cobrar", "Escribí cuánto está pagando el cliente.");
            return;
        }

        try
        {
            var pago = await _alquileres.RegistrarCobroAsync(new CobroAlquiler(
                _alquilerId, monto, MetodoCobro.Valor,
                string.IsNullOrWhiteSpace(NotaCobro) ? null : NotaCobro.Trim(),
                Ncf: string.IsNullOrWhiteSpace(NcfTexto) ? null : NcfTexto.Trim(),
                AsignarNcfAuto: NcfDeSecuencia));

            MontoCobroTexto = string.Empty;
            NotaCobro = string.Empty;
            // El comprobante se limpia: un NCF se consume una sola vez.
            NcfTexto = string.Empty;
            NcfDeSecuencia = false;
            await CargarCobrosAsync();

            _dialogos.Informar("Cobro registrado",
                $"Recibo {pago.NumeroRecibo} por {pago.Monto.ToString("N2", Textos.CulturaRd)} DOP." +
                (pago.Ncf is null ? "" : $"\nComprobante fiscal {pago.Ncf}.") +
                (EstaSaldado
                    ? "\n\nEl alquiler quedó saldado."
                    : $"\n\nQuedan {Pendiente.ToString("N2", Textos.CulturaRd)} DOP por cobrar."));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or UnauthorizedAccessException)
        {
            _dialogos.MostrarError("Cobrar", ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cobrando el alquiler {Id}", _alquilerId);
            _dialogos.MostrarError("Cobrar", $"No se pudo registrar el cobro.\n\n{ex.Message}");
        }
    }

}

/// <summary>Fila del calendario mensual del alquiler (037).</summary>
public record CuotaAlquilerFila(CuotaAlquiler Cuota, DateOnly Hoy)
{
    public int Numero => Cuota.Numero;
    public string PeriodoTexto =>
        $"{Cuota.Desde.ToString(Textos.FormatoFecha, Textos.CulturaRd)} → " +
        $"{Cuota.Hasta.ToString(Textos.FormatoFecha, Textos.CulturaRd)}";
    public int Dias => Cuota.Dias;
    public decimal Monto => Cuota.Monto;
    public decimal Pagado => Cuota.Pagado;
    public decimal Pendiente => Cuota.Pendiente;
    public bool EstaPagada => Cuota.EstaPagada;
    public bool EstaAtrasada => Cuota.EstaAtrasada(Hoy);
    public string EstadoTexto => Cuota.EstaPagada
        ? "Pagado"
        : EstaAtrasada ? "Atrasado" : "Pendiente";
}

/// <summary>Fila del historial de cobros del alquiler (034).</summary>
public record AlquilerPagoFila(AlquilerPago Pago)
{
    public string NumeroRecibo => Pago.NumeroRecibo;
    public string FechaTexto =>
        FechaNegocio.AUtcLocal(Pago.FechaPagoUtc).ToString(Textos.FormatoFecha, Textos.CulturaRd);
    public decimal Monto => Pago.Monto;
    public string MetodoTexto => Textos.De(Pago.MetodoPago);
    public string NotasTexto => string.IsNullOrWhiteSpace(Pago.Notas) ? "—" : Pago.Notas!;
    /// <summary>Comprobante fiscal del cobro (042). "—" cuando no lleva.</summary>
    public string NcfTexto => string.IsNullOrWhiteSpace(Pago.Ncf) ? "—" : Pago.Ncf!;
    public string CobradoPorTexto => string.IsNullOrWhiteSpace(Pago.CobradoPor) ? "—" : Pago.CobradoPor!;
}

/// <summary>Lo que el diálogo de cierre necesita mostrar.</summary>
public record CierreAlquilerPedido(
    long AlquilerId,
    string Codigo,
    string VehiculoDescripcion,
    /// <summary>Desde acá se cuentan los días reales si devolvió tarde o antes.</summary>
    DateOnly FechaInicio,
    decimal TarifaDia,
    int DiasPactados,
    decimal MontoPactado);

/// <summary>
/// Lo que el diálogo de corrección necesita. Incluye el cálculo de días y total
/// porque la capa de Views no referencia Services (lo impide el grafo de
/// proyectos, a propósito).
/// </summary>
public record AlquilerParaEditar(
    long AlquilerId,
    string Codigo,
    Alquiler Actual,
    Func<DateOnly, DateOnly, decimal, (int Dias, decimal Total)> Calcular);

/// <summary>Fila de la lista de renovaciones (039): un tramo del contrato.</summary>
public record RenovacionFila(AlquilerRenovacion Renovacion)
{
    public string PeriodoTexto =>
        $"{Renovacion.FechaFinAnterior.ToString(Textos.FormatoFecha, Textos.CulturaRd)} → " +
        $"{Renovacion.FechaFinNueva.ToString(Textos.FormatoFecha, Textos.CulturaRd)}";
    public int Dias => Renovacion.Dias;
    public decimal TarifaDia => Renovacion.TarifaDia;
    public decimal Monto => Renovacion.Monto;
    public string Notas => string.IsNullOrWhiteSpace(Renovacion.Notas) ? "—" : Renovacion.Notas!;
    public string RegistradoPor => string.IsNullOrWhiteSpace(Renovacion.CreadoPorNombre)
        ? "—" : Renovacion.CreadoPorNombre!;
    public string CuandoTexto =>
        FechaNegocio.AUtcLocal(Renovacion.CreatedAtUtc).ToString(Textos.FormatoFecha, Textos.CulturaRd);
}

/// <summary>
/// Lo que el diálogo de renovación necesita (039). Incluye el cálculo del tramo
/// porque Views no referencia Services: la vista previa tiene que dar el mismo
/// número que después persiste el servicio.
/// </summary>
public record RenovacionAlquilerPedido(
    long AlquilerId,
    string Codigo,
    string VehiculoDescripcion,
    string ClienteNombre,
    /// <summary>Hasta cuándo va el contrato hoy: la fecha nueva tiene que ser posterior.</summary>
    DateOnly FechaFinActual,
    /// <summary>Tarifa vigente. El diálogo la propone tal cual: renovar al mismo precio es lo normal.</summary>
    decimal TarifaVigente,
    Func<DateOnly, DateOnly, decimal, (int Dias, decimal Monto)> Calcular);
