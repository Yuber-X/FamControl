using FluentAssertions;
using MySqlConnector;
using FAControl.Common;
using FAControl.Data;
using FAControl.Models;
using FAControl.Services;

namespace FAControl.Data.Tests;

/// <summary>
/// Dos correcciones de la prueba del 31/07, contra MySQL real:
///
///  1. La MATRICULA se perdia al CREAR un vehiculo (al editar si se guardaba).
///     El formulario la pedia, el repositorio la guardaba y la consulta la
///     traia; el unico punto donde se caia era al armar la entidad del alta.
///     Este test recorre el camino completo, que es donde estaba el agujero.
///
///  2. El grid de CLIENTES del dealer mostraba "prestamos activos" y "saldo
///     pendiente", que son de PrestControl y daban siempre 0. Ahora cada
///     estancia cuenta lo suyo.
///
/// Requiere MySQL local (root/root). Recrea su base en cada corrida.
/// </summary>
[Collection(ColeccionSesionData.Nombre)]   // SesionActual es global
public class GridDealerTests : IAsyncLifetime
{
    private const string CadenaServidor = "Server=localhost;Port=3306;Uid=root;Pwd=root;";
    private const string Bd = "facontrol_grid_dealer_test";
    private const string Cadena = CadenaServidor + $"Database={Bd};";

    private ConexionFactory _fabrica = null!;
    private VehiculoService _vehiculos = null!;
    private ClienteRepository _clientes = null!;
    private VentaVehiculoService _ventas = null!;
    private AlquilerService _alquileres = null!;
    private long _clienteId;

