using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAControl.Common;
using Serilog;

namespace FAControl.ViewModels;

/// <summary>Página de destino de la navegación del sidebar.</summary>
public enum Pagina
{
    Panel,
    Clientes,
    Prestamos,
    NuevoPrestamo,
    Cobros,
    Contratos,
    Reportes,
    Historial,
    Usuarios,
    Configuracion,
    // DealerControl (Tier 5)
    Vehiculos
}

/// <summary>
/// Shell principal: página activa + cableado de la navegación entre módulos
/// (lista → detalle → cobros). Las páginas sin implementar muestran un placeholder.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly PrestamosViewModel _prestamosVm;
    private readonly PrestamoNuevoViewModel _nuevoVm;
    private readonly PrestamoDetalleViewModel _detalleVm;
    private readonly CobrosViewModel _cobrosVm;
    private readonly ClientesViewModel _clientesVm;
    private readonly ClienteFichaViewModel _fichaVm;
    private readonly ClienteFormViewModel _clienteFormVm;
    private readonly PanelViewModel _panelVm;
    private readonly ReportesViewModel _reportesVm;
    private readonly HistorialViewModel _historialVm;
    private readonly UsuariosViewModel _usuariosVm;
    private readonly ContratosViewModel _contratosVm;
    private readonly ConfiguracionViewModel _configuracionVm;
    private readonly VehiculosViewModel _vehiculosVm;
    private readonly VehiculoFormViewModel _vehiculoFormVm;

    public MainViewModel(PrestamosViewModel prestamosVm, PrestamoNuevoViewModel nuevoVm,
        PrestamoDetalleViewModel detalleVm, CobrosViewModel cobrosVm,
        ClientesViewModel clientesVm, ClienteFichaViewModel fichaVm, ClienteFormViewModel clienteFormVm,
        PanelViewModel panelVm, ReportesViewModel reportesVm, HistorialViewModel historialVm,
        UsuariosViewModel usuariosVm, ContratosViewModel contratosVm, ConfiguracionViewModel configuracionVm,
        VehiculosViewModel vehiculosVm, VehiculoFormViewModel vehiculoFormVm)
    {
        _panelVm = panelVm;
        _reportesVm = reportesVm;
        _historialVm = historialVm;
        _usuariosVm = usuariosVm;
        _contratosVm = contratosVm;
        _configuracionVm = configuracionVm;
        _vehiculosVm = vehiculosVm;
        _vehiculoFormVm = vehiculoFormVm;
        _panelVm.CobrarSolicitado += id => _ = AbrirCobrosAsync(id);
        _prestamosVm = prestamosVm;
        _nuevoVm = nuevoVm;
        _detalleVm = detalleVm;
        _cobrosVm = cobrosVm;
        _clientesVm = clientesVm;
        _fichaVm = fichaVm;
        _clienteFormVm = clienteFormVm;

        // Navegación entre módulos disparada por los propios ViewModels
        _prestamosVm.NuevoSolicitado += () => _ = NavegarAsync(Pagina.NuevoPrestamo);
        _prestamosVm.DetalleSolicitado += id => _ = AbrirDetalleAsync(id);
        _nuevoVm.PrestamoCreado += id => _ = AbrirDetalleAsync(id);
        _detalleVm.VolverSolicitado += () => _ = NavegarAsync(Pagina.Prestamos);
        _detalleVm.CobrarSolicitado += id => _ = AbrirCobrosAsync(id);

        // Clientes: lista → ficha → formulario / nuevo préstamo
        _clientesVm.NuevoSolicitado += AbrirClienteNuevo;
        _clientesVm.FichaSolicitada += id => _ = AbrirFichaAsync(id);
        _fichaVm.EditarSolicitado += id => _ = AbrirClienteEdicionAsync(id);
        _fichaVm.VolverSolicitado += () => _ = NavegarAsync(Pagina.Clientes);
        _fichaVm.PrestamoSeleccionado += id => _ = AbrirDetalleAsync(id);
        _fichaVm.NuevoPrestamoSolicitado += id => _ = AbrirNuevoPrestamoParaClienteAsync(id);
        _clienteFormVm.Guardado += id => _ = AbrirFichaAsync(id);
        _clienteFormVm.Cancelado += () => _ = NavegarAsync(Pagina.Clientes);

        // DealerControl: inventario → alta/edición → vuelta a la lista
        _vehiculosVm.NuevoSolicitado += AbrirVehiculoNuevo;
        _vehiculosVm.EditarSolicitado += id => _ = AbrirVehiculoEdicionAsync(id);
        _vehiculoFormVm.Guardado += id => _ = NavegarAsync(Pagina.Vehiculos);
        _vehiculoFormVm.Cancelado += () => _ = NavegarAsync(Pagina.Vehiculos);
    }

    [ObservableProperty]
    private Pagina _paginaActual = Pagina.Panel;

    [ObservableProperty]
    private object? _paginaActualVm;

    [ObservableProperty]
    private string _tituloPagina = "Panel de control";

    public string NombreUsuario => SesionActual.Nombre;
    public string RolUsuario => SesionActual.Rol;

    /// <summary>Modo activo de la suite (elegido en el launcher). Gobierna qué módulos aparecen.</summary>
    public ModoApp Modo { get; private set; } = ModoApp.PrestControl;
    public bool EsPrestControl => Modo == ModoApp.PrestControl;
    public bool EsDealerControl => Modo == ModoApp.DealerControl;

    // ------------------------------------------------------------------
    // Visibilidad del sidebar por MODO + permiso (multicuentas 2026-07-16,
    // multimodo Tier 5). Esto es UX, NO seguridad: la regla la aplica el Service.
    // Los módulos de PrestControl solo aparecen en ese modo; el inventario, en Dealer.
    // Historial / Usuarios / Configuración son transversales a los modos.
    // ------------------------------------------------------------------
    public bool PuedeVerPanel => EsPrestControl && SesionActual.TienePermiso(Permisos.Panel);
    public bool PuedeVerClientes => EsPrestControl && SesionActual.TienePermiso(Permisos.Clientes);
    public bool PuedeVerPrestamos => EsPrestControl && SesionActual.TienePermiso(Permisos.Prestamos);
    public bool PuedeVerNuevoPrestamo => EsPrestControl && SesionActual.TienePermiso(Permisos.PrestamosCrear);
    public bool PuedeVerCobros => EsPrestControl && SesionActual.TienePermiso(Permisos.Cobros);
    /// <summary>El contrato es el pagaré del préstamo: reusa el permiso de préstamos.</summary>
    public bool PuedeVerContratos => EsPrestControl && SesionActual.TienePermiso(Permisos.Prestamos);
    public bool PuedeVerReportes => EsPrestControl && SesionActual.TienePermiso(Permisos.Reportes);
    /// <summary>Inventario de vehículos: exclusivo de DealerControl.</summary>
    public bool PuedeVerVehiculos => EsDealerControl && SesionActual.TienePermiso(Permisos.Vehiculos);
    public bool PuedeVerHistorial => SesionActual.TienePermiso(Permisos.Historial);
    public bool PuedeVerUsuarios => SesionActual.TienePermiso(Permisos.Usuarios);
    /// <summary>Configuración es EXCLUSIVA de Admin (regla del cliente).</summary>
    public bool PuedeVerConfiguracion => SesionActual.EsAdmin
                                         && SesionActual.TienePermiso(Permisos.Configuracion);

    /// <summary>Fija el modo activo (lo llama App al abrir el shell) y refresca el sidebar.</summary>
    public void EstablecerModo(ModoApp modo)
    {
        Modo = modo;
        OnPropertyChanged(nameof(Modo));
        OnPropertyChanged(nameof(EsPrestControl));
        OnPropertyChanged(nameof(EsDealerControl));
        RefrescarPermisos();
    }

    /// <summary>
    /// Reevalúa el sidebar tras un login (los permisos cambian con el usuario).
    /// </summary>
    public void RefrescarPermisos()
    {
        OnPropertyChanged(nameof(NombreUsuario));
        OnPropertyChanged(nameof(RolUsuario));
        OnPropertyChanged(nameof(Iniciales));
        OnPropertyChanged(nameof(PuedeVerPanel));
        OnPropertyChanged(nameof(PuedeVerClientes));
        OnPropertyChanged(nameof(PuedeVerPrestamos));
        OnPropertyChanged(nameof(PuedeVerNuevoPrestamo));
        OnPropertyChanged(nameof(PuedeVerCobros));
        OnPropertyChanged(nameof(PuedeVerContratos));
        OnPropertyChanged(nameof(PuedeVerReportes));
        OnPropertyChanged(nameof(PuedeVerVehiculos));
        OnPropertyChanged(nameof(PuedeVerHistorial));
        OnPropertyChanged(nameof(PuedeVerUsuarios));
        OnPropertyChanged(nameof(PuedeVerConfiguracion));
    }

    public string Iniciales
    {
        get
        {
            var partes = SesionActual.Nombre.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return partes.Length switch
            {
                0 => "?",
                1 => partes[0][..1].ToUpperInvariant(),
                _ => $"{partes[0][..1]}{partes[1][..1]}".ToUpperInvariant()
            };
        }
    }

    /// <summary>
    /// Carga inicial del shell: aterriza en la página principal del modo activo
    /// (Panel en PrestControl, Vehículos en DealerControl).
    /// </summary>
    public Task InicializarAsync() =>
        NavegarAsync(EsDealerControl ? Pagina.Vehiculos : Pagina.Panel);

    [RelayCommand]
    private async Task NavegarAsync(Pagina destino)
    {
        try
        {
            PaginaActual = destino;
            TituloPagina = TituloDe(destino);

            switch (destino)
            {
                case Pagina.Panel:
                    await _panelVm.CargarAsync();
                    PaginaActualVm = _panelVm;
                    break;
                case Pagina.Clientes:
                    await _clientesVm.CargarAsync();
                    PaginaActualVm = _clientesVm;
                    break;
                case Pagina.Prestamos:
                    await _prestamosVm.CargarAsync();
                    PaginaActualVm = _prestamosVm;
                    break;
                case Pagina.NuevoPrestamo:
                    await _nuevoVm.CargarAsync();
                    PaginaActualVm = _nuevoVm;
                    break;
                case Pagina.Cobros:
                    await _cobrosVm.CargarAsync();
                    PaginaActualVm = _cobrosVm;
                    break;
                case Pagina.Reportes:
                    await _reportesVm.CargarAsync();
                    PaginaActualVm = _reportesVm;
                    break;
                case Pagina.Historial:
                    await _historialVm.CargarAsync();
                    PaginaActualVm = _historialVm;
                    break;
                case Pagina.Contratos:
                    await _contratosVm.CargarAsync();
                    PaginaActualVm = _contratosVm;
                    break;
                case Pagina.Usuarios:
                    await _usuariosVm.CargarAsync();
                    PaginaActualVm = _usuariosVm;
                    break;
                case Pagina.Vehiculos:
                    await _vehiculosVm.CargarAsync();
                    PaginaActualVm = _vehiculosVm;
                    break;
                case Pagina.Configuracion:
                    PaginaActualVm = _configuracionVm;
                    break;
                default:
                    PaginaActualVm = new PlaceholderViewModel(TituloDe(destino));
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error navegando a {Destino}", destino);
        }
    }

    private async Task AbrirDetalleAsync(long prestamoId)
    {
        try
        {
            PaginaActual = Pagina.Prestamos;
            TituloPagina = "Detalle de préstamo";
            await _detalleVm.CargarAsync(prestamoId);
            PaginaActualVm = _detalleVm;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error abriendo el detalle del préstamo {Id}", prestamoId);
        }
    }

    private async Task AbrirFichaAsync(long clienteId)
    {
        try
        {
            PaginaActual = Pagina.Clientes;
            TituloPagina = "Ficha de cliente";
            await _fichaVm.CargarAsync(clienteId);
            PaginaActualVm = _fichaVm;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error abriendo la ficha del cliente {Id}", clienteId);
        }
    }

    private void AbrirClienteNuevo()
    {
        _clienteFormVm.PrepararNuevo();
        PaginaActual = Pagina.Clientes;
        TituloPagina = "Nuevo cliente";
        PaginaActualVm = _clienteFormVm;
    }

    private async Task AbrirClienteEdicionAsync(long clienteId)
    {
        try
        {
            await _clienteFormVm.PrepararEdicionAsync(clienteId);
            PaginaActual = Pagina.Clientes;
            TituloPagina = "Editar cliente";
            PaginaActualVm = _clienteFormVm;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error abriendo la edición del cliente {Id}", clienteId);
        }
    }

    private async Task AbrirNuevoPrestamoParaClienteAsync(long clienteId)
    {
        await NavegarAsync(Pagina.NuevoPrestamo);
        _nuevoVm.PreseleccionarCliente(clienteId);
    }

    private void AbrirVehiculoNuevo()
    {
        _vehiculoFormVm.PrepararNuevo();
        PaginaActual = Pagina.Vehiculos;
        TituloPagina = "Nuevo vehículo";
        PaginaActualVm = _vehiculoFormVm;
    }

    private async Task AbrirVehiculoEdicionAsync(long vehiculoId)
    {
        try
        {
            await _vehiculoFormVm.PrepararEdicionAsync(vehiculoId);
            PaginaActual = Pagina.Vehiculos;
            TituloPagina = "Editar vehículo";
            PaginaActualVm = _vehiculoFormVm;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error abriendo la edición del vehículo {Id}", vehiculoId);
        }
    }

    private async Task AbrirCobrosAsync(long prestamoId)
    {
        try
        {
            PaginaActual = Pagina.Cobros;
            TituloPagina = TituloDe(Pagina.Cobros);
            await _cobrosVm.CargarAsync(prestamoId);
            PaginaActualVm = _cobrosVm;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error abriendo cobros para el préstamo {Id}", prestamoId);
        }
    }

    private static string TituloDe(Pagina pagina) => pagina switch
    {
        Pagina.Panel => "Panel de control",
        Pagina.Clientes => "Clientes",
        Pagina.Prestamos => "Préstamos",
        Pagina.NuevoPrestamo => "Nuevo préstamo",
        Pagina.Cobros => "Cobros",
        Pagina.Contratos => "Almacén de contratos",
        Pagina.Reportes => "Reportes",
        Pagina.Historial => "Historial",
        Pagina.Usuarios => "Usuarios",
        Pagina.Configuracion => "Configuración",
        Pagina.Vehiculos => "Inventario de vehículos",
        _ => string.Empty
    };
}
