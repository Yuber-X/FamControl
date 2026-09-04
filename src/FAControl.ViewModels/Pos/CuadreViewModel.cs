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

/// <summary>Fila del desglose por cajero (modo general).</summary>
/// <param name="Comision">
/// Lo que gano el cajero por sus ventas (037). Se calcula aquí y no en la base
/// porque el porcentaje puede cambiar: el cuadre de ayer se recalcula con el
/// porcentaje de ayer solo si se guarda, y guardarlo es una decision que el
/// cliente no pidio. Hoy muestra la comision al porcentaje VIGENTE.
/// </param>
public record CuadreCajeroFila(CuadreResumen Cuadre, decimal Comision = 0m)
{
    public string Nombre => Cuadre.NombreCajero;
    public int Facturas => Cuadre.TotalFacturas;
    public decimal Efectivo => Cuadre.TotalEfectivo;
    public decimal Tarjeta => Cuadre.TotalTarjeta;
    public decimal Otros => Cuadre.TotalTransferencia + Cuadre.TotalMixto;
    public decimal Total => Cuadre.TotalVendido;
    public string TiempoActivo => Cuadre.TiempoActivoTexto;
    public string EstadoTexto => Cuadre.YaCerrado ? "Cerrado" : "Abierto";
}

/// <summary>
/// Cuadre de caja. Por DEFECTO muestra el cuadre GENERAL (todos los cajeros
/// en un solo desglose, pedido Yuber 2026-07-12); también se puede elegir un
/// cajero concreto. Un Cajero sin 'cuadre_todos' solo ve su propio turno.
/// El cierre se imprime SIEMPRE con vista previa y tamaño elegible.
/// </summary>
public partial class CuadreViewModel : ObservableObject, IPaginaAsincrona
{
    /// <summary>Valor del combo que representa "todos los cajeros".</summary>
    public const long IdGeneral = -1;

    private readonly CuadreService _cuadres;
    private readonly IDialogService _dialogos;
    private readonly AjustesLocales _ajustes;
    /// <summary>De aca sale el % de comision del vendedor (037): es del negocio.</summary>
    private readonly ConfiguracionNegocioService _config;

    /// <summary>La App abre la vista previa imprimible del cierre.</summary>
    public event Action<CuadreGeneral, TamanoImpresion>? ImpresionSolicitada;

    public CuadreViewModel(CuadreService cuadres, IDialogService dialogos, AjustesLocales ajustes,
        ConfiguracionNegocioService config)
    {
        _cuadres = cuadres;
        _dialogos = dialogos;
        _ajustes = ajustes;
        _config = config;
        _fecha = FechaNegocio.Hoy.ToDateTime(TimeOnly.MinValue);

        Tamanos =
        [
            new Opcion<TamanoImpresion>(TamanoImpresion.Ticket80mm, "Ticket 80mm"),
            new Opcion<TamanoImpresion>(TamanoImpresion.Carta, "Hoja carta")
        ];
        _tamanoSeleccionado = Tamanos[0];
    }

    public ObservableCollection<Opcion<long>> Cajeros { get; } = [];
    public ObservableCollection<CuadreCajeroFila> Desglose { get; } = [];
    public IReadOnlyList<Opcion<TamanoImpresion>> Tamanos { get; }

    [ObservableProperty] private Opcion<long>? _cajeroSeleccionado;
    [ObservableProperty] private Opcion<TamanoImpresion> _tamanoSeleccionado;
    [ObservableProperty] private DateTime _fecha;

    [ObservableProperty] private string _tituloCuadre = string.Empty;
    [ObservableProperty] private int _totalFacturas;
    [ObservableProperty] private decimal _totalVendido;

    /// <summary>
    /// Comision del dia (037). Aparece en el cuadre y en la exportacion, NUNCA
    /// en la factura: es entre el negocio y su empleado.
    /// </summary>
    [ObservableProperty] private decimal _comisionTotal;
    [ObservableProperty] private bool _mostrarComision;
    [ObservableProperty] private decimal _totalEfectivo;
    [ObservableProperty] private decimal _totalTarjeta;
    [ObservableProperty] private decimal _totalTransferencia;
    [ObservableProperty] private decimal _totalMixto;
    [ObservableProperty] private int _facturasAnuladas;
    [ObservableProperty] private decimal _montoAnulado;
    [ObservableProperty] private string _tiempoActivoTexto = "—";
    [ObservableProperty] private bool _esGeneral = true;
    [ObservableProperty] private bool _yaCerrado;
    [ObservableProperty] private string _mensajeEstado = string.Empty;
    [ObservableProperty] private bool _ocupado;

