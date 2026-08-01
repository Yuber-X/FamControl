using FluentAssertions;
using MySqlConnector;
using FAControl.Common;
using FAControl.Data;
using FAControl.Data.Pos;
using FAControl.Models.Pos;
using FAControl.Services;
using FAControl.Services.Pos;

namespace FAControl.Data.Tests;

/// <summary>
/// El punto de venta de punta a punta. Desde el 2026-07-30 sus tablas viven en
/// la MISMA base que el resto de la suite, con prefijo `pos_` (024).
///
/// Lo que se verifica:
///  * una venta descuenta stock, numera la factura y deja su línea en el
///    historial —que es el mismo del resto de la suite—;
///  * anular devuelve el stock y queda como acción 'anular' en ese historial;
///  * si la venta falla, no queda ni la factura ni la línea de auditoría.
///
/// Requiere MySQL local (root/root). Recrea su base en cada corrida.
/// </summary>
[Collection(ColeccionSesionData.Nombre)]   // SesionActual es global
public class VentaPosTests : IAsyncLifetime
{
    private const string CadenaServidor = "Server=localhost;Port=3306;Uid=root;Pwd=root;";
    private const string Bd = "facontrol_pos_venta_test";
    private const string Cadena = CadenaServidor + $"Database={Bd};";

    private VentaService _ventas = null!;
    private FacturaService _facturas = null!;
    private ProductoService _productos = null!;
    private AuditoriaService _auditoria = null!;
    private long _productoId;

    public async Task InitializeAsync()
    {
        // Se borra ANTES de crear, no solo al terminar: si una corrida se corta
        // a la mitad, la base queda viva y 001 revienta con "Table 'rol' already
        // exists" en todas las corridas siguientes hasta que alguien la borre a
        // mano. Es el mismo patron del resto de las pruebas de integracion.
        await BorrarBaseAsync();

        // Una sola base para todo: usuarios y auditoría de la suite, y las
        // tablas pos_* del punto de venta
        await new VerificadorBaseDatos(Cadena).CrearEsquemaAsync();

        var suite = new ConexionFactory(Cadena);
        _auditoria = new AuditoriaService(new AuditoriaRepository(suite),
            new SesionRepository(suite), new UsuarioRepository(suite));

        var facturaRepo = new FacturaRepository(suite);
        var productoRepo = new ProductoRepository(suite);
        var config = new ConfiguracionNegocioService(
            new ConfiguracionNegocioRepository(suite), _auditoria);
        await config.CargarAsync();

        // Cliente del MOSTRADOR: el del POS, no el de préstamos (mismo nombre de
        // clase en distinto namespace, por eso el calificado)
        _ventas = new VentaService(facturaRepo,
            new FAControl.Data.Pos.ClienteRepository(suite), config, _auditoria);
        _facturas = new FacturaService(facturaRepo, _auditoria);
        _productos = new ProductoService(productoRepo, _auditoria);

        // El cajero es un usuario de FAControl, no del punto de venta
        await using var conexion = new MySqlConnection(Cadena);
        await conexion.OpenAsync();
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = """
            INSERT INTO usuario (username, password_hash, nombre)
            VALUES ('cajero', 'hash-de-prueba', 'Cajero Prueba');
            SELECT LAST_INSERT_ID();
            """;
        var usuarioId = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        SesionActual.Iniciar(usuarioId, "cajero", "Cajero Prueba", Roles.Admin,
            Permisos.Todos, DateTime.UtcNow, 1);
        SesionActual.EstablecerModo(ModoApp.Pos500);

        _productoId = await _productos.CrearAsync(new ProductoDatos(
            "7501", "Agua 1L", 50m, Cantidad: 100, "Botella", FechaCaducidad: null));
    }

    public async Task DisposeAsync()
    {
        SesionActual.Cerrar();
        await BorrarBaseAsync();
    }

    private static async Task BorrarBaseAsync()
    {
        await using var conexion = new MySqlConnection(CadenaServidor);
        await conexion.OpenAsync();
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"DROP DATABASE IF EXISTS {Bd};";
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<int> ContarAuditoriaAsync(string accion)
    {
        await using var conexion = new MySqlConnection(Cadena);
        await conexion.OpenAsync();
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM auditoria WHERE accion = @accion AND entidad = 'pos_factura';";
        cmd.Parameters.AddWithValue("@accion", accion);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private async Task<int> StockAsync()
    {
        await using var conexion = new MySqlConnection(Cadena);
        await conexion.OpenAsync();
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = "SELECT cantidad FROM pos_producto WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", _productoId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    [Fact]
    public async Task UnaVenta_DescuentaStock_YDejaSuLineaEnElHistorial()
    {
        var resultado = await _ventas.RegistrarVentaAsync(new VentaSolicitud(
            [new VentaLinea(_productoId, "Agua 1L", Cantidad: 3, PrecioUnitario: 50m)],
            ClienteId: null, MetodoPagoFactura.Efectivo, EfectivoRecibido: 200m));

        // Numeración y totales (ITBIS 18% sobre subtotal)
        resultado.NumeroFactura.Should().Be("F-0001");
        resultado.Totales.Subtotal.Should().Be(150m);
        resultado.Totales.Itbis.Should().Be(27m);
        resultado.Totales.Total.Should().Be(177m);
        resultado.Cambio.Should().Be(23m);

        (await StockAsync()).Should().Be(97, "se vendieron 3 de 100");

        // El historial es el mismo del resto de la suite
        (await ContarAuditoriaAsync("crear")).Should().Be(1);
    }

    [Fact]
    public async Task Anular_DevuelveElStock_YQuedaComoAccionAnularEnElHistorial()
    {
        var venta = await _ventas.RegistrarVentaAsync(new VentaSolicitud(
            [new VentaLinea(_productoId, "Agua 1L", Cantidad: 10, PrecioUnitario: 50m)],
            ClienteId: null, MetodoPagoFactura.Efectivo, EfectivoRecibido: 600m));

        (await StockAsync()).Should().Be(90);

        await _facturas.AnularAsync(venta.FacturaId, "Cliente devolvió la mercancía");

        (await StockAsync()).Should().Be(100, "anular devuelve el stock");
        (await ContarAuditoriaAsync("anular")).Should().Be(1);
    }

    /// <summary>
    /// Sin stock la venta tiene que fallar ENTERA: ni factura, ni descuento, ni
    /// línea de auditoría. Si la auditoría se escribiera después del commit,
    /// este test seguiría pasando pero el historial mentiría en el caso
    /// contrario (venta guardada sin registrar).
    /// </summary>
    [Fact]
    public async Task SinStockSuficiente_NoQuedaNada_NiFacturaNiHistorial()
    {
        var vender = () => _ventas.RegistrarVentaAsync(new VentaSolicitud(
            [new VentaLinea(_productoId, "Agua 1L", Cantidad: 500, PrecioUnitario: 50m)],
            ClienteId: null, MetodoPagoFactura.Efectivo, EfectivoRecibido: 30_000m));

        await vender.Should().ThrowAsync<InvalidOperationException>();

        (await StockAsync()).Should().Be(100, "no se tocó el inventario");
        (await ContarAuditoriaAsync("crear")).Should().Be(0, "no hay venta que auditar");

        await using var conexion = new MySqlConnection(Cadena);
        await conexion.OpenAsync();
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM pos_factura;";
        Convert.ToInt32(await cmd.ExecuteScalarAsync()).Should().Be(0);
    }
}
