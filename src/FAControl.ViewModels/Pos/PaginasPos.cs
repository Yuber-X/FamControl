using FAControl.Common;
using FAControl.Services.Pos;

namespace FAControl.ViewModels.Pos;

/// <summary>
/// Las páginas del punto de venta, agrupadas.
///
/// Existe para no meterle nueve parámetros más al constructor del shell, que ya
/// tiene veintiséis: el <see cref="MainViewModel"/> pide este objeto y adentro
/// encuentra todo lo del POS. También deja claro de un vistazo qué pantallas
/// pertenecen al punto de venta y cuáles a la suite.
///
/// Se registra como singleton igual que el resto de las páginas: conservan su
/// estado mientras dure la sesión.
/// </summary>
public class PaginasPos
{
    public PanelViewModel Panel { get; }
    public VenderViewModel Vender { get; }
    public ClientesViewModel Clientes { get; }
    public ClienteFormViewModel ClienteForm { get; }
    public ProductosViewModel Productos { get; }
    public ProductoFormViewModel ProductoForm { get; }
    public AlmacenViewModel Almacen { get; }
    public CaducidadViewModel Caducidad { get; }
    public ComprobantesViewModel Comprobantes { get; }
    public CuadreViewModel Cuadre { get; }
    public ReportesViewModel Reportes { get; }

    private readonly ConfiguracionNegocioService _config;

    public PaginasPos(PanelViewModel panel, VenderViewModel vender,
        ClientesViewModel clientes, ClienteFormViewModel clienteForm,
        ProductosViewModel productos, ProductoFormViewModel productoForm,
        AlmacenViewModel almacen, CaducidadViewModel caducidad,
        ComprobantesViewModel comprobantes, CuadreViewModel cuadre,
        ReportesViewModel reportes, ConfiguracionNegocioService config)
    {
        Panel = panel;
        Vender = vender;
        Clientes = clientes;
        ClienteForm = clienteForm;
        Productos = productos;
        ProductoForm = productoForm;
        Almacen = almacen;
        Caducidad = caducidad;
        Comprobantes = comprobantes;
        Cuadre = cuadre;
        Reportes = reportes;
        _config = config;
    }

    /// <summary>
    /// Deja el punto de venta listo para trabajar: carga la configuración del
    /// negocio (ITBIS, moneda, numeración de facturas) desde `pos500_db`.
    /// La llama el shell al entrar al modo, una sola vez.
    /// </summary>
    public Task PrepararAsync(CancellationToken ct = default) => _config.CargarAsync(ct);
}