    public bool PuedeElegirCajero => CuadreService.PuedeVerTodos;
    public bool PuedeCerrar => !YaCerrado && (TotalFacturas + FacturasAnuladas) > 0;
    public bool PuedeImprimir => (TotalFacturas + FacturasAnuladas) > 0;

    partial void OnCajeroSeleccionadoChanged(Opcion<long>? value) => _ = CargarAsync();
    partial void OnFechaChanged(DateTime value) => _ = CargarAsync();
    partial void OnYaCerradoChanged(bool value) => OnPropertyChanged(nameof(PuedeCerrar));
    partial void OnTotalFacturasChanged(int value)
    {
        OnPropertyChanged(nameof(PuedeCerrar));
        OnPropertyChanged(nameof(PuedeImprimir));
    }

    public async Task RefrescarAsync()
    {
        OnPropertyChanged(nameof(PuedeElegirCajero));

        // Tras un cambio de usuario, el alcance puede haber cambiado (un Admin
        // ve a todos, un Cajero solo a sí mismo): se reconstruye la lista.
        var alcanceCambio = PuedeElegirCajero
            ? Cajeros.Count == 0 || Cajeros[0].Valor != IdGeneral
            : Cajeros.Count != 1 || Cajeros[0].Valor != SesionActual.Id;
        if (alcanceCambio)
        {
            Cajeros.Clear();
            CajeroSeleccionado = null;
        }

        if (Cajeros.Count == 0)
        {
            if (PuedeElegirCajero)
            {
                // Opción por defecto: el cuadre GENERAL de todo el negocio
                Cajeros.Add(new Opcion<long>(IdGeneral, "General (todos los cajeros)"));
                foreach (var (id, nombre) in await _cuadres.ObtenerCajerosAsync())
                    Cajeros.Add(new Opcion<long>(id, nombre));
                CajeroSeleccionado = Cajeros[0];
            }
            else
            {
                // Un Cajero solo puede ver su turno
                Cajeros.Add(new Opcion<long>(SesionActual.Id, SesionActual.Nombre));
                CajeroSeleccionado = Cajeros[0];
            }
            return;   // el cambio de selección ya dispara CargarAsync
        }

        await CargarAsync();
    }

