// Portado de POS500.ViewModels el 2026-07-30 al integrar el punto de venta a la
// suite. Usa el SesionActual, los permisos y el IDialogService de FAControl.
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAControl.Common;
using FAControl.Models.Pos;
using FAControl.Services.Pos;
using Serilog;

namespace FAControl.ViewModels.Pos;

/// <summary>Línea del carrito en pantalla. La cantidad se ajusta con +/−.</summary>
public partial class CarritoLinea : ObservableObject
{
    public required long ProductoId { get; init; }
    public required string Nombre { get; init; }
    public required decimal Precio { get; init; }
    public required int StockDisponible { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Subtotal))]
    private int _cantidad = 1;

    public decimal Subtotal => Cantidad * Precio;
}

/// <summary>
/// Pantalla Vender. La selección de cliente es OPCIONAL y puede ocultarse
/// por completo desde Configuración (regla Yuber: solo se ve el código de
/// compra, que es el número de factura del ticket). Los cálculos los hace
/// VentaService — este VM solo orquesta la UI.
/// </summary>
public partial class VenderViewModel : ObservableObject, IPaginaAsincrona
{
    private static readonly CultureInfo CulturaDo = CultureInfo.GetCultureInfo("es-DO");
    private readonly VentaService _ventas;
    private readonly ProductoService _productos;
    private readonly ClienteService _clientes;
    private readonly ConfiguracionNegocioService _config;
    private readonly IDialogService _dialogos;
    private IReadOnlyList<Producto> _catalogo = [];

    /// <summary>La App abre el TicketWindow al registrarse una venta.</summary>
    public event Action<VentaResultado>? VentaRegistrada;

    public VenderViewModel(VentaService ventas, ProductoService productos,
        ClienteService clientes, ConfiguracionNegocioService config, IDialogService dialogos)
    {
        _ventas = ventas;
        _productos = productos;
        _clientes = clientes;
        _config = config;
        _dialogos = dialogos;

        MetodosPago =
        [
            new Opcion<MetodoPagoFactura>(MetodoPagoFactura.Efectivo, "Efectivo"),
            new Opcion<MetodoPagoFactura>(MetodoPagoFactura.Tarjeta, "Tarjeta"),
            new Opcion<MetodoPagoFactura>(MetodoPagoFactura.Transferencia, "Transferencia"),
            new Opcion<MetodoPagoFactura>(MetodoPagoFactura.Mixto, "Mixto")
        ];
        _metodoSeleccionado = MetodosPago[0];
    }

    public ObservableCollection<ProductoFila> Resultados { get; } = [];
    public ObservableCollection<CarritoLinea> Carrito { get; } = [];
    public ObservableCollection<Opcion<long?>> Clientes { get; } = [];
    public IReadOnlyList<Opcion<MetodoPagoFactura>> MetodosPago { get; }

    [ObservableProperty] private string _textoBusqueda = string.Empty;
    [ObservableProperty] private Opcion<long?>? _clienteSeleccionado;
    [ObservableProperty] private Opcion<MetodoPagoFactura> _metodoSeleccionado;
    [ObservableProperty] private string _efectivoTexto = string.Empty;
    [ObservableProperty] private bool _mostrarCliente = true;
    [ObservableProperty] private bool _mostrarItbis = true;
    [ObservableProperty] private decimal _subtotal;
    [ObservableProperty] private decimal _itbis;
    [ObservableProperty] private decimal _total;
    [ObservableProperty] private string _itbisEtiqueta = "ITBIS (18%)";
    [ObservableProperty] private string _cambioTexto = "—";
    [ObservableProperty] private bool _ocupado;

    public bool PideEfectivo =>
        MetodoSeleccionado.Valor is MetodoPagoFactura.Efectivo or MetodoPagoFactura.Mixto;

    partial void OnTextoBusquedaChanged(string value) => Buscar();
    partial void OnEfectivoTextoChanged(string value) => ActualizarCambio();
    partial void OnMetodoSeleccionadoChanged(Opcion<MetodoPagoFactura> value)
    {
        OnPropertyChanged(nameof(PideEfectivo));
        ActualizarCambio();
    }

    public async Task RefrescarAsync()
    {
        try
        {
            MostrarCliente = _config.Actual.MostrarClienteEnVenta;
            MostrarItbis = _config.Actual.ItbisActivo;
            ItbisEtiqueta = $"ITBIS ({_config.Actual.ItbisTasa:0.##}%)";
            Recalcular();   // por si cambió la tasa/activación desde Configuración
            _catalogo = await _productos.ObtenerTodosAsync();

            if (MostrarCliente)
            {
                var lista = await _clientes.ObtenerTodosAsync();
                Clientes.Clear();
                Clientes.Add(new Opcion<long?>(null, "Consumidor final"));
                foreach (var c in lista)
                    Clientes.Add(new Opcion<long?>(c.Id, c.Nombre));
                ClienteSeleccionado ??= Clientes[0];
            }
            Buscar();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cargando la pantalla Vender");
            _dialogos.MostrarError("Vender", $"No se pudo cargar el catálogo.\n\n{ex.Message}");
        }
    }

