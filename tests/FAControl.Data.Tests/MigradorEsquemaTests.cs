using FluentAssertions;
using MySqlConnector;
using FAControl.Data;

namespace FAControl.Data.Tests;

/// <summary>
/// El migrador que corre en el arranque (pedido del cliente 2026-08-06: poder
/// actualizar FAControl sin reinstalar). Es el código que va a tocar la base
/// REAL del cliente sin nadie mirando, así que se prueba contra MySQL de verdad.
///
/// Requiere el servicio MySQL80 local con credenciales Dev (root/root).
/// </summary>
public class MigradorEsquemaTests : IAsyncLifetime
{
    private const string CadenaServidor = "Server=localhost;Port=3306;Uid=root;Pwd=root;";
    private const string Bd = "facontrol_migrador_test";
    private const string Cadena = CadenaServidor + $"Database={Bd};";

    public async Task InitializeAsync() => await CrearBaseComoLaApp();

    public async Task DisposeAsync()
    {
        await using var conexion = new MySqlConnection(CadenaServidor);
        await conexion.OpenAsync();
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"DROP DATABASE IF EXISTS {Bd};";
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Base recién creada por la app: esquema completo y SIN esquema_migracion.</summary>
    private static async Task CrearBaseComoLaApp()
    {
        await using (var conexion = new MySqlConnection(CadenaServidor))
        {
            await conexion.OpenAsync();
            await using var drop = conexion.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS {Bd};";
            await drop.ExecuteNonQueryAsync();
        }
        await new VerificadorBaseDatos(Cadena).CrearEsquemaAsync();
    }

    private static async Task<T?> EscalarAsync<T>(string sql)
    {
        await using var conexion = new MySqlConnection(Cadena);
        await conexion.OpenAsync();
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = sql;
        var valor = await cmd.ExecuteScalarAsync();
        return valor is null or DBNull ? default : (T)Convert.ChangeType(valor, typeof(T));
    }

    private static async Task EjecutarAsync(string sql)
    {
        await using var conexion = new MySqlConnection(Cadena);
        await conexion.OpenAsync();
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    // ---------- Pruebas ----------

    [Fact]
    public void LasMigracionesViajanDentroDelEjecutable()
    {
        var scripts = MigradorEsquema.ObtenerMigracionesEmbebidas();

        scripts.Should().NotBeEmpty("sin esto la app no puede actualizarse sola");
        scripts.Keys.Should().NotContain("001_create_schema.sql", "el esquema completo va aparte");
        scripts.Keys.Should().NotContain("999_rollback.sql", "ese BORRA la base entera");
        scripts.Keys.Should().Contain(MigradorEsquema.UltimaMigracionHistorica);
    }

    /// <summary>
    /// USE facontrol_db lo entiende mysql.exe, no el protocolo. Y en una
    /// instalación con la base bajo otro nombre saltaría a la equivocada.
    /// </summary>
    [Fact]
    public void LimpiarParaProtocolo_SacaElUseYLosCreateDatabase()
    {
        const string sql = """
            SET NAMES utf8mb4;
            USE facontrol_db;
            CREATE DATABASE IF NOT EXISTS otra;
            ALTER TABLE prestamo ADD COLUMN algo INT NULL;
            """;

        var limpio = MigradorEsquema.LimpiarParaProtocolo(sql);

        limpio.Should().NotContain("USE facontrol_db");
        limpio.Should().NotContain("CREATE DATABASE");
        limpio.Should().Contain("ALTER TABLE prestamo");
        limpio.Should().Contain("SET NAMES utf8mb4");
    }

    /// <summary>
    /// Una base recién creada por la app YA trae todo lo de las migraciones
    /// históricas dentro de 001. Volver a correrlas sería, en el mejor de los
    /// casos, ruido; en el peor (005 no es repetible), romper la base.
    /// </summary>
    [Fact]
    public async Task BaseNueva_MarcaLasHistoricasSinEjecutarlas()
    {
        var ejecutadas = await new MigradorEsquema(Cadena).AplicarPendientesAsync();

        ejecutadas.Where(MigradorEsquema.EsHistorica).Should().BeEmpty(
            "las históricas se anotan, no se corren");
        var anotadas = await EscalarAsync<long>("SELECT COUNT(*) FROM esquema_migracion;");
        anotadas.Should().Be(MigradorEsquema.ObtenerMigracionesEmbebidas().Count,
            "el registro queda completo, corran o no");
    }

    /// <summary>
    /// El circuito completo, de punta a punta: la app arranca contra una base
    /// que NO conoce el método nuevo, aplica sola la migración y la base queda
    /// lista. Es exactamente lo que va a pasar en la PC del cliente al abrir
    /// FAControl después de correr el actualizador.
    /// </summary>
    [Fact]
    public async Task Migracion040_DejaLaBaseListaParaElMetodoDiferido()
    {
        // La base arranca sin el método (como la que tiene el cliente hoy)
        await EjecutarAsync("""
            ALTER TABLE prestamo
              MODIFY COLUMN metodo_amortizacion
                ENUM('frances','cuota_fija','solo_interes') NOT NULL DEFAULT 'cuota_fija';
            """);
        await EjecutarAsync("ALTER TABLE prestamo DROP COLUMN cuota_inicio_capital;");

        var ejecutadas = await new MigradorEsquema(Cadena).AplicarPendientesAsync();

        ejecutadas.Should().Contain("040_capital_diferido.sql");
        (await EscalarAsync<string>($"""
            SELECT COLUMN_TYPE FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = '{Bd}' AND TABLE_NAME = 'prestamo'
              AND COLUMN_NAME = 'metodo_amortizacion';
            """)).Should().Contain("capital_diferido");
        (await EscalarAsync<long>($"""
            SELECT COUNT(*) FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = '{Bd}' AND TABLE_NAME = 'prestamo'
              AND COLUMN_NAME = 'cuota_inicio_capital';
            """)).Should().Be(1);
    }

    /// <summary>
    /// La 040 corre sola al arrancar, así que un arranque interrumpido la deja
    /// a medias y el siguiente la repite. Tiene que aguantarlo.
    /// </summary>
    [Fact]
    public async Task Migracion040_SePuedeCorrerDeNuevoSinRomperNada()
    {
        var sql = MigradorEsquema.ObtenerMigracionesEmbebidas()["040_capital_diferido.sql"];
        var limpio = MigradorEsquema.LimpiarParaProtocolo(sql);

        // Con la misma conexión que usa el migrador: las migraciones usan
        // variables de usuario (@tiene := ...) para poder repetirse.
        await using var conexion = new MySqlConnection(
            new MySqlConnectionStringBuilder(Cadena) { AllowUserVariables = true }.ConnectionString);
        await conexion.OpenAsync();

        // Dos veces seguidas sobre una base que YA la tiene aplicada (viene de 001)
        for (var i = 0; i < 2; i++)
        {
            foreach (var bloque in VerificadorBaseDatos.TrocearParaProtocolo(limpio))
            {
                await using var cmd = conexion.CreateCommand();
                cmd.CommandText = bloque;
                await cmd.ExecuteNonQueryAsync();
            }
        }

        (await EscalarAsync<string>($"""
            SELECT COLUMN_TYPE FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = '{Bd}' AND TABLE_NAME = 'prestamo'
              AND COLUMN_NAME = 'metodo_amortizacion';
            """)).Should().Contain("capital_diferido");
    }

    [Fact]
    public async Task CorrerloDosVeces_NoHaceNadaLaSegunda()
    {
        var migrador = new MigradorEsquema(Cadena);
        await migrador.AplicarPendientesAsync();
        var anotadasPrimera = await EscalarAsync<long>("SELECT COUNT(*) FROM esquema_migracion;");

        var ejecutadas = await migrador.AplicarPendientesAsync();

        ejecutadas.Should().BeEmpty();
        (await EscalarAsync<long>("SELECT COUNT(*) FROM esquema_migracion;"))
            .Should().Be(anotadasPrimera);
    }

    /// <summary>
    /// El camino que de verdad importa: una migración NUEVA se ejecuta contra la
    /// base y queda anotada. Los scripts se inyectan porque las que hoy vienen
    /// embebidas son todas históricas — la primera de verdad la trae la versión
    /// que sigue, y este test ya la está esperando.
    /// </summary>
    [Fact]
    public async Task MigracionNueva_SeEjecutaYSeAnota()
    {
        var scripts = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            // El USE va a propósito: todas las migraciones lo traen, y si no se
            // quitara esto escribiría en facontrol_db —la base REAL de esta PC—
            // en vez de la de prueba.
            ["900_prueba.sql"] = """
                SET NAMES utf8mb4;
                USE facontrol_db;
                ALTER TABLE cliente ADD COLUMN apodo VARCHAR(60) NULL;
                """
        };

        var ejecutadas = await new MigradorEsquema(Cadena, scripts).AplicarPendientesAsync();

        ejecutadas.Should().ContainSingle().Which.Should().Be("900_prueba.sql");
        (await EscalarAsync<long>(
            "SELECT COUNT(*) FROM esquema_migracion WHERE script = '900_prueba.sql';"))
            .Should().Be(1);
        (await EscalarAsync<long>($"""
            SELECT COUNT(*) FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = '{Bd}' AND TABLE_NAME = 'cliente' AND COLUMN_NAME = 'apodo';
            """)).Should().Be(1, "corrió de verdad, y sobre la base de la conexión");
    }

    [Fact]
    public async Task MigracionNueva_NoSeRepiteEnElArranqueSiguiente()
    {
        var scripts = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["900_prueba.sql"] = "ALTER TABLE cliente ADD COLUMN apodo VARCHAR(60) NULL;"
        };
        var migrador = new MigradorEsquema(Cadena, scripts);
        await migrador.AplicarPendientesAsync();

        // Sin ADD COLUMN IF NOT EXISTS: si se repitiera, MySQL tiraría
        // "Duplicate column name" y la app no abriría
        var ejecutadas = await migrador.AplicarPendientesAsync();

        ejecutadas.Should().BeEmpty();
    }

