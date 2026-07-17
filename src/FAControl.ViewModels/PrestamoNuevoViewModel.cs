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

    public event Action<long>? PrestamoCreado;

    public PrestamoNuevoViewModel(PrestamoService prestamos, ClienteService clientes,
        AmortizacionService amortizacion, IDialogService dialogos, IAutorizadorAdmin autorizador)
    {
        _autorizador = autorizador;
        _prestamos = prestamos;
        _clientes = clientes;
        _amortizacion = amortizacion;
        _dialogos = dialogos;

        Modalidades =
        [
            new Opcion<Modalidad>(Modalidad.Mensual, Textos.De(Modalidad.Mensual)),
            new Opcion<Modalidad>(Modalidad.Quincenal, Textos.De(Modalidad.Quincenal)),
            new Opcion<Modalidad>(Modalidad.Semanal, Textos.De(Modalidad.Semanal)),
            new Opcion<Modalidad>(Modalidad.Diaria, Textos.De(Modalidad.Diaria))
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

    [ObservableProperty] private Cliente? _clienteSeleccionado;
    [ObservableProperty] private string _montoTexto = string.Empty;
    [ObservableProperty] private string _tasaTexto = string.Empty;
    [ObservableProperty] private string _plazoTexto = string.Empty;
    [ObservableProperty] private Opcion<Modalidad> _modalidadSeleccionada;
    [ObservableProperty] private Opcion<MetodoAmortizacion> _metodoSeleccionado;
    [ObservableProperty] private DateTime _fechaPrimerPago;
    [ObservableProperty] private string _garantia = string.Empty;
    [ObservableProperty] private string _notas = string.Empty;

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
    partial void OnModalidadSeleccionadaChanged(Opcion<Modalidad> value) => Recalcular();
    partial void OnMetodoSeleccionadoChanged(Opcion<MetodoAmortizacion> value) => Recalcular();
    partial void OnFechaPrimerPagoChanged(DateTime value) => Recalcular();
    partial void OnClienteSeleccionadoChanged(Cliente? value) => GuardarCommand.NotifyCanExecuteChanged();

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
            string.IsNullOrWhiteSpace(PlazoTexto))
            return null; // formulario vacío: sin preview y sin regaño

        if (!decimal.TryParse(MontoTexto, NumberStyles.Number, CulturaRd, out var monto) || monto <= 0m)
        {
            mensaje = "Ingresá un monto válido mayor que cero (ej. 75,000).";
            return null;
        }
        if (!decimal.TryParse(TasaTexto, NumberStyles.Number, CulturaRd, out var tasa) || tasa < 0m)
        {
            mensaje = "Ingresá una tasa mensual válida (ej. 5).";
            return null;
        }
        if (!int.TryParse(PlazoTexto, NumberStyles.Integer, CulturaRd, out var plazo) || plazo <= 0)
        {
            mensaje = "Ingresá la cantidad de cuotas (ej. 12).";
            return null;
        }
        if (plazo > 1000)
        {
            mensaje = "El plazo máximo soportado es de 1,000 cuotas.";
            return null;
        }

        return new ParametrosAmortizacion(
            monto, tasa, plazo,
            ModalidadSeleccionada.Valor,
            MetodoSeleccionado.Valor,
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
            GuardarCommand.NotifyCanExecuteChanged();
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
        GuardarCommand.NotifyCanExecuteChanged();
    }

    private bool PuedeGuardar() => TienePreview && ClienteSeleccionado is not null;

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
                string.IsNullOrWhiteSpace(Notas) ? null : Notas.Trim());

            var (id, codigo) = await _prestamos.CrearAsync(solicitud, autorizacion);

            var quien = autorizacion is null
                ? string.Empty
                : $"\n\nAutorizado por {autorizacion.Nombre}.";
            _dialogos.Informar("Préstamo creado",
                $"El préstamo {codigo} de {ClienteSeleccionado.NombreCompleto} se creó correctamente.{quien}");
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

    private void Limpiar()
    {
        ClienteSeleccionado = null;
        MontoTexto = string.Empty;
        TasaTexto = string.Empty;
        PlazoTexto = string.Empty;
        ModalidadSeleccionada = Modalidades[0];
        MetodoSeleccionado = Metodos[0];
        FechaPrimerPago = FechaNegocio.Hoy.AddMonths(1).ToDateTime(TimeOnly.MinValue);
        Garantia = string.Empty;
        Notas = string.Empty;
    }
}
