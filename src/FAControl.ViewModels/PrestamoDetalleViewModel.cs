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
    private readonly RecordatorioService _recordatorios;
    private readonly NcfService _ncf;
    private long _prestamoId;
    private long _clienteId;

    public event Action<long>? CobrarSolicitado;
    public event Action? VolverSolicitado;
    /// <summary>
    /// La View abre el diálogo de corrección (029) y devuelve lo que el usuario
    /// confirmó, o null si se arrepintió. Es una propiedad y no un `event`
    /// porque DEVUELVE un valor: un evento con retorno da CS0079.
    /// </summary>
    public Func<PrestamoParaEditar, EdicionPrestamo?>? EdicionSolicitada { get; set; }
    /// <summary>La App abre la vista previa imprimible del préstamo.</summary>
    public event Action<PrestamoImpreso>? ImpresionSolicitada;
    /// <summary>La App abre la vista previa de la intimación de pago.</summary>
    public event Action<IntimacionImpresa>? IntimacionSolicitada;

    public PrestamoDetalleViewModel(PrestamoService prestamos, ClienteService clientes,
        IDialogService dialogos, AjustesLocales ajustes, RecordatorioService recordatorios,
        NcfService ncf, ExpedienteViewModel expediente)
    {
        _prestamos = prestamos;
        _clientes = clientes;
        _dialogos = dialogos;
        _ajustes = ajustes;
        _recordatorios = recordatorios;
        _ncf = ncf;
        Expediente = expediente;
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
    // Comprobante fiscal (pedido 2026-07-25)
    [ObservableProperty] private string _ncfTexto = "—";
    [ObservableProperty] private bool _tieneNcf;
    [ObservableProperty] private bool _tieneNotas;
    [ObservableProperty] private string _ncfManual = string.Empty;

    /// <summary>
    /// Muestra el botón "Editar" (029). El Admin lo tiene siempre; a los demás
    /// se los habilita el Admin desde Usuarios, tal como lo pidió el cliente:
    /// "solo los admin pueden tener, o un permiso otorgado por el mismo".
    /// </summary>
    public bool PuedeEditar => SesionActual.EsAdmin || SesionActual.TienePermiso(Permisos.PrestamosEditar);

    public async Task CargarAsync(long prestamoId)
    {
        try
        {
            _prestamoId = prestamoId;
            var prestamo = await _prestamos.ObtenerPorIdAsync(prestamoId)
                ?? throw new InvalidOperationException($"No existe el préstamo con id {prestamoId}.");
            _clienteId = prestamo.ClienteId;
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
            // Sin notas la sección entera se oculta: no vale la pena gastar
            // alto de pantalla en un guion.
            TieneNotas = !string.IsNullOrWhiteSpace(prestamo.Notas);
            NotasTexto = prestamo.Notas ?? string.Empty;
            TieneNcf = !string.IsNullOrWhiteSpace(prestamo.Ncf);
            NcfTexto = TieneNcf ? prestamo.Ncf! : "—";
            NcfManual = string.Empty;

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

    /// <summary>
    /// Expediente de ESTE préstamo (026): donde queda archivada la intimación
    /// de pago al imprimirla.
    /// </summary>
    public DuenoExpediente Dueno => DuenoExpediente.DePrestamo(_prestamoId);

    public ExpedienteViewModel Expediente { get; }

    [RelayCommand]
    private void Cobrar() => CobrarSolicitado?.Invoke(_prestamoId);

    /// <summary>
    /// Corrige el préstamo (029). Antes de abrir el formulario le pregunta al
    /// servicio hasta dónde se puede editar, para que la pantalla no ofrezca
    /// cambiar montos que después van a ser rechazados.
    /// </summary>
    [RelayCommand]
    private async Task EditarAsync()
    {
        if (EdicionSolicitada is null)
            return;

        try
        {
            var prestamo = await _prestamos.ObtenerPorIdAsync(_prestamoId);
            if (prestamo is null)
            {
                _dialogos.MostrarError("Corregir préstamo", "El préstamo ya no existe.");
                return;
            }

            var permitido = await _prestamos.ConsultarEdicionPermitidaAsync(_prestamoId);

            // La vista previa se calcula acá y baja como delegado: la View no
            // referencia Services. Así el número que se ve mientras se tipea
            // sale del mismo cálculo que después se guarda.
            var cambios = EdicionSolicitada(new PrestamoParaEditar(
                _prestamoId, Codigo, prestamo, permitido, Previsualizar));
            if (cambios is null)
                return;   // se arrepintió

            await _prestamos.EditarAsync(cambios);
            await CargarAsync(_prestamoId);
            _dialogos.Informar("Préstamo corregido",
                $"El préstamo {Codigo} quedó corregido." +
                (permitido.Todo ? "\n\nLa tabla de cuotas se rehizo con los datos nuevos." : ""));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            _dialogos.MostrarError("Corregir préstamo", ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error corrigiendo el préstamo {Id}", _prestamoId);
            _dialogos.MostrarError("Corregir préstamo", $"No se pudo corregir el préstamo.\n\n{ex.Message}");
        }
    }

    /// <summary>Cómo queda la cuota con lo que el usuario está tipeando.</summary>
    private VistaPreviaCuota Previsualizar(ParametrosAmortizacion parametros)
    {
        try
        {
            var tabla = _prestamos.CalcularAmortizacion(parametros);
            var total = tabla.Sum(c => c.MontoTotal);
            return new VistaPreviaCuota(
                $"Cuota: {tabla[0].MontoTotal:N2} DOP × {parametros.PlazoCuotas}",
                $"Total a pagar {total:N2} DOP — interés {total - parametros.MontoCapital:N2} DOP. " +
                $"Primera cuota el {parametros.FechaPrimerPago:dd/MM/yyyy}, " +
                $"última el {tabla[^1].FechaVencimiento:dd/MM/yyyy}.");
        }
        catch (ArgumentException ex)
        {
            // El cálculo valida los rangos; se muestra su mensaje tal cual en
            // vez de inventar uno propio que diga otra cosa.
            return new VistaPreviaCuota(ex.Message, string.Empty);
        }
    }

    /// <summary>
    /// Asigna el comprobante fiscal a un préstamo que no tiene: con texto en
    /// <see cref="NcfManual"/> lo registra (Facturador Gratuito DGII); vacío,
    /// toma el siguiente de la secuencia configurada. Irreversible.
    /// </summary>
    [RelayCommand]
    private async Task AsignarNcfAsync()
    {
        var manual = string.IsNullOrWhiteSpace(NcfManual) ? null : NcfManual.Trim();
        var detalle = manual is null
            ? "Se tomará el SIGUIENTE número de la secuencia configurada."
            : $"Se registrará el comprobante {manual.ToUpperInvariant()}.";
        if (!_dialogos.Confirmar("Comprobante fiscal",
            $"{detalle}\n\nUn comprobante asignado no se puede cambiar. ¿Continuar?"))
            return;

        try
        {
            var ncf = await _ncf.AsignarAsync(_prestamoId, manual);
            TieneNcf = true;
            NcfTexto = ncf;
            NcfManual = string.Empty;
            _dialogos.Informar("Comprobante fiscal", $"Comprobante {ncf} asignado al préstamo {Codigo}.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or ArgumentException)
        {
            _dialogos.MostrarError("Comprobante fiscal", ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error asignando NCF al préstamo {Id}", _prestamoId);
            _dialogos.MostrarError("Comprobante fiscal", $"No se pudo asignar el comprobante.\n\n{ex.Message}");
        }
    }

    /// <summary>Envía un recordatorio por correo al cliente de ESTE préstamo.</summary>
    [RelayCommand]
    private async Task EnviarRecordatorioAsync()
    {
        try
        {
            var mensaje = await _recordatorios.EnviarAClienteAsync(_clienteId);
            _dialogos.Informar("Recordatorio", mensaje);
        }
        catch (InvalidOperationException ex)
        {
            _dialogos.Informar("Recordatorio", ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error enviando recordatorio individual del préstamo {Id}", _prestamoId);
            _dialogos.MostrarError("Recordatorio", ex.Message);
        }
    }

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
                c.MontoTotal, c.SaldoDespues, c.SemaforoTexto))],
            NegocioNombre: _ajustes.NombreNegocio,
            NegocioRnc: _ajustes.RncNegocio,
            NegocioTelefono: _ajustes.TelefonoNegocio));
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