    /// <summary>
    /// El seguro: una migración histórica NUNCA se ejecuta, ni aunque el registro
    /// diga que falta. Varias no son repetibles (005 rechoca los roles contra la
    /// clave única), así que intentarlo rompería la base del cliente en el
    /// arranque en vez de arreglarla.
    /// </summary>
    [Fact]
    public async Task MigracionHistorica_NoSeEjecutaNiAunqueFalteEnElRegistro()
    {
        var migrador = new MigradorEsquema(Cadena);
        await migrador.AplicarPendientesAsync();
        await EjecutarAsync("DELETE FROM esquema_migracion WHERE script = '005_multicuentas.sql';");

        var ejecutadas = await migrador.AplicarPendientesAsync();

        ejecutadas.Should().BeEmpty("es histórica: se vuelve a anotar, no a correr");
        (await EscalarAsync<long>(
            "SELECT COUNT(*) FROM esquema_migracion WHERE script = '005_multicuentas.sql';"))
            .Should().Be(1);
    }

    /// <summary>
    /// LA REGLA QUE NO SE PUEDE ROMPER: actualizar no toca los datos. El cliente
    /// aceptó actualizar justamente porque se le prometió esto.
    /// </summary>
    [Fact]
    public async Task ActualizarNoTocaLosDatosDelCliente()
    {
        await EjecutarAsync("""
            INSERT INTO cliente (ambito, cedula, nombre, apellido, telefono)
            VALUES ('prestcontrol', '053-0038510-0', 'José Ángel', 'Sánchez Peña', '809-494-4144');
            """);
        // LAST_INSERT_ID() es por conexión: leerlo desde otra devuelve 0
        var clienteId = await EscalarAsync<long>(
            "SELECT id FROM cliente WHERE cedula = '053-0038510-0';");
        clienteId.Should().BeGreaterThan(0);

        await new MigradorEsquema(Cadena).AplicarPendientesAsync();
        // Y una corrida que SÍ ejecuta algo, no solo la que anota y se va
        var scripts = MigradorEsquema.ObtenerMigracionesEmbebidas();
        scripts["900_prueba.sql"] = "ALTER TABLE cliente ADD COLUMN apodo VARCHAR(60) NULL;";
        await new MigradorEsquema(Cadena, scripts).AplicarPendientesAsync();

        (await EscalarAsync<long>("SELECT COUNT(*) FROM cliente;")).Should().Be(1);
        (await EscalarAsync<string>("SELECT nombre FROM cliente LIMIT 1;")).Should().Be("José Ángel");
        (await EscalarAsync<string>("SELECT apellido FROM cliente LIMIT 1;")).Should().Be("Sánchez Peña");
        (await EscalarAsync<long>("SELECT id FROM cliente WHERE cedula = '053-0038510-0';"))
            .Should().Be(clienteId, "ni siquiera cambió de id");
    }

