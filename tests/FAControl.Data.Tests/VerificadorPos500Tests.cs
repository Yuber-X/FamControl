using FluentAssertions;
using MySqlConnector;

namespace FAControl.Data.Tests;

/// <summary>
/// La base del punto de venta (`pos500_db`) se crea sola la primera vez que se
/// entra al modo POS-500, igual que la principal. Acá se verifica contra MySQL
/// real que el esquema embebido corre completo y que volver a correrlo no
/// rompe nada (el usuario puede entrar y salir del modo cien veces).
/// </summary>
public class VerificadorPos500Tests : IAsyncLifetime
{
    private const string CadenaServidor = "Server=localhost;Port=3306;Uid=root;Pwd=root;";
    private const string Bd = "pos500_provision_test";
    private const string CadenaPrueba = CadenaServidor + $"Database={Bd};";

    public async Task InitializeAsync() => await BorrarAsync();

    public async Task DisposeAsync() => await BorrarAsync();

    private static async Task BorrarAsync()
    {
        await using var conexion = new MySqlConnection(CadenaServidor);
        await conexion.OpenAsync();
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"DROP DATABASE IF EXISTS {Bd};";
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Preparar_CreaLaBaseDelPuntoDeVenta_YEsIdempotente()
    {
        var verificador = new VerificadorPos500(CadenaPrueba);

        (await verificador.EstaListaAsync()).Should().BeFalse("todavía no existe");

        await verificador.PrepararAsync();
        (await verificador.EstaListaAsync()).Should().BeTrue();

        // Entrar y salir del modo no puede romper nada
        await verificador.PrepararAsync();
        (await verificador.EstaListaAsync()).Should().BeTrue();

        await using var conexion = new MySqlConnection(CadenaPrueba);
        await conexion.OpenAsync();

        // Las tablas del punto de venta, y SOLO esas
        await using (var cmd = conexion.CreateCommand())
        {
            cmd.CommandText = """
                SELECT TABLE_NAME FROM information_schema.TABLES
                WHERE TABLE_SCHEMA = DATABASE() ORDER BY TABLE_NAME;
                """;
            var tablas = new List<string>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                tablas.Add(reader.GetString(0));

            tablas.Should().BeEquivalentTo(
                "cliente", "configuracion_negocio", "cuadre_caja", "detalle", "factura", "producto");
            // Usuarios, roles y permisos viven en facontrol_db: son compartidos
            tablas.Should().NotContain(["usuario", "rol", "permiso", "sesion", "auditoria"]);
        }

        // La fila única de configuración queda sembrada, y una sola
        await using (var cmd = conexion.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*), MIN(itbis_tasa) FROM configuracion_negocio;";
            await using var reader = await cmd.ExecuteReaderAsync();
            (await reader.ReadAsync()).Should().BeTrue();
            reader.GetInt32(0).Should().Be(1);
            reader.GetDecimal(1).Should().Be(18.00m);
        }
    }

    /// <summary>
    /// Una instalación que se actualiza no tiene la cadena 'POS500Db' en su
    /// App.config: se deriva de la principal cambiándole la base, para no
    /// obligar a editar el archivo a mano en la máquina del cliente.
    /// </summary>
    [Fact]
    public void LaCadenaDelPos_SeDerivaDeLaPrincipalSiNoEstaConfigurada()
    {
        var derivada = ConexionPos500.DerivarDe(
            "Server=192.168.1.50;Port=3306;Database=facontrol_db;Uid=facontrol;Pwd=clave;");

        derivada.Should().Contain("pos500_db");
        derivada.Should().NotContain("facontrol_db");
        // El resto de la cadena se respeta: mismo servidor y mismas credenciales
        derivada.Should().Contain("192.168.1.50");
        derivada.Should().Contain("facontrol");
    }
}
