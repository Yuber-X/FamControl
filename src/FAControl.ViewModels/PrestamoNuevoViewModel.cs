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
    private readonly NcfService _ncf;
    private readonly ContratoService _contratos;

    public event Action<long>? PrestamoCreado;

    /// <summary>
    /// La vista previa lateral tiene que redibujarse: cambió el documento
    /// elegido o los datos del formulario (2026-09-03).
    ///
    /// Es un evento y no una propiedad porque lo que hay que rehacer es un
    /// FlowDocument, y eso es cosa de la capa de vistas: el ViewModel no
    /// referencia a Printing.
    /// </summary>
    public event Action? VistaPreviaCambiada;

    /// <summary>
    /// Contratos para la vista: con la lista VACÍA se abre la ventana de vista
    /// previa (el usuario elige y decide); con contratos adentro se mandan
    /// directo a la impresora, que es lo que hace "Crear e imprimir".
    ///
    /// El expediente es null mientras el préstamo no exista: un borrador se
    /// puede mirar e imprimir, pero no se archiva.
    /// </summary>
    public event Action<PagareNotarialImpreso, DuenoExpediente?, IReadOnlyList<TipoContrato>>?
        ContratosParaImprimir;

    public PrestamoNuevoViewModel(PrestamoService prestamos, ClienteService clientes,
        AmortizacionService amortizacion, IDialogService dialogos, IAutorizadorAdmin autorizador,
        AjustesLocales ajustes, NcfService ncf, ContratoService contratos,
        ExpedienteViewModel expediente)
    {
        Expediente = expediente;
        _autorizador = autorizador;
        _ajustes = ajustes;
        _ncf = ncf;
        _contratos = contratos;
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
        _deudorSexo = Sexos[0];
        CargarSeleccionDeContratos();
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

    // ================= Pagaré notarial (044) =================
    // Datos que solo pide el acta notarial. TODOS son opcionales: el acta se
    // imprime igual con una raya para llenar a mano, que es como se trabaja
    // con un notario. Ninguno puede impedir que el préstamo se cree.
    //
    // Lo que se repite en todas las actas (notario, representante, testigos,
    // dirección de la empresa) NO está aquí: vive en Configuración y se carga
    // una sola vez.

    [ObservableProperty] private string _actoNo = string.Empty;
    [ObservableProperty] private string _folioNo = string.Empty;
    [ObservableProperty] private DateTime? _fechaActo;
    [ObservableProperty] private string _municipioActo = string.Empty;
    [ObservableProperty] private Opcion<SexoPersona> _deudorSexo;
    [ObservableProperty] private string _deudorNacionalidad = string.Empty;
    [ObservableProperty] private string _deudorEstadoCivil = string.Empty;
    [ObservableProperty] private string _deudorOcupacion = string.Empty;
    [ObservableProperty] private string _cuotasExigibilidadTexto = string.Empty;
    [ObservableProperty] private string _diasGraciaTexto = string.Empty;
    [ObservableProperty] private string _moraPorcentajeTexto = string.Empty;
    [ObservableProperty] private string _registroTitulos = string.Empty;

    // ---- Las partes del acta (2026-09-04) ----
    // Antes vivian SOLO en Configuracion y aca no se veian. El cliente pidio
    // tenerlas tambien en Nuevo Prestamo, precargadas desde Configuracion, para
    // poder ajustarlas sin salir de la pantalla.
    //
    // La copia va en un solo sentido salvo que se prenda GuardarNotarialEnConfiguracion:
    // escribir un testigo distinto para UN prestamo no tiene por que cambiar el
    // testigo de todos los demas.

    [ObservableProperty] private string _negocioDireccion = string.Empty;
    [ObservableProperty] private string _notarioNombre = string.Empty;
    [ObservableProperty] private string _notarioMatricula = string.Empty;
    [ObservableProperty] private string _notarioCedula = string.Empty;
    [ObservableProperty] private string _notarioEstadoCivil = string.Empty;
    [ObservableProperty] private string _notarioDomicilio = string.Empty;
    [ObservableProperty] private string _representanteNombre = string.Empty;
    [ObservableProperty] private string _representanteCedula = string.Empty;
    [ObservableProperty] private string _representanteEstadoCivil = string.Empty;
    [ObservableProperty] private string _representanteOcupacion = string.Empty;
    [ObservableProperty] private string _representanteDomicilio = string.Empty;
    [ObservableProperty] private string _testigo1Nombre = string.Empty;
    [ObservableProperty] private string _testigo1Cedula = string.Empty;
    [ObservableProperty] private string _testigo1EstadoCivil = string.Empty;
    [ObservableProperty] private string _testigo1Ocupacion = string.Empty;
    [ObservableProperty] private string _testigo1Domicilio = string.Empty;
    [ObservableProperty] private string _testigo2Nombre = string.Empty;
    [ObservableProperty] private string _testigo2Cedula = string.Empty;
    [ObservableProperty] private string _testigo2EstadoCivil = string.Empty;
    [ObservableProperty] private string _testigo2Ocupacion = string.Empty;
    [ObservableProperty] private string _testigo2Domicilio = string.Empty;
    [ObservableProperty] private bool _representanteEsFemenino;
    [ObservableProperty] private bool _testigo1EsFemenino;
    [ObservableProperty] private bool _testigo2EsFemenino;

    /// <summary>
    /// Al crear el prestamo, lo escrito arriba PISA lo que haya en
    /// Configuracion → Pagare notarial (pedido del cliente 2026-09-04).
    ///
    /// Apagado por defecto y a proposito: lo normal es corregir un dato para
    /// este contrato puntual. Prenderlo es decir "de ahora en mas, estos son
    /// los datos del negocio".
    /// </summary>
    [ObservableProperty] private bool _guardarNotarialEnConfiguracion;

    partial void OnNegocioDireccionChanged(string value) => NotarialCambio();
    partial void OnNotarioNombreChanged(string value) => NotarialCambio();
    partial void OnNotarioMatriculaChanged(string value) => NotarialCambio();
    partial void OnNotarioCedulaChanged(string value) => NotarialCambio();
    partial void OnNotarioEstadoCivilChanged(string value) => NotarialCambio();
    partial void OnNotarioDomicilioChanged(string value) => NotarialCambio();
    partial void OnRepresentanteNombreChanged(string value) => NotarialCambio();
    partial void OnRepresentanteCedulaChanged(string value) => NotarialCambio();
    partial void OnRepresentanteEstadoCivilChanged(string value) => NotarialCambio();
    partial void OnRepresentanteOcupacionChanged(string value) => NotarialCambio();
    partial void OnRepresentanteDomicilioChanged(string value) => NotarialCambio();
    partial void OnTestigo1NombreChanged(string value) => NotarialCambio();
    partial void OnTestigo1CedulaChanged(string value) => NotarialCambio();
    partial void OnTestigo1EstadoCivilChanged(string value) => NotarialCambio();
    partial void OnTestigo1OcupacionChanged(string value) => NotarialCambio();
    partial void OnTestigo1DomicilioChanged(string value) => NotarialCambio();
    partial void OnTestigo2NombreChanged(string value) => NotarialCambio();
    partial void OnTestigo2CedulaChanged(string value) => NotarialCambio();
    partial void OnTestigo2EstadoCivilChanged(string value) => NotarialCambio();
    partial void OnTestigo2OcupacionChanged(string value) => NotarialCambio();
    partial void OnTestigo2DomicilioChanged(string value) => NotarialCambio();
    partial void OnRepresentanteEsFemeninoChanged(bool value) => NotarialCambio();
    partial void OnTestigo1EsFemeninoChanged(bool value) => NotarialCambio();
    partial void OnTestigo2EsFemeninoChanged(bool value) => NotarialCambio();

    /// <summary>
    /// Cambio un dato del acta: si el panel lateral esta mostrando un contrato,
    /// hay que redibujarlo para que se vea lo que se acaba de escribir.
    /// </summary>
    private void NotarialCambio()
    {
        if (VistaPreviaTipo is not null)
            VistaPreviaCambiada?.Invoke();
    }

    /// <summary>
    /// Trae las partes del acta desde Configuracion. Se llama al entrar a la
    /// pantalla y despues de crear un prestamo: asi el formulario siempre
    /// arranca con los datos vigentes del negocio.
    /// </summary>
    private void CargarNotarialDesdeConfiguracion()
    {
        var acta = _contratos.DesdeConfiguracion();

        NegocioDireccion = acta.EmpresaDireccion;
        if (string.IsNullOrWhiteSpace(MunicipioActo))
            MunicipioActo = acta.Municipio;

        NotarioNombre = acta.Notario.Nombre;
        NotarioMatricula = acta.NotarioMatricula;
        NotarioCedula = acta.Notario.Cedula;
        NotarioEstadoCivil = acta.Notario.EstadoCivil;
        NotarioDomicilio = acta.Notario.Domicilio;

        RepresentanteNombre = acta.Representante.Nombre;
        RepresentanteCedula = acta.Representante.Cedula;
        RepresentanteEstadoCivil = acta.Representante.EstadoCivil;
        RepresentanteOcupacion = acta.Representante.Ocupacion;
        RepresentanteDomicilio = acta.Representante.Domicilio;
        RepresentanteEsFemenino = Genero.EsFemenino(acta.Representante.Sexo);

        var t1 = acta.Testigos.Count > 0 ? acta.Testigos[0] : new ParteDelActo("", "");
        Testigo1Nombre = t1.Nombre;
        Testigo1Cedula = t1.Cedula;
        Testigo1EstadoCivil = t1.EstadoCivil;
        Testigo1Ocupacion = t1.Ocupacion;
        Testigo1Domicilio = t1.Domicilio;
        Testigo1EsFemenino = Genero.EsFemenino(t1.Sexo);

        var t2 = acta.Testigos.Count > 1 ? acta.Testigos[1] : new ParteDelActo("", "");
        Testigo2Nombre = t2.Nombre;
        Testigo2Cedula = t2.Cedula;
        Testigo2EstadoCivil = t2.EstadoCivil;
        Testigo2Ocupacion = t2.Ocupacion;
        Testigo2Domicilio = t2.Domicilio;
        Testigo2EsFemenino = Genero.EsFemenino(t2.Sexo);

        if (string.IsNullOrWhiteSpace(CuotasExigibilidadTexto))
            CuotasExigibilidadTexto = acta.CuotasParaExigibilidad.ToString(CulturaRd);
        if (string.IsNullOrWhiteSpace(DiasGraciaTexto))
            DiasGraciaTexto = acta.DiasDeGracia.ToString(CulturaRd);
        if (string.IsNullOrWhiteSpace(MoraPorcentajeTexto))
            MoraPorcentajeTexto = acta.MoraPorcentaje.ToString("0.##", CulturaRd);
        if (string.IsNullOrWhiteSpace(RegistroTitulos))
            RegistroTitulos = acta.RegistroTitulos;
    }

    /// <summary>Las partes del acta tal como estan escritas en el formulario.</summary>
    private DatosNotariales ActoDelFormulario() => new()
    {
        Municipio = MunicipioActo.Trim(),
        EmpresaDireccion = NegocioDireccion.Trim(),

        Notario = new ParteDelActo(
            Nombre: NotarioNombre.Trim(),
            Cedula: NotarioCedula.Trim(),
            Nacionalidad: "dominicano",
            EstadoCivil: NotarioEstadoCivil.Trim(),
            Ocupacion: "abogado notario público",
            Domicilio: NotarioDomicilio.Trim()),
        NotarioMatricula = NotarioMatricula.Trim(),

        Representante = new ParteDelActo(
            Nombre: RepresentanteNombre.Trim(),
            Cedula: RepresentanteCedula.Trim(),
            Sexo: RepresentanteEsFemenino ? SexoPersona.Femenino : SexoPersona.Masculino,
            Nacionalidad: "dominicano",
            EstadoCivil: RepresentanteEstadoCivil.Trim(),
            Ocupacion: RepresentanteOcupacion.Trim(),
            Domicilio: RepresentanteDomicilio.Trim()),

        Testigos =
        [
            new ParteDelActo(Testigo1Nombre.Trim(), Testigo1Cedula.Trim(),
                Testigo1EsFemenino ? SexoPersona.Femenino : SexoPersona.Masculino, "dominicano",
                Testigo1EstadoCivil.Trim(), Testigo1Ocupacion.Trim(), Testigo1Domicilio.Trim()),
            new ParteDelActo(Testigo2Nombre.Trim(), Testigo2Cedula.Trim(),
                Testigo2EsFemenino ? SexoPersona.Femenino : SexoPersona.Masculino, "dominicano",
                Testigo2EstadoCivil.Trim(), Testigo2Ocupacion.Trim(), Testigo2Domicilio.Trim())
        ]
    };

    /// <summary>
    /// Copia las partes del acta a Configuracion. Solo corre con el interruptor
    /// prendido: sin el, un dato escrito para un contrato puntual no toca los
    /// valores generales del negocio.
    /// </summary>
    private void GuardarNotarialEnAjustes()
    {
        _ajustes.DireccionNegocio = NegocioDireccion.Trim();
        if (!string.IsNullOrWhiteSpace(MunicipioActo))
            _ajustes.MunicipioActo = MunicipioActo.Trim();

        _ajustes.NotarioNombre = NotarioNombre.Trim();
        _ajustes.NotarioMatricula = NotarioMatricula.Trim();
        _ajustes.NotarioCedula = NotarioCedula.Trim();
        _ajustes.NotarioEstadoCivil = NotarioEstadoCivil.Trim();
        _ajustes.NotarioDomicilio = NotarioDomicilio.Trim();

        _ajustes.RepresentanteNombre = RepresentanteNombre.Trim();
        _ajustes.RepresentanteCedula = RepresentanteCedula.Trim();
        _ajustes.RepresentanteEstadoCivil = RepresentanteEstadoCivil.Trim();
        _ajustes.RepresentanteOcupacion = RepresentanteOcupacion.Trim();
        _ajustes.RepresentanteDomicilio = RepresentanteDomicilio.Trim();
        _ajustes.RepresentanteSexo = RepresentanteEsFemenino ? 2 : 1;

        _ajustes.Testigo1Nombre = Testigo1Nombre.Trim();
        _ajustes.Testigo1Cedula = Testigo1Cedula.Trim();
        _ajustes.Testigo1EstadoCivil = Testigo1EstadoCivil.Trim();
        _ajustes.Testigo1Ocupacion = Testigo1Ocupacion.Trim();
        _ajustes.Testigo1Domicilio = Testigo1Domicilio.Trim();
        _ajustes.Testigo1Sexo = Testigo1EsFemenino ? 2 : 1;

        _ajustes.Testigo2Nombre = Testigo2Nombre.Trim();
        _ajustes.Testigo2Cedula = Testigo2Cedula.Trim();
        _ajustes.Testigo2EstadoCivil = Testigo2EstadoCivil.Trim();
        _ajustes.Testigo2Ocupacion = Testigo2Ocupacion.Trim();
        _ajustes.Testigo2Domicilio = Testigo2Domicilio.Trim();
        _ajustes.Testigo2Sexo = Testigo2EsFemenino ? 2 : 1;

        if (EnteroOpcional(CuotasExigibilidadTexto) is { } cuotas && cuotas > 0)
            _ajustes.CuotasParaExigibilidad = cuotas;
        if (EnteroOpcional(DiasGraciaTexto) is { } dias)
            _ajustes.DiasDeGracia = dias;
        if (DecimalOpcional(MoraPorcentajeTexto) is { } mora)
            _ajustes.MoraPorcentaje = mora;
        if (!string.IsNullOrWhiteSpace(RegistroTitulos))
            _ajustes.RegistroTitulos = RegistroTitulos.Trim();

        _ajustes.Guardar();
    }


    /// <summary>Opciones de sexo del deudor, para la concordancia del acta.</summary>
    public IReadOnlyList<Opcion<SexoPersona>> Sexos { get; } =
    [
        new(SexoPersona.NoIndicado, "Sin indicar"),
        new(SexoPersona.Masculino, "Masculino"),
        new(SexoPersona.Femenino, "Femenino")
    ];

    // ================= Qué contratos se imprimen (2026-09-03) =================
    // Los tildados se imprimen al guardar; los destildados no. La elección se
    // recuerda por PC: quién imprime qué depende de la impresora que tenga esa
    // terminal al lado.

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayContratoTildado))]
    private bool _imprimirPagare;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayContratoTildado))]
    private bool _imprimirNotarial;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayContratoTildado))]
    private bool _imprimirCombinado;

    /// <summary>
    /// Al menos un contrato tildado. Con ninguno, "Crear e imprimir" no tiene
    /// nada que imprimir y se deshabilita: dejarlo apretable haría creer que
    /// salió un papel que nunca salió.
    /// </summary>
    public bool HayContratoTildado => ImprimirPagare || ImprimirNotarial || ImprimirCombinado;

    /// <summary>Los contratos tildados, en el orden en que se imprimen.</summary>
    public IReadOnlyList<TipoContrato> ContratosTildados()
    {
        var lista = new List<TipoContrato>();
        if (ImprimirPagare) lista.Add(TipoContrato.Pagare);
        if (ImprimirNotarial) lista.Add(TipoContrato.Notarial);
        if (ImprimirCombinado) lista.Add(TipoContrato.Combinado);
        return lista;
    }

    partial void OnImprimirPagareChanged(bool value) => GuardarSeleccionDeContratos();
    partial void OnImprimirNotarialChanged(bool value) => GuardarSeleccionDeContratos();
    partial void OnImprimirCombinadoChanged(bool value) => GuardarSeleccionDeContratos();

    private void GuardarSeleccionDeContratos()
    {
        GuardarEImprimirCommand.NotifyCanExecuteChanged();
        if (_cargandoSeleccion)
            return;
        _ajustes.ContratosAImprimir = [.. ContratosTildados().Select(t => t.ToString())];
        _ajustes.Guardar();
    }

    /// <summary>Evita reescribir el ajuste mientras se lee del ajuste.</summary>
    private bool _cargandoSeleccion;

    private void CargarSeleccionDeContratos()
    {
        _cargandoSeleccion = true;
        var guardados = _ajustes.ContratosAImprimir ?? [];
        ImprimirPagare = guardados.Contains(nameof(TipoContrato.Pagare));
        ImprimirNotarial = guardados.Contains(nameof(TipoContrato.Notarial));
        ImprimirCombinado = guardados.Contains(nameof(TipoContrato.Combinado));
        _cargandoSeleccion = false;
        GuardarEImprimirCommand.NotifyCanExecuteChanged();
    }

    // ================= Vista previa lateral (2026-09-03) =================

    /// <summary>
    /// Qué se ve en el panel lateral: null = la tabla de amortización (lo de
    /// siempre), o uno de los tres contratos.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MuestraTablaLateral))]
    [NotifyPropertyChangedFor(nameof(MuestraContratoLateral))]
    [NotifyPropertyChangedFor(nameof(MuestraMetricas))]
    private TipoContrato? _vistaPreviaTipo;

    public bool MuestraTablaLateral => VistaPreviaTipo is null;
    public bool MuestraContratoLateral => VistaPreviaTipo is not null;

    partial void OnVistaPreviaTipoChanged(TipoContrato? value) => VistaPreviaCambiada?.Invoke();

    /// <summary>
    /// Cambia el documento del panel lateral. Volver a apretar el mismo botón
    /// regresa a la tabla de amortización, que es lo que la pantalla mostraba
    /// antes y sigue siendo lo que más se mira.
    /// </summary>
    [RelayCommand]
    private void VerEnLateral(TipoContrato tipo) =>
        VistaPreviaTipo = VistaPreviaTipo == tipo ? null : tipo;

    /// <summary>
    /// El borrador de los contratos con lo que hay escrito ahora mismo, o null
    /// si el formulario todavía no da para armarlo. Lo pide la vista para
    /// dibujar el panel lateral y la ventana de vista previa.
    /// </summary>
    public PagareNotarialImpreso? ContratoBorrador()
    {
        var parametros = ParsearParametros(out _);
        if (parametros is null || ClienteSeleccionado is null)
            return null;
        return ConstruirContrato("(borrador)", ClienteSeleccionado, parametros);
    }
    // Comprobante fiscal (pedido 2026-07-25): pegado del Facturador Gratuito
    // DGII, o tomado de la secuencia local configurada en Configuración.
    [ObservableProperty] private string _ncfTexto = string.Empty;
    /// <summary>
    /// Proximo comprobante que entregaria la secuencia, para mostrarlo como
    /// marcador dentro de la caja de NCF (pedido del cliente 2026-09-03).
    /// Cadena vacia cuando esta estancia no tiene secuencia configurada, esta
    /// apagada, vencio o se agoto: en ese caso la caja no muestra marcador.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayNcfConfigurado))]
    [NotifyPropertyChangedFor(nameof(NcfSwitchTexto))]
    private string _ncfMarcador = string.Empty;

    /// <summary>
    /// Hay una secuencia de la que tomar el proximo comprobante. Cuando es
    /// false el switch se apaga Y se deshabilita: prenderlo solo conseguiria
    /// que la operacion fallara al final con "esta estancia no tiene secuencia
    /// configurada", que es un error que conviene no dejar llegar tan tarde.
    /// </summary>
    public bool HayNcfConfigurado => NcfMarcador.Length > 0;

    /// <summary>Texto del switch: nombra el numero exacto que se va a usar.</summary>
    public string NcfSwitchTexto => HayNcfConfigurado
        ? $"Usar el comprobante {NcfMarcador}"
        : "No hay secuencia de comprobantes configurada (Configuración → Comprobante fiscal)";

    /// <summary>
    /// Si la secuencia dejo de estar disponible (se apago, vencio, se agoto o
    /// se cambio de estancia), el switch no puede quedar prendido apuntando a
    /// un talonario que ya no existe.
    /// </summary>
    partial void OnNcfMarcadorChanged(string value)
    {
        if (value.Length == 0)
            NcfDeSecuencia = false;
    }

    [ObservableProperty] private bool _ncfDeSecuencia;

    /// <summary>
    /// Al prender el switch, el NCF escrito a mano se borra. El servicio ya lo
    /// ignora (AsignarNcfAuto manda), asi que dejarlo a la vista haria creer
    /// que se va a usar ese numero. Mismo criterio que pidio el cliente el
    /// 2026-08-02 para la secuencia de Configuracion.
    /// </summary>
    partial void OnNcfDeSecuenciaChanged(bool value)
    {
        if (value)
            NcfTexto = string.Empty;
    }


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

    /// <summary>
    /// Expediente donde se archiva lo que se imprima (2026-09-03). La vista se
    /// lo pasa a la impresión; aquí no se usa para nada más.
    /// </summary>
    public ExpedienteViewModel Expediente { get; }

    public ObservableCollection<CuotaCalculada> Preview { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MuestraMetricas))]
    private bool _tienePreview;

    /// <summary>
    /// Las tarjetas de cuota/total/interés/capital solo tienen sentido junto a
    /// la tabla Y con datos: sin formulario mostrarían cuatro ceros, y con un
    /// contrato en pantalla no vienen al caso.
    /// </summary>
    public bool MuestraMetricas => MuestraTablaLateral && TienePreview;
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

            NcfMarcador = await _ncf.ProximoNcfAsync() ?? string.Empty;
            CargarNotarialDesdeConfiguracion();
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
            mensaje = "Ingresa un monto válido mayor que cero (ej. 75,000).";
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
                mensaje = "Ingresa la cantidad de cuotas (ej. 12).";
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
                mensaje = "Ingresa cuánto te devolverá el cliente en total (ej. 90,000).";
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
                mensaje = "Ingresa una tasa mensual válida (ej. 5).";
                return null;
            }
        }

        // Modo manual del método diferido: el usuario escribe la cuota donde
        // arranca el capital, así que se valida aquí y con el mensaje del
        // formulario. El Service vuelve a validarlo: esto es para la UX.
        int? inicioCapital = null;
        if (metodo == MetodoAmortizacion.CapitalDiferido && modalidad != Modalidad.PagoUnico &&
            !InicioCapitalAutomatico)
        {
            if (!int.TryParse(InicioCapitalTexto, NumberStyles.Integer, CulturaRd, out var inicio))
            {
                mensaje = "Indica en qué cuota empieza a cobrarse el capital (ej. 7).";
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

        // Si el panel lateral está mostrando un contrato, hay que rehacerlo:
        // cambió el monto, el plazo o la tasa y el documento quedó viejo.
        if (VistaPreviaTipo is not null)
            VistaPreviaCambiada?.Invoke();

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
        GuardarEImprimirCommand.NotifyCanExecuteChanged();
        VerPagareCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Se puede guardar E imprimir: hace falta lo mismo que para guardar, más
    /// al menos un contrato tildado. Sin ninguno no hay nada que imprimir, y
    /// dejar el botón apretable haría creer que salió un papel que nunca salió.
    /// </summary>
    private bool PuedeGuardarEImprimir() => PuedeGuardar() && HayContratoTildado;

    /// <summary>
    /// "Crear préstamo": guarda y nada más (pedido del cliente 2026-09-03).
    ///
    /// Hasta el 2026-09-02 este botón abría además la vista previa del pagaré.
    /// Ahora imprimir es una decisión aparte, con su propio botón y sus tildes.
    /// </summary>
    [RelayCommand(CanExecute = nameof(PuedeGuardar))]
    private Task GuardarAsync() => CrearAsync(imprimir: false);

    /// <summary>
    /// "Crear e imprimir": crea el préstamo y manda a la impresora los
    /// contratos tildados, en orden. Lo impreso queda archivado en el
    /// expediente del préstamo.
    /// </summary>
    [RelayCommand(CanExecute = nameof(PuedeGuardarEImprimir))]
    private Task GuardarEImprimirAsync() => CrearAsync(imprimir: true);

    private async Task CrearAsync(bool imprimir)
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
                    MensajeValidacion = $"Cuotas ya pagadas: ingresa un número entre 0 y {parametros.PlazoCuotas}.";
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
                CuotaInicioCapital: parametros.CuotaInicioCapital,
                Notarial: DatosNotarialesDelFormulario());

            var (id, codigo) = await _prestamos.CrearAsync(solicitud, autorizacion);

            // Los contratos salen SOLO si se apretó "Crear e imprimir" y hay
            // alguno tildado. Se arman con el código real ya asignado, no con
            // "(borrador)", y la vista los archiva en el expediente al imprimir.
            var cliente = ClienteSeleccionado;
            if (imprimir && ContratosTildados() is { Count: > 0 } tildados)
                ContratosParaImprimir?.Invoke(
                    ConstruirContrato(codigo, cliente, parametros),
                    DuenoExpediente.DePrestamo(id),
                    tildados);

            var quien = autorizacion is null
                ? string.Empty
                : $"\n\nAutorizado por {autorizacion.Nombre}.";
            _dialogos.Informar("Préstamo creado",
                $"El préstamo {codigo} de {cliente.NombreCompleto} se creó correctamente.{quien}");
            // Los datos del acta pasan a ser los del negocio SOLO si el usuario
            // lo pidió con el interruptor. Va después de crear: si el préstamo
            // falla, la configuración no se toca.
            if (GuardarNotarialEnConfiguracion)
                GuardarNotarialEnAjustes();

            Limpiar();
            // La secuencia pudo moverse: o consumio un numero, o adopto el NCF
            // que se digito a mano. El marcador tiene que reflejar el nuevo.
            NcfMarcador = await _ncf.ProximoNcfAsync() ?? string.Empty;
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
    /// "Ver contratos" en Nuevo Préstamo: previsualiza los tres documentos con
    /// los datos actuales ANTES de crear (código en borrador). Sirve para
    /// revisarlos con el cliente antes de firmar.
    ///
    /// No lleva expediente porque el préstamo todavía no existe: lo que se
    /// imprima desde aquí es un borrador y no se archiva.
    /// </summary>
    [RelayCommand(CanExecute = nameof(PuedeGuardar))]
    private void VerPagare()
    {
        if (ContratoBorrador() is { } contrato)
            ContratosParaImprimir?.Invoke(contrato, null, []);
    }

    /// <summary>
    /// Arma los TRES contratos con lo que hay en el formulario (2026-09-03).
    ///
    /// Se apoya en <see cref="ContratoService.ArmarNotarialBorrador"/>, que es
    /// el mismo camino que usa un préstamo ya guardado: así la vista previa
    /// muestra exactamente el papel que va a salir, y no una aproximación que
    /// después no coincide.
    /// </summary>
    private PagareNotarialImpreso ConstruirContrato(string codigo, Cliente cliente,
        ParametrosAmortizacion parametros)
    {
        var tabla = _amortizacion.Calcular(parametros);
        var borrador = new Prestamo
        {
            Codigo = codigo,
            ClienteId = cliente.Id,
            MontoCapital = parametros.MontoCapital,
            TasaInteres = parametros.TasaInteresMensual,
            PlazoCuotas = parametros.PlazoCuotas,
            Modalidad = parametros.Modalidad,
            MetodoAmortizacion = parametros.Metodo,
            FechaInicio = parametros.FechaPrimerPago,
            Garantia = string.IsNullOrWhiteSpace(Garantia) ? null : Garantia.Trim(),
            // Sin préstamo guardado no hay created_at: el acta usa la fecha
            // cargada o, si no hay, la de hoy.
            CreatedAtUtc = DateTime.UtcNow,
            ActoNo = Vacio(ActoNo),
            FolioNo = Vacio(FolioNo),
            FechaActo = FechaActo is { } f ? DateOnly.FromDateTime(f) : null,
            MunicipioActo = Vacio(MunicipioActo),
            DeudorSexo = DeudorSexo?.Valor ?? SexoPersona.NoIndicado,
            DeudorNacionalidad = Vacio(DeudorNacionalidad),
            DeudorEstadoCivil = Vacio(DeudorEstadoCivil),
            DeudorOcupacion = Vacio(DeudorOcupacion),
            CuotasExigibilidad = EnteroOpcional(CuotasExigibilidadTexto),
            DiasGracia = EnteroOpcional(DiasGraciaTexto),
            MoraPorcentaje = DecimalOpcional(MoraPorcentajeTexto),
            RegistroTitulos = Vacio(RegistroTitulos)
        };
        return _contratos.ArmarNotarialBorrador(borrador, cliente, tabla, ActoDelFormulario());
    }

    /// <summary>Lo que capturó el formulario para el acta, listo para guardar.</summary>
    private ContratoNotarialNuevo DatosNotarialesDelFormulario() => new(
        ActoNo: Vacio(ActoNo),
        FolioNo: Vacio(FolioNo),
        FechaActo: FechaActo is { } f ? DateOnly.FromDateTime(f) : null,
        MunicipioActo: Vacio(MunicipioActo),
        DeudorSexo: DeudorSexo?.Valor ?? SexoPersona.NoIndicado,
        DeudorNacionalidad: Vacio(DeudorNacionalidad),
        DeudorEstadoCivil: Vacio(DeudorEstadoCivil),
        DeudorOcupacion: Vacio(DeudorOcupacion),
        CuotasExigibilidad: EnteroOpcional(CuotasExigibilidadTexto),
        DiasGracia: EnteroOpcional(DiasGraciaTexto),
        MoraPorcentaje: DecimalOpcional(MoraPorcentajeTexto),
        RegistroTitulos: Vacio(RegistroTitulos),
        // Las partes se congelan con el préstamo (045): reimprimir el contrato
        // el año que viene tiene que dar el MISMO papel que se firmó, aunque
        // para entonces el negocio haya cambiado de notario o de testigos.
        Partes: ActoDelFormulario());

    private static string? Vacio(string? texto) =>
        string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

    /// <summary>
    /// Un número opcional del formulario. Lo que no se entienda se trata como
    /// "no cargado" y cae al valor de Configuración: es un dato de un papel, no
    /// una condición del préstamo, así que no vale la pena frenar el guardado
    /// por un tipeo.
    /// </summary>
    private static int? EnteroOpcional(string texto) =>
        int.TryParse(texto, NumberStyles.Integer, CulturaRd, out var valor) && valor >= 0
            ? valor
            : null;

    private static decimal? DecimalOpcional(string texto) =>
        decimal.TryParse(texto, NumberStyles.Number, CulturaRd, out var valor) && valor >= 0m
            ? valor
            : null;

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
        // Los datos del acta también se limpian: son del contrato que se acaba
        // de crear, y dejarlos puestos los pegaría al préstamo siguiente sin
        // que nadie lo note. La selección de qué imprimir SÍ se conserva: es
        // una preferencia de la terminal, no del contrato.
        ActoNo = string.Empty;
        FolioNo = string.Empty;
        FechaActo = null;
        MunicipioActo = string.Empty;
        DeudorSexo = Sexos[0];
        DeudorNacionalidad = string.Empty;
        DeudorEstadoCivil = string.Empty;
        DeudorOcupacion = string.Empty;
        CuotasExigibilidadTexto = string.Empty;
        DiasGraciaTexto = string.Empty;
        MoraPorcentajeTexto = string.Empty;
        RegistroTitulos = string.Empty;
        VistaPreviaTipo = null;
        // Las partes del acta vuelven a las del negocio: son las que sirven
        // para el próximo préstamo. Lo del acto puntual (acto, folio, fecha)
        // sí se borró arriba.
        CargarNotarialDesdeConfiguracion();
        NcfTexto = string.Empty;
        NcfDeSecuencia = false;
        EsPrestamoAntiguo = false;
        ClienteAlDia = true;
        CuotasPagadasTexto = string.Empty;
        _cuotasVencidasAlCrear = 0;
    }
}