    private async Task CargarAsync()
    {
        var seleccion = CajeroSeleccionado?.Valor ?? SesionActual.Id;
        EsGeneral = seleccion == IdGeneral;

        try
        {
            Ocupado = true;
            Desglose.Clear();

            if (EsGeneral)
            {
                var general = await _cuadres.CalcularGeneralAsync(DateOnly.FromDateTime(Fecha));

                TituloCuadre = "Cuadre general del negocio";
                TotalFacturas = general.TotalFacturas;
                TotalVendido = general.TotalVendido;
                // Comision del dia (037): sobre lo vendido, al porcentaje vigente
                ComisionTotal = _config.Actual.ComisionSobre(general.TotalVendido);
                MostrarComision = _config.Actual.ComisionActiva;
                TotalEfectivo = general.TotalEfectivo;
                TotalTarjeta = general.TotalTarjeta;
                TotalTransferencia = general.TotalTransferencia;
                TotalMixto = general.TotalMixto;
                FacturasAnuladas = general.FacturasAnuladas;
                MontoAnulado = general.MontoAnulado;
                TiempoActivoTexto = $"{general.PorCajero.Count} cajero(s) con actividad";
                YaCerrado = general.PorCajero.Count > 0 && general.TodosCerrados;

                foreach (var cajero in general.PorCajero)
                    Desglose.Add(new CuadreCajeroFila(cajero));

                MensajeEstado = general.PorCajero.Count == 0
                    ? "Sin ventas registradas en este día."
                    : general.TodosCerrados
                        ? "Todos los turnos del día están cerrados: son registros definitivos."
                        : $"{general.PorCajero.Count(c => !c.YaCerrado)} turno(s) sin cerrar.";
            }
            else
            {
                var cuadre = await _cuadres.CalcularAsync(seleccion, DateOnly.FromDateTime(Fecha));

                TituloCuadre = $"Cuadre de {cuadre.NombreCajero}";
                TotalFacturas = cuadre.TotalFacturas;
                TotalVendido = cuadre.TotalVendido;
                ComisionTotal = _config.Actual.ComisionSobre(cuadre.TotalVendido);
                MostrarComision = _config.Actual.ComisionActiva;
                TotalEfectivo = cuadre.TotalEfectivo;
                TotalTarjeta = cuadre.TotalTarjeta;
                TotalTransferencia = cuadre.TotalTransferencia;
                TotalMixto = cuadre.TotalMixto;
                FacturasAnuladas = cuadre.FacturasAnuladas;
                MontoAnulado = cuadre.MontoAnulado;
                TiempoActivoTexto = cuadre.TiempoActivoTexto;
                YaCerrado = cuadre.YaCerrado;

                MensajeEstado = cuadre.YaCerrado
                    ? "Este cuadre ya fue cerrado: es un registro definitivo y no puede modificarse."
                    : cuadre.TotalFacturas == 0 && cuadre.FacturasAnuladas == 0
                        ? "Sin ventas registradas en este día."
                        : string.Empty;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error calculando el cuadre");
            _dialogos.MostrarError("Cuadre de caja", ex.Message);
        }
        finally
        {
            Ocupado = false;
        }
    }

    [RelayCommand]
    private async Task CerrarAsync()
    {
        var fecha = DateOnly.FromDateTime(Fecha);
        var seleccion = CajeroSeleccionado?.Valor ?? SesionActual.Id;

        var mensaje = EsGeneral
            ? $"¿Cerrar la caja de TODOS los cajeros del {fecha:dd/MM/yyyy}?\n\n" +
              $"Total del día: {TotalVendido:N2} en {TotalFacturas} factura(s).\n" +
              "Una vez cerrados, los turnos no se pueden modificar."
            : $"¿Cerrar el cuadre de {TituloCuadre.Replace("Cuadre de ", "")} del {fecha:dd/MM/yyyy}?\n\n" +
              $"Total: {TotalVendido:N2} en {TotalFacturas} factura(s).\n" +
              "Una vez cerrado NO se puede modificar.";

        if (!_dialogos.Confirmar("Cerrar cuadre", mensaje))
            return;

        try
        {
            Ocupado = true;
            if (EsGeneral)
            {
                var cerrados = await _cuadres.CerrarPendientesDelDiaAsync(fecha);
                _dialogos.Informar("Cuadre cerrado",
                    cerrados == 0
                        ? "No había turnos pendientes de cerrar."
                        : $"Se cerraron {cerrados} turno(s).");
            }
            else
            {
                var cuadre = await _cuadres.CalcularAsync(seleccion, fecha);
                await _cuadres.CerrarAsync(cuadre);
                _dialogos.Informar("Cuadre cerrado", "El cuadre quedó registrado.");
            }
            await CargarAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cerrando el cuadre");
            _dialogos.MostrarError("Cerrar cuadre", ex.Message);
        }
        finally
        {
            Ocupado = false;
        }
    }

    /// <summary>
    /// Imprime el cierre. SIEMPRE muestra vista previa antes (pedido de Yuber)
    /// y respeta el tamaño de papel elegido en pantalla.
    /// </summary>
    [RelayCommand]
    private async Task ImprimirAsync()
    {
        var fecha = DateOnly.FromDateTime(Fecha);
        try
        {
            Ocupado = true;
            CuadreGeneral cierre;

            if (EsGeneral)
            {
                cierre = await _cuadres.CalcularGeneralAsync(fecha);
            }
            else
            {
                // Un cajero suelto se imprime con el mismo formato, con una sola sección
                var cuadre = await _cuadres.CalcularAsync(
                    CajeroSeleccionado?.Valor ?? SesionActual.Id, fecha);
                cierre = new CuadreGeneral(fecha, [cuadre],
                    cuadre.TotalFacturas, cuadre.TotalVendido, cuadre.TotalEfectivo,
                    cuadre.TotalTarjeta, cuadre.TotalTransferencia, cuadre.TotalMixto,
                    cuadre.FacturasAnuladas, cuadre.MontoAnulado);
            }

            ImpresionSolicitada?.Invoke(cierre, TamanoSeleccionado.Valor);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error preparando la impresión del cierre");
            _dialogos.MostrarError("Imprimir cierre", ex.Message);
        }
        finally
        {
            Ocupado = false;
        }
    }
}