    private void Buscar()
    {
        var filtro = TextoBusqueda.Trim();
        Resultados.Clear();
        if (string.IsNullOrEmpty(filtro))
            return;

        // Código exacto primero (pistola de código de barras), luego por nombre
        var hoy = FechaNegocio.Hoy;
        var exacto = _catalogo.FirstOrDefault(p =>
            string.Equals(p.Codigo, filtro, StringComparison.OrdinalIgnoreCase));
        if (exacto is not null)
        {
            Agregar(new ProductoFila(exacto, hoy));
            TextoBusqueda = string.Empty;   // listo para el próximo escaneo
            return;
        }

        foreach (var p in _catalogo
                     .Where(p => p.Nombre.Contains(filtro, StringComparison.OrdinalIgnoreCase) ||
                                 (p.Codigo?.Contains(filtro, StringComparison.OrdinalIgnoreCase) ?? false))
                     .Take(8))
            Resultados.Add(new ProductoFila(p, hoy));
    }

    [RelayCommand]
    private void Agregar(ProductoFila? fila)
    {
        if (fila is null)
            return;

        var existente = Carrito.FirstOrDefault(l => l.ProductoId == fila.Id);
        var enCarrito = existente?.Cantidad ?? 0;
        if (enCarrito + 1 > fila.Cantidad)
        {
            _dialogos.MostrarError("Stock",
                $"Solo quedan {fila.Cantidad} unidades de {fila.Nombre}.");
            return;
        }

        if (existente is not null)
            existente.Cantidad++;
        else
            Carrito.Add(new CarritoLinea
            {
                ProductoId = fila.Id,
                Nombre = fila.Nombre,
                Precio = fila.Precio,
                StockDisponible = fila.Cantidad
            });
        Recalcular();
    }

    [RelayCommand]
    private void Mas(CarritoLinea? linea)
    {
        if (linea is null) return;
        if (linea.Cantidad + 1 > linea.StockDisponible)
        {
            _dialogos.MostrarError("Stock", $"Solo quedan {linea.StockDisponible} unidades de {linea.Nombre}.");
            return;
        }
        linea.Cantidad++;
        Recalcular();
    }

    [RelayCommand]
    private void Menos(CarritoLinea? linea)
    {
        if (linea is null) return;
        if (linea.Cantidad <= 1)
            Carrito.Remove(linea);
        else
            linea.Cantidad--;
        Recalcular();
    }

    [RelayCommand]
    private void Quitar(CarritoLinea? linea)
    {
        if (linea is null) return;
        Carrito.Remove(linea);
        Recalcular();
    }

    private void Recalcular()
    {
        var totales = VentaService.CalcularTotales(
            LineasActuales(), _config.Actual.ItbisTasaEfectiva, _config.Actual.Redondeo);
        Subtotal = totales.Subtotal;
        Itbis = totales.Itbis;
        Total = totales.Total;
        ActualizarCambio();
    }

    private void ActualizarCambio()
    {
        if (MetodoSeleccionado.Valor == MetodoPagoFactura.Efectivo &&
            decimal.TryParse(EfectivoTexto, NumberStyles.Number, CulturaDo, out var efectivo) &&
            efectivo >= Total && Total > 0)
        {
            CambioTexto = VentaService.CalcularCambio(efectivo, Total).ToString("N2", CulturaDo);
        }
        else
        {
            CambioTexto = "—";
        }
    }

    private List<VentaLinea> LineasActuales() =>
        [.. Carrito.Select(l => new VentaLinea(l.ProductoId, l.Nombre, l.Cantidad, l.Precio))];

    [RelayCommand]
    private async Task CobrarAsync()
    {
        if (Carrito.Count == 0)
        {
            _dialogos.MostrarError("Vender", "El carrito está vacío.");
            return;
        }

        decimal? efectivo = null;
        if (PideEfectivo)
        {
            if (!decimal.TryParse(EfectivoTexto, NumberStyles.Number, CulturaDo, out var monto))
            {
                _dialogos.MostrarError("Vender", "Indica el efectivo recibido (ej: 500.00).");
                return;
            }
            efectivo = monto;
        }

        var solicitud = new VentaSolicitud(LineasActuales(),
            MostrarCliente ? ClienteSeleccionado?.Valor : null,
            MetodoSeleccionado.Valor, efectivo);

        try
        {
            Ocupado = true;
            var resultado = await _ventas.RegistrarVentaAsync(solicitud);

            Carrito.Clear();
            EfectivoTexto = string.Empty;
            TextoBusqueda = string.Empty;
            ClienteSeleccionado = Clientes.FirstOrDefault();
            Recalcular();
            await RefrescarAsync();   // stock actualizado para la próxima venta

            VentaRegistrada?.Invoke(resultado);
        }
        catch (ArgumentException ex)
        {
            _dialogos.MostrarError("Vender", ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            // Stock insuficiente u otra regla: la venta se revirtió completa
            _dialogos.MostrarError("Vender", ex.Message);
            await RefrescarAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error registrando la venta");
            _dialogos.MostrarError("Vender", $"No se pudo registrar la venta.\n\n{ex.Message}");
        }
        finally
        {
            Ocupado = false;
        }
    }
}
