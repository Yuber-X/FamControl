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
/// Wizard "Nuevo préstamo": formulario + vista previa EN VIVO de la tabla de
/// amortización. Cada cambio de campo recalcula el preview; al guardar se
/// persiste exactamente la tabla mostrada (transacción atómica en el Service).
/// </summary>
public partial class PrestamoNuevoViewModel : ObservableObject
{
    private static readonly CultureInfo CulturaRd = CultureInfo.GetCultureInfo("es-DO");

    private readonly PrestamoService _prestamos;
    private readonly ClienteService _clientes;
    private readonly AmortizacionService _amortizacion;
    private readonly IDialogService _dialogos;
    private readonly IAutorizadorAdmin _autorizador;
    private readonly AjustesLocales _ajustes;

    public event Action<long>? PrestamoCreado;
    /// <summary>La App abre la vista previa imprimible del pagaré.</summary>
    public event Action<PagareImpreso>? PagareSolicitado;

    public PrestamoNuevoViewModel(PrestamoService prestamos, ClienteService clientes,
        AmortizacionService amortizacion, IDialogService dialogos, IAutorizadorAdmin autorizador,
        AjustesLocales ajustes)
    {
        _autorizador = autorizador;
        _ajustes = ajustes;
        _prestamos = prestamos;
        _clientes = clientes;
        _amortizacion = amortizacion;
        _dialogos = dialogos;

        Modalidades =
        [
            new Opcion<Modalidad>(Modalidad.Mensual, Textos.De(Modalidad.Mensual)),
            new Opcion<Modalidad>(Modalidad.Quincenal, Textos.De(Modalidad.Quincenal)),
            new Opcion<Modalidad>(Modalidad.Semanal, Textos.De(Modalidad.Semanal)),
            new Opcion<Modalidad>(Modalidad.Diaria, Textos.De(Modalidad.Diaria)),
            new Opcion<Modalidad>(Modalidad.PagoUnico, Textos.De(Modalidad.PagoUnico))
        ];
        Metodos =
        [
            new Opcion<MetodoAmortizacion>(MetodoAmortizacion.CuotaFija, Textos.De(MetodoAmortizacion.CuotaFija)),
            new Opcion<MetodoAmortizacion>(MetodoAmortizacion.Frances, Textos.De(MetodoAmortizacion.Frances)),
            // Préstamo abierto: solo interés y el capital queda abierto. Es la
            // mitad de la cartera real del cliente (2026-07-29).
            new Opcion<MetodoAmortizacion>(MetodoAmortizacion.SoloInteres, Textos.De(MetodoAmortizacion.SoloInteres)),
            // Interés fijo unos meses y después también capital (2026-08-06):
            // la forma de prestar de "los clientes viejos", que no estaba en el
            // sistema y por eso no se les podía ni imprimir la cotización.
            new Opcion<MetodoAmortizacion>(MetodoAmortizacion.CapitalDiferido,
                Textos.De(MetodoAmortizacion.CapitalDiferido))
        ];
        _modalidadSeleccionada = Modalidades[0];
        _metodoSeleccionado = Metodos[0];
        _fechaPrimerPago = FechaNegocio.Hoy.AddMonths(1).ToDateTime(TimeOnly.MinValue);
    }

    // ---------- Formulario ----------

    public ObservableCollection<Cliente> Clientes { get; } = [];
    public IReadOnlyList<Opcion<Modalidad>> Modalidades { get; }
    public IReadOnlyList<Opcion<MetodoAmortizacion>> Metodos { get; }

    // ---------- AutoControl: crédito vehicular ----------
    /// <summary>Lo fija el shell: en AutoControl el préstamo financia un vehículo (picker + garantía).</summary>
    [ObservableProperty] private bool _esVehicular;
    /// <summary>Vehículos disponibles para financiar (solo se cargan en modo vehicular).</summary>
    public ObservableCollection<VehiculoResumen> VehiculosDisponibles { get; } = [];

