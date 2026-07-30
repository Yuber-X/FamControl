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
/// El punto de venta trabajando con las DOS bases a la vez, que es lo nuevo y
/// lo que puede romperse: los datos del POS van a `pos500_db` y la auditoría a
/// `facontrol_db`, dentro de la MISMA transacción.
///
/// Lo que se verifica:
///  * una venta descuenta stock, numera la factura y deja su línea en el
///    historial compartido;
///  * anular devuelve el stock y queda como acción 'anular' en ese historial;
///  * si la venta falla, no queda ni la factura ni la línea de auditoría —
///    que es justamente lo que se ganó calificando el esquema en vez de
///    auditar después del commit.
///
/// Requiere MySQL local (root/root). Recrea sus dos bases en cada corrida.
/// </summary>
[Collection(ColeccionSesionData.Nombre)]   // SesionActual es global
public class VentaPosTests : IAsyncLifetime
{
    private const string CadenaServidor = "Server=localhost;Port=3306;Uid=root;Pwd=root;";
    private const string BdSuite = "facontrol_pos_suite_test";
    private const string BdPos = "pos500_venta_test";
    private const string CadenaSuite = CadenaServidor + $"Database={BdSuite};";
    private const string CadenaPos = CadenaServidor + $"Database={BdPos};";

    private ConexionPos500 _conexionPos = null!;
    private VentaService _ventas = null!;
    private FacturaService _facturas = null!;
    private ProductoService _productos = null!;
    private AuditoriaService _auditoria = null!;
    private long _productoId;

    public async Task InitializeAsync()
    {
        // Base de la suite: usuarios, roles, permisos y auditoría
        await new VerificadorBaseDatos(CadenaSuite).CrearEsquemaAsync();
        // Base del punto de venta: productos, facturas, caja
        _conexionPos = new ConexionPos500(CadenaPos, BdSuite);
        await new VerificadorPos500(CadenaPos).PrepararAsync();

        var suite = new ConexionFactory(CadenaSuite);
        _auditoria = new AuditoriaService(new AuditoriaRepository(suite),
            new SesionRepository(suite), new UsuarioRepository(suite));

        var facturaRepo = new FacturaRepository(_conexionPos);
        var productoRepo = new ProductoRepository(_conexionPos);
        var config = new ConfiguracionNegocioService(
            new ConfiguracionNegocioRepository(_conexionPos), _auditoria);
        await config.CargarAsync();

        // Cliente del MOSTRADOR: el del POS, no el de préstamos (mismo nombre de
        // clase en distinto namespace, por eso el calificado)
        _ventas = new VentaService(facturaRepo,
            new FAControl.Data.Pos.ClienteRepository(_conexionPos), config, _auditoria);
        _facturas = new FacturaService(facturaRepo, _auditoria);
        _productos = new ProductoService(productoRepo, _auditoria);

        // Usuario de la SUITE: el cajero es un usuario de FAControl, no del POS
        await using var conexion = new MySqlConnection(CadenaSuite);
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
        await using var conexion = new MySqlConnection(CadenaServidor);
        await conexion.OpenAsync();
        foreach (var bd in new[] { BdSuite, BdPos })
        {
            await using var cmd = conexion.CreateCommand();
            cmd.CommandText = $"DROP DATABASE IF EXISTS {bd};";
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private async Task<int> ContarAuditoriaAsync(string accion)
    {
        await using var conexion = new MySqlConnection(CadenaSuite);
        await conexion.OpenAsync();
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM auditoria WHERE accion = @accion AND entidad = 'factura';";
        cmd.Parameters.AddWithValue("@accion", accion);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private async Task<int> StockAsync()
    {
        await using var conexion = new MySqlConnection(CadenaPos);
        await conexion.OpenAsync();
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = "SELECT cantidad FROM producto WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", _productoId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    [Fact]
    public async Task UnaVenta_DescuentaStock_YSuHistorialQuedaEnLaBaseDeLaSuite()
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

        // La línea del historial quedó en la base de la SUITE, no en la del POS
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

        await using var conexion = new MySqlConnection(CadenaPos);
        await conexion.OpenAsync();
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM factura;";
        Convert.ToInt32(await cmd.ExecuteScalarAsync()).Should().Be(0);
    }
}
