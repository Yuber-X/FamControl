using FluentAssertions;
using MySqlConnector;
using FAControl.Common;
using FAControl.Data;
using FAControl.Models;
using FAControl.Services;

namespace FAControl.Data.Tests;

/// <summary>
/// Filtros del reporte del dealer (pedido 2026-07-30: "los filtros por usuario
/// (lo que vendio y etc.), tambien otro para los clientes").
///
/// LO QUE SE VERIFICA:
///  * Filtrar por usuario deja solo lo que ESE usuario registro.
///  * Filtrar por cliente deja solo lo de ESE cliente.
///  * Los dos juntos se cruzan (AND), no se suman.
///  * El INVENTARIO no se filtra: es del negocio, no de una persona. Es el
///    error facil de cometer aca, y el que haria que el dueño lea el capital
///    de todo el negocio como si fuera de un cliente.
///
/// Requiere MySQL local (root/root). Recrea su base en cada corrida.
/// </summary>
[Collection(ColeccionSesionData.Nombre)]   // SesionActual es global
public class ReporteDealFiltrosTests : IAsyncLifetime
{
    private const string CadenaServidor = "Server=localhost;Port=3306;Uid=root;Pwd=root;";
    private const string Bd = "facontrol_reporte_filtros_test";
    private const string Cadena = CadenaServidor + $"Database={Bd};";

    private ReporteDealService _reportes = null!;
    private VentaVehiculoService _ventas = null!;
    private long _ana, _beto;          // usuarios (vendedores)
    private long _cliente1, _cliente2;
    private long _v1, _v2, _v3;        // vehiculos

    public async Task InitializeAsync()
    {
        await BorrarBaseAsync();
        await new VerificadorBaseDatos(Cadena).CrearEsquemaAsync();

        var fabrica = new ConexionFactory(Cadena);
        var auditoria = new AuditoriaService(new AuditoriaRepository(fabrica),
            new SesionRepository(fabrica), new UsuarioRepository(fabrica));
        var contador = new ContadorRepository();

        _reportes = new ReporteDealService(new ReporteDealRepository(fabrica), new AjustesLocales());
        _ventas = new VentaVehiculoService(new VentaVehiculoRepository(fabrica),
            new VehiculoRepository(fabrica), new ClienteRepository(fabrica), contador, fabrica,
            auditoria, new VentaPlazoRepository(fabrica));

        await using var conexion = new MySqlConnection(Cadena);
        await conexion.OpenAsync();

        _ana = await InsertarUsuarioAsync(conexion, "ana", "Ana");
        _beto = await InsertarUsuarioAsync(conexion, "beto", "Beto");
        _cliente1 = await InsertarClienteAsync(conexion, "001-0000011-1", "Carlos");
        _cliente2 = await InsertarClienteAsync(conexion, "001-0000012-2", "Diana");
        _v1 = await InsertarVehiculoAsync(conexion, "V-8001");
        _v2 = await InsertarVehiculoAsync(conexion, "V-8002");
        _v3 = await InsertarVehiculoAsync(conexion, "V-8003");
    }

