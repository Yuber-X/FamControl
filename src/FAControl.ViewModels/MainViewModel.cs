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
    Vehiculos,
    Ventas,
    Alquileres,
    Gastos,
    // POS-500 (2026-07-30). Panel, Clientes y Reportes se reusan de arriba:
    // son la misma pagina conceptual, resuelta segun el modo activo.
    Vender,
    Productos,
    Almacen,
    Caducidad,
    Comprobantes,
    Cuadre
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
    private readonly ExpedienteContratoViewModel _expedienteContratoVm;
    private readonly ConfiguracionViewModel _configuracionVm;
    private readonly VehiculosViewModel _vehiculosVm;
    private readonly VehiculoFormViewModel _vehiculoFormVm;
    private readonly VentasViewModel _ventasVm;
    private readonly VentaNuevaViewModel _ventaNuevaVm;
    private readonly AlquileresViewModel _alquileresVm;
    private readonly AlquilerNuevoViewModel _alquilerNuevoVm;
    private readonly AlquilerDetalleViewModel _alquilerDetalleVm;
    private readonly GastosViewModel _gastosVm;
    private readonly PanelDealViewModel _panelDealVm;
    private readonly VehiculoFichaViewModel _fichaVehiculoVm;
    private readonly VentaFinanciamientoViewModel _financiamientoVm;
    private readonly ContratosDealViewModel _contratosDealVm;
    private readonly ReportesDealViewModel _reportesDealVm;
    private readonly ClienteFichaDealViewModel _fichaDealVm;
    /// <summary>Las paginas del punto de venta, agrupadas (POS-500, 2026-07-30).</summary>
    private readonly Pos.PaginasPos _pos;

    public MainViewModel(PrestamosViewModel prestamosVm, PrestamoNuevoViewModel nuevoVm,
        PrestamoDetalleViewModel detalleVm, CobrosViewModel cobrosVm,
        ClientesViewModel clientesVm, ClienteFichaViewModel fichaVm, ClienteFormViewModel clienteFormVm,
        PanelViewModel panelVm, ReportesViewModel reportesVm, HistorialViewModel historialVm,
        UsuariosViewModel usuariosVm, ContratosViewModel contratosVm,
        ExpedienteContratoViewModel expedienteContratoVm, ConfiguracionViewModel configuracionVm,
        VehiculosViewModel vehiculosVm, VehiculoFormViewModel vehiculoFormVm,
        VentasViewModel ventasVm, VentaNuevaViewModel ventaNuevaVm,
        AlquileresViewModel alquileresVm, AlquilerNuevoViewModel alquilerNuevoVm,
        AlquilerDetalleViewModel alquilerDetalleVm, GastosViewModel gastosVm,
        PanelDealViewModel panelDealVm, VehiculoFichaViewModel fichaVehiculoVm,
        VentaFinanciamientoViewModel financiamientoVm,
        ContratosDealViewModel contratosDealVm, ReportesDealViewModel reportesDealVm,
        ClienteFichaDealViewModel fichaDealVm, Pos.PaginasPos pos)
    {
        _panelVm = panelVm;
        _panelDealVm = panelDealVm;
        _fichaVehiculoVm = fichaVehiculoVm;
        _financiamientoVm = financiamientoVm;
        _contratosDealVm = contratosDealVm;
        _reportesDealVm = reportesDealVm;
        _reportesVm = reportesVm;
        _historialVm = historialVm;
        _usuariosVm = usuariosVm;
        _contratosVm = contratosVm;
        _expedienteContratoVm = expedienteContratoVm;
        // Contratos → archivos del contrato → ficha del cliente (2026-08-01)
        _contratosVm.ArchivosSolicitados += id => _ = AbrirExpedienteContratoAsync(id);
        _expedienteContratoVm.VolverSolicitado += () => _ = NavegarAsync(Pagina.Contratos);
        _expedienteContratoVm.FichaClienteSolicitada += id => _ = AbrirFichaAsync(id);
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
        // Ficha PROPIA del dealer (2026-07-27): mismos eventos, otra pantalla
        _fichaDealVm = fichaDealVm;
        _fichaDealVm.EditarSolicitado += id => _ = AbrirClienteEdicionAsync(id);
        _fichaDealVm.VolverSolicitado += () => _ = NavegarAsync(Pagina.Clientes);
        _fichaDealVm.FichaVehiculoSolicitada += id => _ = AbrirFichaVehiculoAsync(id);
        _clienteFormVm.Guardado += id => _ = AbrirFichaAsync(id);
        _clienteFormVm.Cancelado += () => _ = NavegarAsync(Pagina.Clientes);

        // DealerControl: inventario → alta/edición → vuelta a la lista
        _ventasVm = ventasVm;
        _ventaNuevaVm = ventaNuevaVm;
        _alquileresVm = alquileresVm;
        _alquilerNuevoVm = alquilerNuevoVm;
        _alquilerDetalleVm = alquilerDetalleVm;
        _gastosVm = gastosVm;
        _vehiculosVm.NuevoSolicitado += AbrirVehiculoNuevo;
        _vehiculosVm.EditarSolicitado += id => _ = AbrirVehiculoEdicionAsync(id);
        _vehiculosVm.FichaSolicitada += id => _ = AbrirFichaVehiculoAsync(id);
        _fichaVehiculoVm.VolverSolicitado += () => _ = NavegarAsync(Pagina.Vehiculos);
        _vehiculoFormVm.Guardado += id => _ = NavegarAsync(Pagina.Vehiculos);
        _vehiculoFormVm.Cancelado += () => _ = NavegarAsync(Pagina.Vehiculos);

        // Ventas al contado: lista → nueva venta → vuelta
        _ventasVm.NuevoSolicitado += () => _ = AbrirVentaNuevaAsync();
        _ventasVm.FinanciamientoSolicitado += id => _ = AbrirFinanciamientoAsync(id);
        _financiamientoVm.VolverSolicitado += () => _ = NavegarAsync(Pagina.Ventas);
        // Expediente de contratos del dealer: "ver detalles" abre el financiamiento
        _contratosDealVm.DetalleSolicitado += id => _ = AbrirFinanciamientoAsync(id);
        _contratosDealVm.DetalleAlquilerSolicitado += id => _ = AbrirAlquilerDetalleAsync(id);
        // Registrada la venta se va DIRECTO a su financiamiento (033): ahi se
        // imprimen los papeles, se archivan solos y queda a mano subir lo que
        // trajo el cliente. Volver a la lista obligaba a buscar la venta recien
        // hecha para poder seguir.
        _ventaNuevaVm.Registrado += id => _ = AbrirFinanciamientoAsync(id, reciénRegistrada: true);
        _ventaNuevaVm.Cancelado += () => _ = NavegarAsync(Pagina.Ventas);

        // Alquileres: lista → nuevo alquiler → vuelta
        _alquileresVm.NuevoSolicitado += () => _ = AbrirAlquilerNuevoAsync();
        _alquileresVm.DetalleSolicitado += id => _ = AbrirAlquilerDetalleAsync(id);
        _alquilerDetalleVm.VolverSolicitado += () => _ = NavegarAsync(Pagina.Alquileres);
        _alquilerNuevoVm.Registrado += () => _ = NavegarAsync(Pagina.Alquileres);
        _alquilerNuevoVm.Cancelado += () => _ = NavegarAsync(Pagina.Alquileres);

        // Punto de venta: listas → formulario → vuelta a la lista
        _pos = pos;
        _pos.Clientes.NuevoSolicitado += AbrirClientePosNuevo;
        _pos.Clientes.EdicionSolicitada += id => _ = AbrirClientePosAsync(id);
        _pos.ClienteForm.Guardado += id => _ = NavegarAsync(Pagina.Clientes);
        _pos.ClienteForm.Cancelado += () => _ = NavegarAsync(Pagina.Clientes);
        _pos.Productos.NuevoSolicitado += AbrirProductoPosNuevo;
        _pos.Productos.EdicionSolicitada += id => _ = AbrirProductoPosAsync(id);
        _pos.ProductoForm.Guardado += id => _ = NavegarAsync(Pagina.Productos);
        _pos.ProductoForm.Cancelado += () => _ = NavegarAsync(Pagina.Productos);
    }

    // ---------- Punto de venta ----------

    private void AbrirClientePosNuevo()
    {
        _pos.ClienteForm.PrepararNuevo();
        TituloPagina = "Nuevo cliente";
        PaginaActualVm = _pos.ClienteForm;
    }

    private void AbrirProductoPosNuevo()
    {
        _pos.ProductoForm.PrepararNuevo();
        TituloPagina = "Nuevo producto";
        PaginaActualVm = _pos.ProductoForm;
    }

    private async Task AbrirClientePosAsync(long id)
    {
        await _pos.ClienteForm.PrepararEdicionAsync(id);
        TituloPagina = "Editar cliente";
        PaginaActualVm = _pos.ClienteForm;
    }

    private async Task AbrirProductoPosAsync(long id)
    {
        await _pos.ProductoForm.PrepararEdicionAsync(id);
        TituloPagina = "Editar producto";
        PaginaActualVm = _pos.ProductoForm;
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
    /// <summary>Punto de venta (POS-500, 2026-07-30): su propia base y sus propias pantallas.</summary>
    public bool EsPos500 => Modo == ModoApp.Pos500;
    public bool EsAutoControl => Modo == ModoApp.AutoControl;
    /// <summary>Clientes, cobros, contratos y reportes son compartidos por los modos de crédito.</summary>
    private bool EsCredito => EsPrestControl || EsAutoControl;

    // ------------------------------------------------------------------
    // Visibilidad del sidebar por MODO + permiso (multicuentas 2026-07-16,
    // multimodo Tier 5). Esto es UX, NO seguridad: la regla la aplica el Service.
    //   PrestControl: préstamos personales.  AutoControl: créditos vehiculares
    //   (reusa la misma maquinaria, filtrada).  DealerControl: inventario/ventas.
    //   Historial / Usuarios / Configuración son transversales a los modos.
    // ------------------------------------------------------------------
    /// <summary>PrestControl: panel de cartera. DealControl: panel del dealer (2026-07-25) — el Vendedor no lo ve.</summary>
    public bool PuedeVerPanel => (EsPrestControl || EsDealerControl || EsPos500)
                                 && SesionActual.TienePermiso(Permisos.Panel);
    /// <summary>
    /// Clientes existe en los TRES modos, pero cada estancia ve solo LOS SUYOS
    /// (aislamiento por ámbito, 2026-07-18). Así Dealer/Auto registran sus
    /// propios clientes sin mezclarse con los de PrestControl.
    /// </summary>
    public bool PuedeVerClientes => SesionActual.TienePermiso(Permisos.Clientes);
    public bool PuedeVerPrestamos => EsPrestControl && SesionActual.TienePermiso(Permisos.Prestamos);
    public bool PuedeVerNuevoPrestamo => EsPrestControl && SesionActual.TienePermiso(Permisos.PrestamosCrear);
    /// <summary>AutoControl: lista de créditos vehiculares (misma página, filtrada).</summary>
    public bool PuedeVerVentasFinanciadas => EsAutoControl && SesionActual.TienePermiso(Permisos.Prestamos);
    /// <summary>AutoControl: alta de crédito vehicular (mismo wizard, con picker de vehículo).</summary>
    public bool PuedeVerNuevaVentaFinanciada => EsAutoControl && SesionActual.TienePermiso(Permisos.PrestamosCrear);
    public bool PuedeVerCobros => EsCredito && SesionActual.TienePermiso(Permisos.Cobros);
    /// <summary>
    /// Contratos: en crédito es el pagaré del préstamo; en DealControl es el
    /// expediente de la venta (2026-07-25), con su propio permiso ('ventas').
    /// </summary>
    public bool PuedeVerContratos => EsDealerControl
        ? SesionActual.TienePermiso(Permisos.Ventas)
        : EsCredito && SesionActual.TienePermiso(Permisos.Contratos);
    /// <summary>Reportes: cada estancia tiene los suyos, nunca datos cruzados.</summary>
    public bool PuedeVerReportes => (EsCredito || EsDealerControl || EsPos500)
        && SesionActual.TienePermiso(Permisos.Reportes);
    /// <summary>Inventario, ventas, alquileres y gastos: exclusivos de DealControl, con permisos FINOS (roles por modo).</summary>
    public bool PuedeVerVehiculos => EsDealerControl && SesionActual.TienePermiso(Permisos.Inventario);
    public bool PuedeVerVentas => EsDealerControl && SesionActual.TienePermiso(Permisos.Ventas);
    public bool PuedeVerAlquileres => EsDealerControl && SesionActual.TienePermiso(Permisos.Alquileres);
    public bool PuedeVerGastos => EsDealerControl && SesionActual.TienePermiso(Permisos.Gastos);

    // ---- Punto de venta ----
    public bool PuedeVerVender => EsPos500 && SesionActual.TienePermiso(Permisos.Vender);
    public bool PuedeVerProductos => EsPos500 && SesionActual.TienePermiso(Permisos.Productos);
    public bool PuedeVerAlmacen => EsPos500 && SesionActual.TienePermiso(Permisos.Almacen);
    public bool PuedeVerCaducidad => EsPos500 && SesionActual.TienePermiso(Permisos.Caducidad);
    public bool PuedeVerComprobantes => EsPos500 && SesionActual.TienePermiso(Permisos.Comprobantes);
    public bool PuedeVerCuadre => EsPos500 && SesionActual.TienePermiso(Permisos.Cuadre);
    public bool PuedeVerHistorial => SesionActual.TienePermiso(Permisos.Historial);
    public bool PuedeVerUsuarios => SesionActual.TienePermiso(Permisos.Usuarios);
    /// <summary>Configuración es EXCLUSIVA de Admin (regla del cliente).</summary>
    public bool PuedeVerConfiguracion => SesionActual.EsAdmin
                                         && SesionActual.TienePermiso(Permisos.Configuracion);

    /// <summary>Fija el modo activo (lo llama App al abrir el shell) y refresca el sidebar.</summary>
    public void EstablecerModo(ModoApp modo)
    {
        Modo = modo;
        // AutoControl reusa el VM de préstamos, filtrado a créditos vehiculares.
        _prestamosVm.SoloVehiculares = EsAutoControl ? true : (EsPrestControl ? false : null);
        _nuevoVm.EsVehicular = EsAutoControl;
        // El toggle de tema debe reflejar la estancia actual (DealControl oscuro).
        _configuracionVm.SincronizarTema();
        // Y la casilla de arranque directo, la estancia que quedó fijada.
        _configuracionVm.SincronizarArranque();
        // Y el historial arranca acotado a esta estancia (025).
        _historialVm.SincronizarModo();
        OnPropertyChanged(nameof(Modo));
        OnPropertyChanged(nameof(EsPrestControl));
        OnPropertyChanged(nameof(EsDealerControl));
        OnPropertyChanged(nameof(EsAutoControl));
        OnPropertyChanged(nameof(EsPos500));
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
        OnPropertyChanged(nameof(PuedeVerVentasFinanciadas));
        OnPropertyChanged(nameof(PuedeVerNuevaVentaFinanciada));
        OnPropertyChanged(nameof(PuedeVerCobros));
        OnPropertyChanged(nameof(PuedeVerContratos));
        OnPropertyChanged(nameof(PuedeVerReportes));
        OnPropertyChanged(nameof(PuedeVerVehiculos));
        OnPropertyChanged(nameof(PuedeVerVentas));
        OnPropertyChanged(nameof(PuedeVerAlquileres));
        OnPropertyChanged(nameof(PuedeVerGastos));
        OnPropertyChanged(nameof(PuedeVerVender));
        OnPropertyChanged(nameof(PuedeVerProductos));
        OnPropertyChanged(nameof(PuedeVerAlmacen));
        OnPropertyChanged(nameof(PuedeVerCaducidad));
        OnPropertyChanged(nameof(PuedeVerComprobantes));
        OnPropertyChanged(nameof(PuedeVerCuadre));
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
    public async Task InicializarAsync()
    {
        // El punto de venta necesita su configuracion (ITBIS, moneda, numeracion
        // de facturas) antes de mostrar nada: sin eso no puede facturar.
        if (EsPos500)
            await _pos.PrepararAsync();

        await NavegarAsync(
            EsPos500 ? (PuedeVerVender ? Pagina.Vender : Pagina.Panel)
          : EsDealerControl ? (PuedeVerPanel ? Pagina.Panel : Pagina.Vehiculos)
          : EsAutoControl ? Pagina.Prestamos          // ventas financiadas
          : Pagina.Panel);
    }

    [RelayCommand]
    private async Task NavegarAsync(Pagina destino)
    {
        try
        {
            PaginaActual = destino;
            TituloPagina = TituloDe(destino);

            switch (destino)
            {
                case Pagina.Panel when EsDealerControl:
                    // Panel PROPIO del dealer (2026-07-25): nunca datos de Prest
                    await _panelDealVm.CargarAsync();
                    PaginaActualVm = _panelDealVm;
                    break;
                case Pagina.Panel when EsPos500:
                    await _pos.Panel.RefrescarAsync();
                    PaginaActualVm = _pos.Panel;
                    break;
                case Pagina.Panel:
                    await _panelVm.CargarAsync();
                    PaginaActualVm = _panelVm;
                    break;
                case Pagina.Clientes when EsPos500:
                    await _pos.Clientes.RefrescarAsync();
                    PaginaActualVm = _pos.Clientes;
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
                case Pagina.Reportes when EsPos500:
                    await _pos.Reportes.RefrescarAsync();
                    PaginaActualVm = _pos.Reportes;
                    break;
                case Pagina.Reportes when EsDealerControl:
                    // Reportes PROPIOS del dealer (2026-07-25): nunca datos de Prest
                    await _reportesDealVm.CargarAsync();
                    PaginaActualVm = _reportesDealVm;
                    break;
                case Pagina.Reportes:
                    await _reportesVm.CargarAsync();
                    PaginaActualVm = _reportesVm;
                    break;
                case Pagina.Historial:
                    await _historialVm.CargarAsync();
                    PaginaActualVm = _historialVm;
                    break;
                case Pagina.Contratos when EsDealerControl:
                    // Expediente de contratos del dealer (2026-07-25)
                    await _contratosDealVm.CargarAsync();
                    PaginaActualVm = _contratosDealVm;
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
                case Pagina.Ventas:
                    await _ventasVm.CargarAsync();
                    PaginaActualVm = _ventasVm;
                    break;
                case Pagina.Alquileres:
                    await _alquileresVm.CargarAsync();
                    PaginaActualVm = _alquileresVm;
                    break;
                case Pagina.Gastos:
                    await _gastosVm.CargarAsync();
                    PaginaActualVm = _gastosVm;
                    break;

                // ---- Punto de venta. Panel, Clientes y Reportes comparten
                // pagina con la suite y se resuelven por modo, como el Panel del
                // dealer: nunca se mezclan los datos de una estancia con otra.
                case Pagina.Vender:
                    await _pos.Vender.RefrescarAsync();
                    PaginaActualVm = _pos.Vender;
                    break;
                case Pagina.Productos:
                    await _pos.Productos.RefrescarAsync();
                    PaginaActualVm = _pos.Productos;
                    break;
                case Pagina.Almacen:
                    await _pos.Almacen.RefrescarAsync();
                    PaginaActualVm = _pos.Almacen;
                    break;
                case Pagina.Caducidad:
                    await _pos.Caducidad.RefrescarAsync();
                    PaginaActualVm = _pos.Caducidad;
                    break;
                case Pagina.Comprobantes:
                    await _pos.Comprobantes.RefrescarAsync();
                    PaginaActualVm = _pos.Comprobantes;
                    break;
                case Pagina.Cuadre:
                    await _pos.Cuadre.RefrescarAsync();
                    PaginaActualVm = _pos.Cuadre;
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

    /// <summary>Plazos de una venta financiada del dealer (016).</summary>
    private async Task AbrirFinanciamientoAsync(long ventaId, bool reciénRegistrada = false)
    {
        try
        {
            PaginaActual = Pagina.Ventas;
            TituloPagina = "Financiamiento de la venta";
            await _financiamientoVm.CargarAsync(ventaId, reciénRegistrada);
            PaginaActualVm = _financiamientoVm;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error abriendo el financiamiento de la venta {Id}", ventaId);
        }
    }

    /// <summary>Ficha completa del vehículo (pedido 2026-07-25).</summary>
    private async Task AbrirFichaVehiculoAsync(long vehiculoId)
    {
        try
        {
            PaginaActual = Pagina.Vehiculos;
            TituloPagina = "Ficha del vehículo";
            await _fichaVehiculoVm.CargarAsync(vehiculoId);
            PaginaActualVm = _fichaVehiculoVm;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error abriendo la ficha del vehículo {Id}", vehiculoId);
        }
    }

    /// <summary>
    /// Archivos de UN contrato (2026-08-01). Es una pagina propia: se pidio
    /// expresamente que no fuera una ventana aparte.
    /// </summary>
    private async Task AbrirExpedienteContratoAsync(long prestamoId)
    {
        try
        {
            PaginaActual = Pagina.Contratos;
            TituloPagina = "Expediente del cliente";
            await _expedienteContratoVm.CargarAsync(prestamoId);
            PaginaActualVm = _expedienteContratoVm;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error abriendo el expediente del contrato {Id}", prestamoId);
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
            // DealControl tiene su propia ficha: métricas de compras/alquileres
            // y el grid de SUS vehículos, no la de préstamos (pedido 2026-07-27)
            if (EsDealerControl)
            {
                await _fichaDealVm.CargarAsync(clienteId);
                PaginaActualVm = _fichaDealVm;
                return;
            }
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

    private async Task AbrirVentaNuevaAsync()
    {
        await _ventaNuevaVm.CargarAsync();
        PaginaActual = Pagina.Ventas;
        TituloPagina = "Nueva venta al contado";
        PaginaActualVm = _ventaNuevaVm;
    }

    /// <summary>Detalle de un alquiler (031): editar y cerrar viven ahi adentro.</summary>
    private async Task AbrirAlquilerDetalleAsync(long alquilerId)
    {
        try
        {
            PaginaActual = Pagina.Alquileres;
            TituloPagina = "Detalle del alquiler";
            await _alquilerDetalleVm.CargarAsync(alquilerId);
            PaginaActualVm = _alquilerDetalleVm;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error abriendo el detalle del alquiler {Id}", alquilerId);
        }
    }

    private async Task AbrirAlquilerNuevoAsync()
    {
        await _alquilerNuevoVm.CargarAsync();
        PaginaActual = Pagina.Alquileres;
        TituloPagina = "Nuevo alquiler";
        PaginaActualVm = _alquilerNuevoVm;
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

    private string TituloDe(Pagina pagina) => pagina switch
    {
        Pagina.Panel => EsPos500 ? "Panel del punto de venta" : "Panel de control",
        Pagina.Clientes => "Clientes",
        // En AutoControl los préstamos son ventas financiadas de vehículos.
        Pagina.Prestamos => EsAutoControl ? "Ventas financiadas" : "Préstamos",
        Pagina.NuevoPrestamo => EsAutoControl ? "Nueva venta financiada" : "Nuevo préstamo",
        Pagina.Cobros => "Cobros",
        // En DealControl el contrato es el expediente de la venta, no el pagaré
        Pagina.Contratos => EsDealerControl ? "Contratos del dealer" : "Almacén de contratos",
        Pagina.Reportes => EsDealerControl ? "Reportes del dealer" : EsPos500 ? "Reportes de ventas" : "Reportes",
        Pagina.Ventas when EsDealerControl => "Ventas",
        Pagina.Historial => "Historial",
        Pagina.Usuarios => "Usuarios",
        Pagina.Configuracion => "Configuración",
        Pagina.Vehiculos => "Inventario de vehículos",
        Pagina.Ventas => "Ventas al contado",
        Pagina.Alquileres => "Alquileres (rent a car)",
        Pagina.Gastos => "Gestión de importación",
        // Punto de venta
        Pagina.Vender => "Vender",
        Pagina.Productos => "Productos",
        Pagina.Almacen => "Almacén",
        Pagina.Caducidad => "Control de caducidad",
        Pagina.Comprobantes => "Buscar comprobante",
        Pagina.Cuadre => "Cuadre de caja",
        _ => string.Empty
    };
}