    [ObservableProperty] private VehiculoResumen? _vehiculoSeleccionado;

    partial void OnVehiculoSeleccionadoChanged(VehiculoResumen? value)
    {
        if (value is not null)
        {
            // Sugiere el precio del vehículo como monto a financiar y su descripción como garantía.
            if (string.IsNullOrWhiteSpace(MontoTexto))
                MontoTexto = value.PrecioVenta.ToString("0.##", CulturaRd);
            Garantia = $"Vehículo {value.Codigo} — {value.Descripcion}";
        }
        NotificarComandos();
    }

    [ObservableProperty] private Cliente? _clienteSeleccionado;
    [ObservableProperty] private string _montoTexto = string.Empty;
    [ObservableProperty] private string _tasaTexto = string.Empty;
    [ObservableProperty] private string _plazoTexto = string.Empty;
    [ObservableProperty] private Opcion<Modalidad> _modalidadSeleccionada;
    [ObservableProperty] private Opcion<MetodoAmortizacion> _metodoSeleccionado;
    [ObservableProperty] private DateTime _fechaPrimerPago;
    [ObservableProperty] private string _garantia = string.Empty;
    [ObservableProperty] private string _notas = string.Empty;
    // Comprobante fiscal (pedido 2026-07-25): pegado del Facturador Gratuito
    // DGII, o tomado de la secuencia local configurada en Configuración.
    [ObservableProperty] private string _ncfTexto = string.Empty;
    [ObservableProperty] private bool _ncfDeSecuencia;

    // Préstamo ANTIGUO (pedido 2026-07-25): con fecha atrasada se autodetectan
    // las cuotas ya vencidas y se pregunta si el cliente está al día o cuántas pagó.
    [ObservableProperty] private bool _esPrestamoAntiguo;
    [ObservableProperty] private string _prestamoAntiguoTexto = string.Empty;
    [ObservableProperty] private bool _clienteAlDia = true;
    [ObservableProperty] private string _cuotasPagadasTexto = string.Empty;
    /// <summary>Cuotas que ya estarían vencidas si se crea hoy (según el preview).</summary>
    private int _cuotasVencidasAlCrear;

    // ---------- Modo "para bobos" (cliente 2026-07-17) ----------
    // En vez de la tasa, el usuario escribe cuánto le van a devolver y el
    // sistema calcula la tasa. Útil para quien no piensa en porcentajes.
    [ObservableProperty] private bool _modoMontoFinal;
    [ObservableProperty] private string _montoFinalTexto = string.Empty;
    /// <summary>Tasa calculada a partir del monto final: se muestra como pista.</summary>
    [ObservableProperty] private string _tasaCalculadaTexto = string.Empty;

    /// <summary>Pago único: sin plazo ni método (siempre una cuota).</summary>
    public bool EsPagoUnico => ModalidadSeleccionada?.Valor == Modalidad.PagoUnico;
    /// <summary>El plazo y el método solo aplican a préstamos de varias cuotas.</summary>
    public bool MuestraPlazoYMetodo => !EsPagoUnico;
    /// <summary>La tasa se escribe a mano solo cuando NO está el modo "para bobos".</summary>
    public bool MuestraTasaManual => !ModoMontoFinal;
    public string EtiquetaFecha => EsPagoUnico ? "Fecha del pago" : "Fecha del primer pago";

    /// <summary>
    /// Explica en una línea el método elegido. El abierto lo necesita más que los
    /// otros dos: hay que dejar claro que la cantidad de cuotas es un horizonte,
    /// no una fecha en la que el cliente esté obligado a saldar.
    /// </summary>
    public string MetodoAyudaTexto => MetodoSeleccionado?.Valor switch
    {
        MetodoAmortizacion.CuotaFija =>
            "El interés se calcula sobre el monto prestado y se reparte parejo en todas las cuotas.",
        MetodoAmortizacion.Frances =>
            "Cuota siempre igual: al principio se paga más interés y menos capital.",
        MetodoAmortizacion.SoloInteres =>
            "Préstamo abierto: cada cuota es SOLO el interés y el capital queda abierto. " +
            "La cantidad de cuotas es hasta dónde se proyecta; el capital entero aparece en la " +
            "última. Si el cliente sigue pagando interés, se renueva.",
        MetodoAmortizacion.CapitalDiferido =>
            "Las primeras cuotas son solo interés. Desde la cuota que elijas se agrega un abono " +
            "a capital fijo y el interés empieza a bajar, así que la cuota va bajando mes a mes " +
            "hasta saldar todo.",
        _ => string.Empty
    };

