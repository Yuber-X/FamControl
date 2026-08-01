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
        //  recibo_venta desde el financiamiento del dealer, 016 → 6;
        //  recibo_alquiler desde los cobros de alquiler, 034 → 7).
        // Cada talonario va por su lado: son numeraciones distintas y cada una
        // tiene que poder rendirse por separado.
        await using var conexion = new MySqlConnection(CadenaProvision);
        await conexion.OpenAsync();
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM contador;";
        Convert.ToInt32(await cmd.ExecuteScalarAsync()).Should().Be(7);
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

    /// <summary>
    /// El usuario con rol GLOBAL que se siembra (el Programador) tiene TODOS los
    /// permisos del catalogo.
    ///
    /// POR QUE EXISTE: el 01/08/2026 se agrego el permiso 'contratos' al rol
    /// Admin, pero el menu no lee rol_permiso sino usuario_permiso —la union
    /// efectiva por usuario, que los triggers siembran al crear el usuario—.
    /// Nadie lo tenia y la pantalla de Contratos desaparecio para todos,
    /// incluido el dueño. No fallo ninguna prueba: no habia ninguna que mirara
    /// esto.
    ///
    /// Este test cubre la instalacion NUEVA. Para las bases que ya existen la
    /// regla es de disciplina y esta escrita en 036: toda migracion que agregue
    /// un permiso tiene que sembrarlo tambien en usuario_permiso.
    /// </summary>
    [Fact]
    public async Task CrearEsquema_DejaAlUsuarioGlobalConTodosLosPermisos()
    {
        await new VerificadorBaseDatos(CadenaProvision).CrearEsquemaAsync();

        await using var conexion = new MySqlConnection(CadenaProvision);
        await conexion.OpenAsync();
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = """
            SELECT
              (SELECT COUNT(*) FROM permiso) AS catalogo,
              (SELECT COUNT(*) FROM usuario_permiso up
               JOIN usuario u ON u.id = up.usuario_id
               JOIN rol r     ON r.id = u.rol_id AND r.modo IS NULL) AS del_admin;
            """;
        await using var reader = await cmd.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();

        var catalogo = Convert.ToInt32(reader["catalogo"]);
        var delAdmin = Convert.ToInt32(reader["del_admin"]);

        catalogo.Should().BeGreaterThan(0, "el catalogo de permisos no puede estar vacio");
        delAdmin.Should().Be(catalogo,
            "a un rol global le tienen que llegar TODOS los permisos: si falta uno, la " +
            "pantalla que lo usa desaparece del menu sin aviso");
    }
}