    private static async Task<long> InsertarUsuarioAsync(MySqlConnection c, string user, string nombre)
    {
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO usuario (username, password_hash, nombre)
            VALUES (@u, 'hash-de-prueba', @n);
            SELECT LAST_INSERT_ID();
            """;
        cmd.Parameters.AddWithValue("@u", user);
        cmd.Parameters.AddWithValue("@n", nombre);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private static async Task<long> InsertarClienteAsync(MySqlConnection c, string cedula, string nombre)
    {
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO cliente (cedula, nombre, apellido, ambito)
            VALUES (@c, @n, 'Prueba', 'dealercontrol');
            SELECT LAST_INSERT_ID();
            """;
        cmd.Parameters.AddWithValue("@c", cedula);
        cmd.Parameters.AddWithValue("@n", nombre);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private static async Task<long> InsertarVehiculoAsync(MySqlConnection c, string codigo)
    {
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO vehiculo (codigo, marca, modelo, anio, precio_venta,
                                  costo_adquisicion, gastos_importacion, estado)
            VALUES (@c, 'Honda', 'Civic', 2021, 700000, 500000, 50000, 'disponible');
            SELECT LAST_INSERT_ID();
            """;
        cmd.Parameters.AddWithValue("@c", codigo);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
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

    /// <summary>Vende con la sesion de ese usuario: created_by sale de SesionActual.</summary>
    private async Task VenderAsync(long usuarioId, string username, long clienteId,
        long vehiculoId, decimal precio)
    {
        SesionActual.Iniciar(usuarioId, username, username, Roles.Admin,
            Permisos.Todos, DateTime.UtcNow, 1);
        SesionActual.EstablecerModo(ModoApp.DealerControl);
        await _ventas.RegistrarAsync(new VentaVehiculoDatos(
            vehiculoId, clienteId, precio, MetodoPago.Efectivo, Notas: null));
    }

    /// <summary>
    /// Escenario: Ana vende 2 (a Carlos y a Diana), Beto vende 1 (a Carlos).
    /// </summary>
    private async Task ArmarEscenarioAsync()
    {
        await VenderAsync(_ana, "ana", _cliente1, _v1, 700_000m);
        await VenderAsync(_ana, "ana", _cliente2, _v2, 800_000m);
        await VenderAsync(_beto, "beto", _cliente1, _v3, 900_000m);
    }

    private static (DateOnly Desde, DateOnly Hasta) RangoAmplio() =>
        (FechaNegocio.Hoy.AddDays(-30), FechaNegocio.Hoy.AddDays(1));

    [Fact]
    public async Task SinFiltro_TraeTodo()
    {
        await ArmarEscenarioAsync();
        var (desde, hasta) = RangoAmplio();

        var r = await _reportes.ObtenerReporteAsync(desde, hasta);

        r.CantidadVentas.Should().Be(3);
        r.MontoVendido.Should().Be(2_400_000m);
        r.HayFiltro.Should().BeFalse();
    }

    [Fact]
    public async Task FiltrarPorUsuario_DejaSoloLoQueEseUsuarioRegistro()
    {
        await ArmarEscenarioAsync();
        var (desde, hasta) = RangoAmplio();

        var r = await _reportes.ObtenerReporteAsync(desde, hasta, usuarioId: _ana);

        r.CantidadVentas.Should().Be(2);
        r.MontoVendido.Should().Be(1_500_000m, "700,000 + 800,000");
        r.HayFiltro.Should().BeTrue();
        r.PorVendedor.Should().ContainSingle().Which.VendedorNombre.Should().Be("Ana");
    }

    [Fact]
    public async Task FiltrarPorCliente_DejaSoloLoDeEseCliente()
    {
        await ArmarEscenarioAsync();
        var (desde, hasta) = RangoAmplio();

        var r = await _reportes.ObtenerReporteAsync(desde, hasta, clienteId: _cliente1);

        r.CantidadVentas.Should().Be(2, "Carlos compro a Ana y a Beto");
        r.MontoVendido.Should().Be(1_600_000m, "700,000 + 900,000");
        r.PorVendedor.Should().HaveCount(2);
    }

    /// <summary>Los dos filtros se cruzan, no se suman.</summary>
    [Fact]
    public async Task LosDosFiltrosSeCruzan()
    {
        await ArmarEscenarioAsync();
        var (desde, hasta) = RangoAmplio();

        var r = await _reportes.ObtenerReporteAsync(desde, hasta,
            usuarioId: _ana, clienteId: _cliente1);

        r.CantidadVentas.Should().Be(1, "solo la venta de Ana a Carlos");
        r.MontoVendido.Should().Be(700_000m);
    }

    /// <summary>
    /// El error facil: filtrar el inventario. Los vehiculos disponibles y el
    /// capital invertido son del NEGOCIO — no cambian con el filtro, y la
    /// pantalla lo aclara.
    /// </summary>
    [Fact]
    public async Task ElInventarioNoSeFiltra_PorqueEsDelNegocio()
    {
        await ArmarEscenarioAsync();
        var (desde, hasta) = RangoAmplio();

        var todo = await _reportes.ObtenerReporteAsync(desde, hasta);
        var filtrado = await _reportes.ObtenerReporteAsync(desde, hasta, usuarioId: _ana);

        filtrado.VehiculosDisponibles.Should().Be(todo.VehiculosDisponibles);
        filtrado.CapitalInvertido.Should().Be(todo.CapitalInvertido);
        filtrado.HayFiltro.Should().BeTrue("la pantalla necesita saberlo para aclararlo");
    }

    /// <summary>Los combos salen de las OPERACIONES, no de todas las tablas.</summary>
    [Fact]
    public async Task LosCombosTraenSoloAQuienesOperaronEnElDealer()
    {
        await ArmarEscenarioAsync();

        var usuarios = await _reportes.ObtenerUsuariosDelDealerAsync();
        var clientes = await _reportes.ObtenerClientesDelDealerAsync();

        usuarios.Select(u => u.Nombre).Should().BeEquivalentTo(["Ana", "Beto"]);
        clientes.Should().HaveCount(2);
        clientes.Select(c => c.Nombre).Should().Contain(n => n.StartsWith("Carlos"));
    }
}