    // ---------- Método diferido: dónde arranca el capital ----------

    /// <summary>El campo de la cuota de inicio solo aparece con el método diferido.</summary>
    public bool EsCapitalDiferido =>
        MetodoSeleccionado?.Valor == MetodoAmortizacion.CapitalDiferido && !EsPagoUnico;

    /// <summary>
    /// Modo automático (marcado por defecto): el sistema propone un tercio del
    /// plazo como gracia. Al desmarcarlo el usuario escribe la cuota exacta, que
    /// es el "modo manual" que pidió el cliente.
    /// </summary>
    [ObservableProperty] private bool _inicioCapitalAutomatico = true;
    [ObservableProperty] private string _inicioCapitalTexto = string.Empty;

    /// <summary>
    /// El otro RadioButton del par. Existe como propiedad propia porque un
    /// RadioButton necesita escribir en su binding al hacer clic: con un
    /// converter de solo lectura el clic no llegaría al ViewModel.
    /// </summary>
    public bool InicioCapitalManual
    {
        get => !InicioCapitalAutomatico;
        set => InicioCapitalAutomatico = !value;
    }

    /// <summary>Explica en palabras lo que va a pasar, con los números del formulario.</summary>
    [ObservableProperty] private string _inicioCapitalAyuda = string.Empty;

    partial void OnInicioCapitalAutomaticoChanged(bool value)
    {
        OnPropertyChanged(nameof(InicioCapitalManual));
        // Al pasar a manual se precarga la sugerencia, así el usuario corrige un
        // número en vez de arrancar de un campo vacío.
        if (!value && string.IsNullOrWhiteSpace(InicioCapitalTexto) &&
            int.TryParse(PlazoTexto, NumberStyles.Integer, CulturaRd, out var plazo) && plazo > 0)
            InicioCapitalTexto = AmortizacionService.CuotaInicioCapitalSugerida(plazo)
                .ToString(CulturaRd);
        Recalcular();
    }

    partial void OnInicioCapitalTextoChanged(string value) => Recalcular();

    /// <summary>La cuota de inicio a usar, o null si la decide el sistema.</summary>
    private int? InicioCapitalElegido =>
        InicioCapitalAutomatico
            ? null
            : int.TryParse(InicioCapitalTexto, NumberStyles.Integer, CulturaRd, out var c) ? c : null;

    private void ActualizarAyudaInicioCapital(ParametrosAmortizacion? p)
    {
        if (p is null || p.Metodo != MetodoAmortizacion.CapitalDiferido)
        {
            InicioCapitalAyuda = string.Empty;
            return;
        }

        var inicio = p.CuotaInicioCapital ?? AmortizacionService.CuotaInicioCapitalSugerida(p.PlazoCuotas);
        var gracia = inicio - 1;
        InicioCapitalAyuda = gracia <= 0
            ? "Se cobra capital desde la primera cuota: no hay meses de solo interés."
            : $"Cuotas 1 a {gracia}: solo interés. Desde la {inicio} se agrega el abono a capital " +
              $"({p.PlazoCuotas - gracia} cuotas para saldar).";
    }

    // ---------- Preview ----------

    public ObservableCollection<CuotaCalculada> Preview { get; } = [];