    public async Task InitializeAsync()
    {
        await BorrarBaseAsync();
        await new VerificadorBaseDatos(Cadena).CrearEsquemaAsync();

        _fabrica = new ConexionFactory(Cadena);
        var auditoria = new AuditoriaService(new AuditoriaRepository(_fabrica),
            new SesionRepository(_fabrica), new UsuarioRepository(_fabrica));
        var contador = new ContadorRepository();
        var vehiculoRepo = new VehiculoRepository(_fabrica);
        _clientes = new ClienteRepository(_fabrica);

        _vehiculos = new VehiculoService(vehiculoRepo, contador, _fabrica, auditoria,
            new VehiculoReparacionRepository(_fabrica), new VentaVehiculoRepository(_fabrica),
            new PrestamoRepository(_fabrica), new VehiculoGastoRepository(_fabrica));
        _ventas = new VentaVehiculoService(new VentaVehiculoRepository(_fabrica), vehiculoRepo,
            _clientes, contador, _fabrica, auditoria, new VentaPlazoRepository(_fabrica));
        _alquileres = new AlquilerService(new AlquilerRepository(_fabrica), vehiculoRepo,
            _clientes, contador, _fabrica, auditoria);

        await using var conexion = new MySqlConnection(Cadena);
        await conexion.OpenAsync();
        await using (var cmd = conexion.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO usuario (username, password_hash, nombre)
                VALUES ('deal', 'hash-de-prueba', 'Encargado');
                SELECT LAST_INSERT_ID();
                """;
            var usuarioId = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            SesionActual.Iniciar(usuarioId, "deal", "Encargado", Roles.Admin,
                Permisos.Todos, DateTime.UtcNow, 1);
            SesionActual.EstablecerModo(ModoApp.DealerControl);
        }
        await using (var cmd = conexion.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO cliente (cedula, nombre, apellido, ambito)
                VALUES ('001-0000021-1', 'Rafael', 'Comprador', 'dealercontrol');
                SELECT LAST_INSERT_ID();
                """;
            _clienteId = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        }
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

    private static VehiculoDatos DatosDe(string marca, string? matricula) =>
        new(Vin: null, Marca: marca, Modelo: "Modelo", Anio: 2022, Color: "Blanco",
            Placa: "A123456", Matricula: matricula, Tipo: TipoVehiculo.Sedan,
            Kilometraje: 0, CostoAdquisicion: 400_000m, GastosImportacion: 0m,
            PrecioVenta: 600_000m, Notas: null);

    /// <summary>El caso reportado: crear un vehiculo con matricula y verla en el grid.</summary>
    [Fact]
    public async Task CrearVehiculo_GuardaLaMatricula()
    {
        var (id, _) = await _vehiculos.CrearAsync(DatosDe("Toyota", "MAT-000123"));

        // Por la ficha…
        var vehiculo = await _vehiculos.ObtenerPorIdAsync(id);
        vehiculo!.Matricula.Should().Be("MAT-000123");

        // …y por la consulta que alimenta el GRID, que es donde se veia en blanco
        var enGrid = (await _vehiculos.ObtenerResumenesAsync())
            .Single(v => v.Id == id);
        enGrid.Matricula.Should().Be("MAT-000123");
    }

    /// <summary>Se limpia igual que la placa: un espacio al final la haria distinta de si misma.</summary>
    [Fact]
    public async Task LaMatriculaSeLimpiaDeEspacios()
    {
        var (id, _) = await _vehiculos.CrearAsync(DatosDe("Kia", "  MAT-999  "));

        (await _vehiculos.ObtenerPorIdAsync(id))!.Matricula.Should().Be("MAT-999");
    }

    /// <summary>Sin matricula sigue quedando NULL, no cadena vacia.</summary>
    [Fact]
    public async Task SinMatricula_QuedaNula()
    {
        var (id, _) = await _vehiculos.CrearAsync(DatosDe("Honda", null));

        (await _vehiculos.ObtenerPorIdAsync(id))!.Matricula.Should().BeNull();
    }

    /// <summary>
    /// El grid de clientes del dealer cuenta VEHICULOS y ALQUILERES, no
    /// prestamos, y el saldo sale de los plazos de sus ventas financiadas.
    /// </summary>
    [Fact]
    public async Task GridDeClientes_DelDealer_CuentaLoSuyo()
    {
        var (v1, _) = await _vehiculos.CrearAsync(DatosDe("Toyota", "M-1"));
        var (v2, _) = await _vehiculos.CrearAsync(DatosDe("Kia", "M-2"));
        var (v3, _) = await _vehiculos.CrearAsync(DatosDe("Honda", "M-3"));

        // Una venta al contado y una financiada: 2 vehiculos comprados
        await _ventas.RegistrarAsync(new VentaVehiculoDatos(
            v1, _clienteId, 600_000m, MetodoPago.Efectivo, null));
        await _ventas.RegistrarAsync(new VentaVehiculoDatos(
            v2, _clienteId, 900_000m, MetodoPago.Efectivo, null,
            TipoVenta: TipoVenta.Plazos,
            Plan: new PlanPlazos(100_000m, 4, FechaNegocio.Hoy.AddDays(30))));

        // Y un alquiler
        await _alquileres.RegistrarAsync(new AlquilerDatos(
            v3, _clienteId, FechaNegocio.Hoy, FechaNegocio.Hoy.AddDays(3), 2_000m, null));

        var fila = (await _clientes.ObtenerResumenesAsync(ModoApp.DealerControl))
            .Single(c => c.Id == _clienteId);

        fila.ContratosAbiertos.Should().Be(2, "compro dos vehiculos");
        fila.Alquileres.Should().Be(1);
        fila.SaldoPendiente.Should().Be(800_000m, "900,000 menos 100,000 de inicial");
    }

    /// <summary>
    /// El cliente del dealer NO aparece en PrestControl: los ambitos no se
    /// mezclan. Es la regla que el cliente repitio en cada ronda.
    /// </summary>
    [Fact]
    public async Task LosClientesNoSeMezclanEntreEstancias()
    {
        var dePrestControl = await _clientes.ObtenerResumenesAsync(ModoApp.PrestControl);

        dePrestControl.Should().NotContain(c => c.Id == _clienteId);
    }
}
