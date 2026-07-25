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
            new Opcion<MetodoAmortizacion>(MetodoAmortizacion.Frances, Textos.De(MetodoAmortizacion.Frances))
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
    partial void OnMetodoSeleccionadoChanged(Opcion<MetodoAmortizacion> value) => Recalcular();
    partial void OnFechaPrimerPagoChanged(DateTime value) => Recalcular();
    partial void OnMontoFinalTextoChanged(string value) => Recalcular();
    partial void OnClienteSeleccionadoChanged(Cliente? value) => NotificarComandos();

    partial void OnModalidadSeleccionadaChanged(Opcion<Modalidad> value)
    {
        OnPropertyChanged(nameof(EsPagoUnico));
        OnPropertyChanged(nameof(MuestraPlazoYMetodo));
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

        return new ParametrosAmortizacion(
            monto, tasa, plazo, modalidad, metodo,
            DateOnly.FromDateTime(FechaPrimerPago));
    }

    private void Recalcular()
    {
        var parametros = ParsearParametros(out var mensaje);
        MensajeValidacion = mensaje;
        Preview.Clear();

        if (parametros is null)
        {
            TienePreview = false;
            if (ModoMontoFinal)
                TasaCalculadaTexto = string.Empty;   // no dejar una pista vieja
            NotificarComandos();
            return;
        }

        var tabla = _amortizacion.Calcular(parametros);
        foreach (var cuota in tabla)
            Preview.Add(cuota);

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
                AsignarNcfAuto: NcfDeSecuencia);

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
        FechaPrimerPago = FechaNegocio.Hoy.AddMonths(1).ToDateTime(TimeOnly.MinValue);
        Garantia = string.Empty;
        Notas = string.Empty;
        NcfTexto = string.Empty;
        NcfDeSecuencia = false;
    }
}
