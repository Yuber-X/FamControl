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

    public AlquilerDetalleViewModel(AlquilerService alquileres, VehiculoService vehiculos,
        ClienteService clientes, IDialogService dialogos, ExpedienteViewModel expediente)
    {
        _alquileres = alquileres;
        _vehiculos = vehiculos;
        _clientes = clientes;
        _dialogos = dialogos;
        Expediente = expediente;
    }

    /// <summary>
    /// Expediente del alquiler: el contrato firmado, la licencia del conductor,
    /// fotos del estado del auto al salir y al volver. Misma pantalla que en
    /// préstamos y ventas.
    /// </summary>
    public ExpedienteViewModel Expediente { get; }

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

            ArmarCierre(alquiler);
            ArmarAtraso(alquiler);

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
        var extra = Math.Round(alquiler.TarifaDia * dias, 2, MidpointRounding.AwayFromZero);
        AtrasoTexto = $"El vehículo tenía que volver hace {dias} día(s). " +
                      $"Al día de hoy corresponden {extra.ToString("N2", Textos.CulturaRd)} DOP de más " +
                      $"({alquiler.TarifaDia.ToString("N2", Textos.CulturaRd)} por día).";
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
                _alquilerId, Codigo, VehiculoDescripcion, _fechaInicio, TarifaDia, Dias, MontoTotal));
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
