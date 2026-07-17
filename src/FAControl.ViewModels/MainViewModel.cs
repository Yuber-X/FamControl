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
    Reportes,
    Historial,
    Usuarios,
    Configuracion
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
    private readonly ConfiguracionViewModel _configuracionVm;

    public MainViewModel(PrestamosViewModel prestamosVm, PrestamoNuevoViewModel nuevoVm,
        PrestamoDetalleViewModel detalleVm, CobrosViewModel cobrosVm,
        ClientesViewModel clientesVm, ClienteFichaViewModel fichaVm, ClienteFormViewModel clienteFormVm,
        PanelViewModel panelVm, ReportesViewModel reportesVm, HistorialViewModel historialVm,
        UsuariosViewModel usuariosVm, ConfiguracionViewModel configuracionVm)
    {
        _panelVm = panelVm;
        _reportesVm = reportesVm;
        _historialVm = historialVm;
        _usuariosVm = usuariosVm;
        _configuracionVm = configuracionVm;
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
    }

    [ObservableProperty]
    private Pagina _paginaActual = Pagina.Panel;

    [ObservableProperty]
    private object? _paginaActualVm;

    [ObservableProperty]
    private string _tituloPagina = "Panel de control";

    public string NombreUsuario => SesionActual.Nombre;
    public string RolUsuario => SesionActual.Rol;

    // ------------------------------------------------------------------
    // Visibilidad del sidebar por permiso (multicuentas 2026-07-16).
    // Esto es UX, NO seguridad: la regla de verdad la aplica cada Service.
    // ------------------------------------------------------------------
    public bool PuedeVerPanel => SesionActual.TienePermiso(Permisos.Panel);
    public bool PuedeVerClientes => SesionActual.TienePermiso(Permisos.Clientes);
    public bool PuedeVerPrestamos => SesionActual.TienePermiso(Permisos.Prestamos);
    public bool PuedeVerNuevoPrestamo => SesionActual.TienePermiso(Permisos.PrestamosCrear);
    public bool PuedeVerCobros => SesionActual.TienePermiso(Permisos.Cobros);
    public bool PuedeVerReportes => SesionActual.TienePermiso(Permisos.Reportes);
    public bool PuedeVerHistorial => SesionActual.TienePermiso(Permisos.Historial);
    public bool PuedeVerUsuarios => SesionActual.TienePermiso(Permisos.Usuarios);
    /// <summary>Configuración es EXCLUSIVA de Admin (regla del cliente).</summary>
    public bool PuedeVerConfiguracion => SesionActual.EsAdmin
                                         && SesionActual.TienePermiso(Permisos.Configuracion);

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
        OnPropertyChanged(nameof(PuedeVerReportes));
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

    /// <summary>Carga inicial del shell (llamada al abrir la ventana principal).</summary>
    public Task InicializarAsync() => NavegarAsync(Pagina.Panel);

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
                case Pagina.Usuarios:
                    await _usuariosVm.CargarAsync();
                    PaginaActualVm = _usuariosVm;
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
        Pagina.Reportes => "Reportes",
        Pagina.Historial => "Historial",
        Pagina.Usuarios => "Usuarios",
        Pagina.Configuracion => "Configuración",
        _ => string.Empty
    };
}