    [ObservableProperty] private bool _tienePreview;
    [ObservableProperty] private string _mensajeValidacion = string.Empty;
    [ObservableProperty] private decimal _resumenCuota;
    [ObservableProperty] private decimal _resumenTotal;
    [ObservableProperty] private decimal _resumenInteres;
    [ObservableProperty] private decimal _resumenCapital;

    partial void OnMontoTextoChanged(string value) => Recalcular();
    partial void OnTasaTextoChanged(string value) => Recalcular();
    partial void OnPlazoTextoChanged(string value) => Recalcular();
    partial void OnMetodoSeleccionadoChanged(Opcion<MetodoAmortizacion> value)
    {
        OnPropertyChanged(nameof(MetodoAyudaTexto));
        OnPropertyChanged(nameof(EsCapitalDiferido));
        Recalcular();
    }
    partial void OnFechaPrimerPagoChanged(DateTime value) => Recalcular();
    partial void OnMontoFinalTextoChanged(string value) => Recalcular();
    partial void OnClienteSeleccionadoChanged(Cliente? value) => NotificarComandos();

    partial void OnModalidadSeleccionadaChanged(Opcion<Modalidad> value)
    {
        OnPropertyChanged(nameof(EsPagoUnico));
        OnPropertyChanged(nameof(MuestraPlazoYMetodo));
        OnPropertyChanged(nameof(EsCapitalDiferido));
        OnPropertyChanged(nameof(EtiquetaFecha));
        Recalcular();
    }

    partial void OnModoMontoFinalChanged(bool value)
    {
        OnPropertyChanged(nameof(MuestraTasaManual));
        // Al alternar, se limpia la pista para no dejar una tasa desactualizada
        TasaCalculadaTexto = string.Empty;
        Recalcular();
    }

