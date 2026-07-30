using FluentAssertions;
using MySqlConnector;

namespace FAControl.Data.Tests;

/// <summary>
/// Integración contra MySQL real del diagnóstico de arranque y del
/// auto-aprovisionamiento (crear el esquema completo desde el recurso
/// embebido). Requiere el servicio MySQL80 local con credenciales Dev.
/// </summary>
public class VerificadorBaseDatosTests : IAsyncLifetime
{
    private const string CadenaServidor = "Server=localhost;Port=3306;Uid=root;Pwd=root;";
    private const string BdProvision = "facontrol_provision_test";
    private const string CadenaProvision = CadenaServidor + $"Database={BdProvision};";

    public async Task InitializeAsync() => await BorrarBdProvisionAsync();

    public async Task DisposeAsync() => await BorrarBdProvisionAsync();

    private static async Task BorrarBdProvisionAsync()
    {
        await using var conexion = new MySqlConnection(CadenaServidor);
        await conexion.OpenAsync();
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"DROP DATABASE IF EXISTS {BdProvision};";
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Verificar_BaseDatosInexistente_ReportaFaltaBaseDatos()
    {
        var verificador = new VerificadorBaseDatos(CadenaProvision);

        var estado = await verificador.VerificarAsync();

        estado.Should().Be(EstadoBaseDatos.FaltaBaseDatos);
    }

    [Fact]
    public async Task Verificar_PasswordIncorrecta_ReportaCredencialesInvalidas()
    {
        var verificador = new VerificadorBaseDatos(
            "Server=localhost;Port=3306;Uid=root;Pwd=clave-incorrecta;Database=facontrol_db;");

        var estado = await verificador.VerificarAsync();

        estado.Should().Be(EstadoBaseDatos.CredencialesInvalidas);
    }

    [Fact]
    public async Task Verificar_ServidorInalcanzable_ReportaSinServidor()
    {
        // Puerto sin servicio + timeout corto para que el test no espere 15s
        var verificador = new VerificadorBaseDatos(
            "Server=localhost;Port=33999;Uid=root;Pwd=root;Database=facontrol_db;ConnectionTimeout=2;");

        var estado = await verificador.VerificarAsync();

        estado.Should().Be(EstadoBaseDatos.SinServidor);
    }

    [Fact]
    public async Task CrearEsquema_DesdeCero_DejaLaBaseDatosLista()
    {
        var verificador = new VerificadorBaseDatos(CadenaProvision);
        (await verificador.VerificarAsync()).Should().Be(EstadoBaseDatos.FaltaBaseDatos);

        await verificador.CrearEsquemaAsync();

        (await verificador.VerificarAsync()).Should().Be(EstadoBaseDatos.Lista);

        // El esquema quedó operativo: los contadores semilla existen
        // (recibo, prestamo, vehiculo, venta, alquiler — 5 desde Tier 5;
        //  recibo_venta desde el financiamiento del dealer, 016 → 6).
        await using var conexion = new MySqlConnection(CadenaProvision);
        await conexion.OpenAsync();
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM contador;";
        Convert.ToInt32(await cmd.ExecuteScalarAsync()).Should().Be(6);
    }

    /// <summary>
    /// La cuenta de respaldo del desarrollador (020) nace con el esquema, con
    /// todos los permisos… y sin estorbar el wizard: la app tiene que seguir
    /// pidiendo las credenciales del primer Admin, "como si no existiera"
    /// (pedido del cliente 2026-07-29).
    /// </summary>
    [Fact]
    public async Task CrearEsquema_SiembraLaCuentaDelDesarrollador_SinSaltarseElWizard()
    {
        var verificador = new VerificadorBaseDatos(CadenaProvision);
        await verificador.CrearEsquemaAsync();

        await using var conexion = new MySqlConnection(CadenaProvision);
        await conexion.OpenAsync();

        // Existe, es Programador y tiene TODOS los permisos del catálogo
        await using (var cmd = conexion.CreateCommand())
        {
            cmd.CommandText = """
                SELECT r.nombre,
                       (SELECT COUNT(*) FROM usuario_permiso up WHERE up.usuario_id = u.id),
                       (SELECT COUNT(*) FROM permiso)
                FROM usuario u JOIN rol r ON r.id = u.rol_id
                WHERE u.username = 'Yub';
                """;
            await using var reader = await cmd.ExecuteReaderAsync();
            (await reader.ReadAsync()).Should().BeTrue("la cuenta de respaldo se siembra con el esquema");
            reader.GetString(0).Should().Be("Programador");
            reader.GetInt32(1).Should().Be(reader.GetInt32(2));
        }

        // …y el wizard de cuenta inicial igual aparece
        var usuarios = new UsuarioRepository(new ConexionFactory(CadenaProvision));
        (await usuarios.ExisteAlgunUsuarioAsync()).Should().BeFalse(
            "la cuenta del desarrollador no cuenta como usuario del negocio");
    }

    [Fact]
    public void EsquemaEmbebido_NoContieneCreateDatabaseNiUse()
    {
        var sql = VerificadorBaseDatos.LeerEsquemaSinEncabezado();

        sql.Should().NotContainEquivalentOf("CREATE DATABASE");
        sql.Should().NotContainEquivalentOf("USE facontrol_db");
        sql.Should().ContainEquivalentOf("CREATE TABLE usuario");
    }
}