    /// <summary>
    /// Una base que se venía migrando a mano con aplicar.ps1 ya tiene el
    /// registro, y puede tenerlo a medias. El migrador no debe romperse: las
    /// históricas que falten se completan anotándolas, y lo nuevo se corre.
    /// Las dos vías escriben en la misma tabla a propósito.
    /// </summary>
    [Fact]
    public async Task BaseQueYaUsabaAplicarPs1_ConvivenSinPisarse()
    {
        await EjecutarAsync("""
            CREATE TABLE IF NOT EXISTS esquema_migracion (
              script      VARCHAR(120) NOT NULL PRIMARY KEY,
              aplicado_at DATETIME     NOT NULL DEFAULT (UTC_TIMESTAMP())
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        await EjecutarAsync("""
            INSERT INTO esquema_migracion (script)
            VALUES ('003_crear_usuario_dedicado.sql'), ('021_prestamo_abierto.sql');
            """);

        var scripts = MigradorEsquema.ObtenerMigracionesEmbebidas();
        scripts["900_prueba.sql"] = "ALTER TABLE cliente ADD COLUMN apodo VARCHAR(60) NULL;";

        var ejecutadas = await new MigradorEsquema(Cadena, scripts).AplicarPendientesAsync();

        ejecutadas.Should().Contain("900_prueba.sql");
        ejecutadas.Where(MigradorEsquema.EsHistorica).Should().BeEmpty(
            "003 y 021 estaban anotadas, y el resto de las históricas no se corren nunca");
        (await EscalarAsync<long>("SELECT COUNT(*) FROM esquema_migracion;"))
            .Should().Be(scripts.Count, "el registro quedó completo");
    }
    // ==================================================================
    // 044 y 045 — el pagare notarial
    // ==================================================================
    // Son las que le van a correr solas al cliente al abrir la version nueva.
    // El riesgo real no es que fallen los tests: es que fallen en su PC, con la
    // base cargada, y la app quede sin poder abrir.

    [Fact]
    public async Task Migracion044_AgregaLasColumnasDelActaYAgrandaLaGarantia()
    {
        // La base arranca como la del cliente hoy: sin columnas del acta y con
        // la garantía en VARCHAR(255).
        await EjecutarAsync("ALTER TABLE prestamo MODIFY COLUMN garantia VARCHAR(255) NULL;");
        foreach (var columna in new[] { "acto_no", "folio_no", "fecha_acto", "municipio_acto",
                                        "deudor_sexo", "deudor_nacionalidad", "deudor_estado_civil",
                                        "deudor_ocupacion", "cuotas_exigibilidad", "dias_gracia",
                                        "mora_porcentaje", "registro_titulos" })
            await EjecutarAsync($"ALTER TABLE prestamo DROP COLUMN {columna};");

        var ejecutadas = await new MigradorEsquema(Cadena).AplicarPendientesAsync();

        ejecutadas.Should().Contain("044_contrato_notarial.sql");
        (await EscalarAsync<long>($"""
            SELECT COUNT(*) FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = '{Bd}' AND TABLE_NAME = 'prestamo'
              AND COLUMN_NAME IN ('acto_no', 'deudor_sexo', 'mora_porcentaje', 'registro_titulos');
            """)).Should().Be(4);

        // Y la garantía tiene que aguantar la descripción del inmueble completa
        (await EscalarAsync<string>($"""
            SELECT DATA_TYPE FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = '{Bd}' AND TABLE_NAME = 'prestamo'
              AND COLUMN_NAME = 'garantia';
            """)).Should().Be("text");
    }

    [Fact]
    public async Task Migracion045_CreaLaTablaDelActaCongelada()
    {
        await EjecutarAsync("DROP TABLE IF EXISTS prestamo_acta;");

        var ejecutadas = await new MigradorEsquema(Cadena).AplicarPendientesAsync();

        ejecutadas.Should().Contain("045_acta_congelada.sql");
        (await EscalarAsync<long>($"""
            SELECT COUNT(*) FROM information_schema.TABLES
            WHERE TABLE_SCHEMA = '{Bd}' AND TABLE_NAME = 'prestamo_acta';
            """)).Should().Be(1);
        // Las 4 partes completas: notario, representante y dos testigos
        (await EscalarAsync<long>($"""
            SELECT COUNT(*) FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = '{Bd}' AND TABLE_NAME = 'prestamo_acta'
              AND COLUMN_NAME IN ('notario_nombre', 'repr_nombre', 't1_nombre', 't2_nombre',
                                  'notario_sexo', 'repr_sexo', 't1_sexo', 't2_sexo');
            """)).Should().Be(8);
    }

    [Fact]
    public async Task LasDosNuevas_SePuedenCorrerDeNuevoSinRomperNada()
    {
        // Un arranque interrumpido deja una migración a medias y el siguiente la
        // repite. Las dos tienen que aguantarlo.
        //
        // Con la MISMA conexión que usa el migrador: las migraciones usan
        // variables de usuario (@existe := ...) para poder repetirse, y sin
        // AllowUserVariables MySqlConnector las rechaza.
        await using var conexion = new MySqlConnection(
            new MySqlConnectionStringBuilder(Cadena) { AllowUserVariables = true }.ConnectionString);
        await conexion.OpenAsync();

        foreach (var nombre in new[] { "044_contrato_notarial.sql", "045_acta_congelada.sql" })
        {
            var limpio = MigradorEsquema.LimpiarParaProtocolo(
                MigradorEsquema.ObtenerMigracionesEmbebidas()[nombre]);
            for (var vuelta = 0; vuelta < 2; vuelta++)
            {
                foreach (var bloque in VerificadorBaseDatos.TrocearParaProtocolo(limpio))
                {
                    await using var cmd = conexion.CreateCommand();
                    cmd.CommandText = bloque;
                    await cmd.ExecuteNonQueryAsync();   // no debe tirar
                }
            }
        }
    }

}
