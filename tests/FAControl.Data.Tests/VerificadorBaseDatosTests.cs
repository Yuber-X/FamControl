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

    [Fact]
    public void EsquemaEmbebido_NoContieneCreateDatabaseNiUse()
    {
        var sql = VerificadorBaseDatos.LeerEsquemaSinEncabezado();

        sql.Should().NotContainEquivalentOf("CREATE DATABASE");
        sql.Should().NotContainEquivalentOf("USE facontrol_db");
        sql.Should().ContainEquivalentOf("CREATE TABLE usuario");
    }
}
