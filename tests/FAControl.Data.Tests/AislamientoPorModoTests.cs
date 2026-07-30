using FluentAssertions;
using MySqlConnector;
using FAControl.Common;
using FAControl.Data;
using FAControl.Models;
using FAControl.Services;

namespace FAControl.Data.Tests;

/// <summary>
/// La regla que el cliente repitió el 29/07/2026: "todas las db de cada modo
/// deben ser independientes; lo único que podrán compartir son los usuarios,
/// los roles (por respectivos modos) y los permisos otorgados".
///
/// No son bases de datos separadas —es una sola— pero el AISLAMIENTO tiene que
/// comportarse igual: un cliente cargado en PrestControl no puede aparecer
/// nunca operando en DealControl, ni al revés. Esto se prueba contra MySQL real
/// porque el aislamiento vive en los WHERE de los repositorios: un test de
/// mentira no probaría nada.
///
/// Requiere MySQL local (root/root). Recrea su propia base en cada corrida.
/// </summary>
[Collection(ColeccionSesionData.Nombre)]   // SesionActual es global
public class AislamientoPorModoTests : IAsyncLifetime
{
    private const string CadenaServidor = "Server=localhost;Port=3306;Uid=root;Pwd=root;";
    private const string Bd = "facontrol_aislamiento_test";
    private const string CadenaTest = CadenaServidor + $"Database={Bd};";

    private ConexionFactory _factory = null!;
    private ClienteService _clientes = null!;
    private UsuarioRepository _usuarios = null!;
    private long _usuarioId;