    public async Task CargarAsync()
    {
        try
        {
            var clientes = await _clientes.ObtenerActivosAsync();
            var seleccionado = ClienteSeleccionado;
            Clientes.Clear();
            foreach (var cliente in clientes)
                Clientes.Add(cliente);
            // Conserva la selección si el cliente sigue existiendo
            ClienteSeleccionado = clientes.FirstOrDefault(c => c.Id == seleccionado?.Id);

            // AutoControl: cargar los vehículos disponibles para financiar
            if (EsVehicular)
            {
                var vehiculos = await _prestamos.ObtenerVehiculosDisponiblesAsync();
                var vehSel = VehiculoSeleccionado;
                VehiculosDisponibles.Clear();
                foreach (var v in vehiculos.Where(v => v.Estado == EstadoVehiculo.Disponible))
                    VehiculosDisponibles.Add(v);
                VehiculoSeleccionado = VehiculosDisponibles.FirstOrDefault(v => v.Id == vehSel?.Id);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cargando clientes para el nuevo préstamo");
            _dialogos.MostrarError("Nuevo préstamo", $"No se pudieron cargar los clientes.\n\n{ex.Message}");
        }
    }

    /// <summary>Preselecciona el cliente (flujo "Nuevo préstamo" desde su ficha). Llamar tras CargarAsync.</summary>
    public void PreseleccionarCliente(long clienteId) =>
        ClienteSeleccionado = Clientes.FirstOrDefault(c => c.Id == clienteId);

    /// <summary>Parsea el formulario. Devuelve null (con mensaje) si algo aún no es válido.</summary>
    private ParametrosAmortizacion? ParsearParametros(out string mensaje)
    {
        mensaje = string.Empty;

        if (string.IsNullOrWhiteSpace(MontoTexto) && string.IsNullOrWhiteSpace(TasaTexto) &&
            string.IsNullOrWhiteSpace(PlazoTexto) && string.IsNullOrWhiteSpace(MontoFinalTexto))
            return null; // formulario vacío: sin preview y sin regaño

        if (!decimal.TryParse(MontoTexto, NumberStyles.Number, CulturaRd, out var monto) || monto <= 0m)
        {
            mensaje = "Ingresá un monto válido mayor que cero (ej. 75,000).";
            return null;
        }

        var modalidad = ModalidadSeleccionada.Valor;
        var metodo = MetodoSeleccionado.Valor;

        // Pago único: siempre una sola cuota, sin plazo ni método que pedir
        var plazo = 1;
        if (modalidad != Modalidad.PagoUnico)
        {
            if (!int.TryParse(PlazoTexto, NumberStyles.Integer, CulturaRd, out plazo) || plazo <= 0)
            {
                mensaje = "Ingresá la cantidad de cuotas (ej. 12).";
                return null;
            }
            if (plazo > 1000)
            {
                mensaje = "El plazo máximo soportado es de 1,000 cuotas.";
                return null;
            }
        }

        decimal tasa;
        if (ModoMontoFinal)
        {
            // Modo "para bobos": la tasa sale del monto final que devolverá
            if (!decimal.TryParse(MontoFinalTexto, NumberStyles.Number, CulturaRd, out var montoFinal) ||
                montoFinal <= 0m)
            {
                mensaje = "Ingresá cuánto te devolverá el cliente en total (ej. 90,000).";
                return null;
            }
            if (montoFinal < monto)
            {
                mensaje = "El monto final no puede ser menor que el monto prestado.";
                return null;
            }
            try
            {
                tasa = _amortizacion.TasaMensualParaTotal(monto, montoFinal, plazo, modalidad, metodo);
                TasaCalculadaTexto = $"Tasa calculada: {tasa:0.##}% mensual";
            }
            catch (ArgumentException ex)
            {
                mensaje = ex.Message;
                return null;
            }
        }
        else
        {
            if (!decimal.TryParse(TasaTexto, NumberStyles.Number, CulturaRd, out tasa) || tasa < 0m)
            {
                mensaje = "Ingresá una tasa mensual válida (ej. 5).";
                return null;
            }
        }

        // Modo manual del método diferido: el usuario escribe la cuota donde
        // arranca el capital, así que se valida acá y con el mensaje del
        // formulario. El Service vuelve a validarlo: esto es para la UX.
        int? inicioCapital = null;
        if (metodo == MetodoAmortizacion.CapitalDiferido && modalidad != Modalidad.PagoUnico &&
            !InicioCapitalAutomatico)
        {
            if (!int.TryParse(InicioCapitalTexto, NumberStyles.Integer, CulturaRd, out var inicio))
            {
                mensaje = "Indicá en qué cuota empieza a cobrarse el capital (ej. 7).";
                return null;
            }
            if (inicio < 1 || inicio > plazo)
            {
                mensaje = $"El capital tiene que empezar entre la cuota 1 y la {plazo}.";
                return null;
            }
            inicioCapital = inicio;
        }

        return new ParametrosAmortizacion(
            monto, tasa, plazo, modalidad, metodo,
            DateOnly.FromDateTime(FechaPrimerPago), inicioCapital);
    }

    private void Recalcular()
    {
        var parametros = ParsearParametros(out var mensaje);
        MensajeValidacion = mensaje;
        Preview.Clear();
        ActualizarAyudaInicioCapital(parametros);

        if (parametros is null)
        {
            TienePreview = false;
            if (ModoMontoFinal)
                TasaCalculadaTexto = string.Empty;   // no dejar una pista vieja
            EsPrestamoAntiguo = false;
            _cuotasVencidasAlCrear = 0;
            NotificarComandos();
            return;
        }

        var tabla = _amortizacion.Calcular(parametros);
        foreach (var cuota in tabla)
            Preview.Add(cuota);

        // Autodetección de préstamo antiguo: cuotas que ya estarían vencidas hoy
        var hoy = FechaNegocio.Hoy;
        _cuotasVencidasAlCrear = tabla.Count(c => c.FechaVencimiento <= hoy);
        EsPrestamoAntiguo = _cuotasVencidasAlCrear > 0;
        PrestamoAntiguoTexto = _cuotasVencidasAlCrear == 1
            ? "Este préstamo parece ANTIGUO: 1 cuota ya estaría vencida al crearlo."
            : $"Este préstamo parece ANTIGUO: {_cuotasVencidasAlCrear} cuotas ya estarían vencidas al crearlo.";

        var resumen = _amortizacion.Resumir(tabla);
        ResumenCuota = resumen.CuotaFija;
        ResumenTotal = resumen.TotalAPagar;
        ResumenInteres = resumen.InteresTotal;
        ResumenCapital = resumen.Capital;
        TienePreview = true;
        NotificarComandos();
    }

    private bool PuedeGuardar() =>
        TienePreview && ClienteSeleccionado is not null && (!EsVehicular || VehiculoSeleccionado is not null);

    private void NotificarComandos()
    {
        GuardarCommand.NotifyCanExecuteChanged();
        VerPagareCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(PuedeGuardar))]
    private async Task GuardarAsync()
    {
        var parametros = ParsearParametros(out _);
        if (parametros is null || ClienteSeleccionado is null)
            return;

        // Préstamo antiguo: resolver cuántas cuotas nacen pagadas ANTES de crear
        var cuotasPagadasAlCrear = 0;
        if (EsPrestamoAntiguo)
        {
            if (ClienteAlDia)
            {
                cuotasPagadasAlCrear = _cuotasVencidasAlCrear;
            }
            else if (!string.IsNullOrWhiteSpace(CuotasPagadasTexto))
            {
                if (!int.TryParse(CuotasPagadasTexto, NumberStyles.Integer, CulturaRd, out var n) ||
                    n < 0 || n > parametros.PlazoCuotas)
                {
                    MensajeValidacion = $"Cuotas ya pagadas: ingresá un número entre 0 y {parametros.PlazoCuotas}.";
                    return;
                }
                cuotasPagadasAlCrear = n;
            }

            if (cuotasPagadasAlCrear > 0 && !_dialogos.Confirmar("Préstamo antiguo",
                $"Se marcarán {cuotasPagadasAlCrear} cuota(s) como PAGADAS con recibos históricos " +
                "fechados en su vencimiento (así los reportes quedan en su mes real).\n\n¿Continuar?"))
                return;
        }

        try
        {
            // Autorización ANTES de tocar la BD (regla del cliente 2026-07-16).
            // Si quien crea ya puede autorizar, el Service lo resuelve solo y
            // no se le pide su propia contraseña.
            AutorizacionPrestamo? autorizacion = null;
            if (!AutorizacionService.UsuarioActualPuedeAutorizar)
            {
                autorizacion = await _autorizador.PedirAsync(
                    $"{SesionActual.Nombre} está creando un préstamo de " +
                    $"{parametros.MontoCapital.ToString("N2", CulturaRd)} DOP para " +
                    $"{ClienteSeleccionado.NombreCompleto}. Un administrador debe autorizarlo.");

                if (autorizacion is null)
                {
                    // Cancelado o credenciales inválidas: NO se crea nada.
                    MensajeValidacion = "El préstamo no se creó: falta la autorización de un administrador.";
                    return;
                }
            }

            var solicitud = new NuevoPrestamo(
                ClienteSeleccionado.Id,
                parametros.MontoCapital,
                parametros.TasaInteresMensual,
                parametros.PlazoCuotas,
                parametros.Modalidad,
                parametros.Metodo,
                parametros.FechaPrimerPago,
                string.IsNullOrWhiteSpace(Garantia) ? null : Garantia.Trim(),
                string.IsNullOrWhiteSpace(Notas) ? null : Notas.Trim(),
                EsVehicular ? VehiculoSeleccionado?.Id : null,
                Ncf: string.IsNullOrWhiteSpace(NcfTexto) ? null : NcfTexto.Trim(),
                AsignarNcfAuto: NcfDeSecuencia,
                CuotasPagadasAlCrear: cuotasPagadasAlCrear,
                // Sale de los parámetros, no del formulario: así se guarda
                // exactamente la cuota con la que se calculó el preview.
                CuotaInicioCapital: parametros.CuotaInicioCapital);

            var (id, codigo) = await _prestamos.CrearAsync(solicitud, autorizacion);

            // El pagaré se imprime SOLO al crear (pedido del cliente 2026-07-17):
            // se abre la vista previa con el contrato listo para firmar.
            var cliente = ClienteSeleccionado;
            PagareSolicitado?.Invoke(ConstruirPagare(codigo, cliente, parametros));

            var quien = autorizacion is null
                ? string.Empty
                : $"\n\nAutorizado por {autorizacion.Nombre}.";
            _dialogos.Informar("Préstamo creado",
                $"El préstamo {codigo} de {cliente.NombreCompleto} se creó correctamente.{quien}");
            Limpiar();
            PrestamoCreado?.Invoke(id);
        }
        catch (UnauthorizedAccessException ex)
        {
            MensajeValidacion = ex.Message;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error creando el préstamo");
            _dialogos.MostrarError("Nuevo préstamo", $"No se pudo crear el préstamo.\n\n{ex.Message}");
        }
    }