    public async Task InitializeAsync()
    {
        await CrearBaseDeDatosDePruebaAsync();

        _factory = new ConexionFactory(CadenaTest);
        _usuarios = new UsuarioRepository(_factory);
        var auditoria = new AuditoriaService(new AuditoriaRepository(_factory),
            new SesionRepository(_factory), _usuarios);
        _clientes = new ClienteService(new ClienteRepository(_factory), auditoria);

        await using var conexion = await _factory.AbrirAsync();
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = """
            INSERT INTO usuario (username, password_hash, nombre)
            VALUES ('aislamiento', 'hash-de-prueba', 'Usuario Test');
            SELECT LAST_INSERT_ID();
            """;
        _usuarioId = Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    public async Task DisposeAsync()
    {
        SesionActual.Cerrar();
        await using var conexion = new MySqlConnection(CadenaServidor);
        await conexion.OpenAsync();
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"DROP DATABASE IF EXISTS {Bd};";
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task CrearBaseDeDatosDePruebaAsync()
    {
        await using var conexion = new MySqlConnection(CadenaServidor);
        await conexion.OpenAsync();
        await using (var drop = conexion.CreateCommand())
        {
            drop.CommandText = $"DROP DATABASE IF EXISTS {Bd};";
            await drop.ExecuteNonQueryAsync();
        }
        await using (var crear = conexion.CreateCommand())
        {
            crear.CommandText =
                $"CREATE DATABASE {Bd} CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;";
            await crear.ExecuteNonQueryAsync();
        }
        await conexion.ChangeDatabaseAsync(Bd);

        foreach (var bloque in VerificadorBaseDatos.ObtenerBloquesEjecutables())
        {
            await using var cmd = conexion.CreateCommand();
            cmd.CommandText = bloque;
            cmd.CommandTimeout = 120;
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private void EntrarA(ModoApp modo)
    {
        SesionActual.Iniciar(_usuarioId, "aislamiento", "Usuario Test", Roles.Admin,
            Permisos.Todos, DateTime.UtcNow, 1);
        SesionActual.EstablecerModo(modo);
    }

    private static ClienteDatos Datos(string cedula, string nombre) =>
        new(cedula, nombre, "Prueba", "809-000-0000", "Constanza", null, null);

    /// <summary>Lo central: los clientes de una estancia no se ven desde la otra.</summary>
    [Fact]
    public async Task Los_clientes_de_cada_estancia_no_se_mezclan()
    {
        EntrarA(ModoApp.PrestControl);
        await _clientes.CrearAsync(Datos("001-0000001-1", "Prestatario"));

        EntrarA(ModoApp.DealerControl);
        await _clientes.CrearAsync(Datos("002-0000002-2", "Comprador"));

        EntrarA(ModoApp.PrestControl);
        var enPrest = await _clientes.ObtenerActivosAsync();
        enPrest.Should().ContainSingle();
        enPrest[0].Nombre.Should().Be("Prestatario");

        EntrarA(ModoApp.DealerControl);
        var enDeal = await _clientes.ObtenerActivosAsync();
        enDeal.Should().ContainSingle();
        enDeal[0].Nombre.Should().Be("Comprador");
    }

    /// <summary>
    /// La misma persona puede ser cliente de préstamos Y comprar un vehículo:
    /// son dos fichas, una por estancia, y la cédula no choca entre modos.
    /// </summary>
    [Fact]
    public async Task La_misma_cedula_puede_existir_en_las_dos_estancias()
    {
        const string cedula = "402-1111111-1";

        EntrarA(ModoApp.PrestControl);
        await _clientes.CrearAsync(Datos(cedula, "Juan"));

        EntrarA(ModoApp.DealerControl);
        var crearEnDealer = () => _clientes.CrearAsync(Datos(cedula, "Juan"));

        await crearEnDealer.Should().NotThrowAsync();
    }

    /// <summary>…pero DENTRO de una estancia la cédula sigue siendo única.</summary>
    [Fact]
    public async Task La_cedula_repetida_en_la_misma_estancia_se_rechaza()
    {
        const string cedula = "402-2222222-2";

        EntrarA(ModoApp.PrestControl);
        await _clientes.CrearAsync(Datos(cedula, "Ana"));

        var repetir = () => _clientes.CrearAsync(Datos(cedula, "Ana"));

        (await repetir.Should().ThrowAsync<ArgumentException>())
            .WithMessage("*Ya existe un cliente*");
    }

    /// <summary>
    /// Lo que SÍ se comparte: el usuario es uno solo para toda la suite, y sus
    /// roles y permisos se guardan POR estancia.
    /// </summary>
    [Fact]
    public async Task El_usuario_es_uno_solo_y_sus_roles_van_por_estancia()
    {
        var roles = await _usuarios.ObtenerRolesAsync();
        var rolPrest = roles.First(r => r.Modo == ModoApp.PrestControl.ClaveDb());
        var rolDealer = roles.First(r => r.Modo == ModoApp.DealerControl.ClaveDb());

        await _usuarios.GuardarRolesPorModoAsync(_usuarioId,
            new RolesUsuario(EsAdmin: false, RolPrestId: rolPrest.Id,
                RolDealerId: rolDealer.Id, RolAutoId: null),
            rolAdminId: null);

        var guardados = await _usuarios.ObtenerRolesDeUsuarioAsync(_usuarioId);

        guardados.RolPrestId.Should().Be(rolPrest.Id);
        guardados.RolDealerId.Should().Be(rolDealer.Id);
        guardados.EsAdmin.Should().BeFalse();

        // Un solo usuario en la tabla: no se duplica por estancia
        var todos = await _usuarios.ObtenerTodosAsync(incluirProgramadores: false);
        todos.Should().ContainSingle(u => u.Id == _usuarioId);
    }

    /// <summary>
    /// El catálogo de permisos incluye el acceso por estancia: sin él, un
    /// empleado no entra a ese modo aunque su contraseña sea correcta.
    /// </summary>
    [Fact]
    public async Task Existe_un_permiso_de_acceso_por_cada_estancia()
    {
        var permisos = (await _usuarios.ObtenerCatalogoPermisosAsync())
            .Select(p => p.Codigo).ToList();

        permisos.Should().Contain(Permisos.AccesoDe(ModoApp.PrestControl));
        permisos.Should().Contain(Permisos.AccesoDe(ModoApp.DealerControl));
    }
}