    /// <summary>
    /// "Ver pagaré" en Nuevo Préstamo: previsualiza el contrato con los datos
    /// actuales ANTES de crear (código en borrador). Sirve para revisarlo con
    /// el cliente antes de firmar.
    /// </summary>
    [RelayCommand(CanExecute = nameof(PuedeGuardar))]
    private void VerPagare()
    {
        var parametros = ParsearParametros(out _);
        if (parametros is null || ClienteSeleccionado is null)
            return;
        PagareSolicitado?.Invoke(ConstruirPagare("(borrador)", ClienteSeleccionado, parametros));
    }

    /// <summary>Arma el pagaré desde el negocio (AjustesLocales), el cliente y la tabla.</summary>
    private PagareImpreso ConstruirPagare(string codigo, Cliente cliente, ParametrosAmortizacion parametros)
    {
        var tabla = _amortizacion.Calcular(parametros);
        return new PagareImpreso(
            NombreNegocio: _ajustes.NombreNegocio,
            Prestamista: _ajustes.Prestamista,
            Ciudad: _ajustes.CiudadNegocio,
            Telefono: _ajustes.TelefonoNegocio,
            Email: _ajustes.EmailNegocio,
            Rnc: _ajustes.RncNegocio,
            DeudorNombre: cliente.NombreCompleto,
            DeudorCedula: string.IsNullOrWhiteSpace(cliente.Cedula) ? "—" : cliente.Cedula,
            CodigoPrestamo: codigo,
            MontoPrestado: parametros.MontoCapital,
            TasaTexto: PagareImpreso.FormatearTasa(parametros.TasaInteresMensual, parametros.Modalidad),
            TotalAPagar: tabla.Sum(c => c.MontoTotal),
            Cuotas: [.. tabla.Select(c => new PagareCuota(
                c.NumeroCuota,
                c.FechaVencimiento.ToString(Textos.FormatoFecha, CulturaRd),
                c.MontoTotal))]);
    }

    private void Limpiar()
    {
        ClienteSeleccionado = null;
        VehiculoSeleccionado = null;
        MontoTexto = string.Empty;
        TasaTexto = string.Empty;
        PlazoTexto = string.Empty;
        MontoFinalTexto = string.Empty;
        ModoMontoFinal = false;
        TasaCalculadaTexto = string.Empty;
        ModalidadSeleccionada = Modalidades[0];
        MetodoSeleccionado = Metodos[0];
        InicioCapitalAutomatico = true;
        InicioCapitalTexto = string.Empty;
        FechaPrimerPago = FechaNegocio.Hoy.AddMonths(1).ToDateTime(TimeOnly.MinValue);
        Garantia = string.Empty;
        Notas = string.Empty;
        NcfTexto = string.Empty;
        NcfDeSecuencia = false;
        EsPrestamoAntiguo = false;
        ClienteAlDia = true;
        CuotasPagadasTexto = string.Empty;
        _cuotasVencidasAlCrear = 0;
    }
}
