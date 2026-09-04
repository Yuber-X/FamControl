using FluentAssertions;
using MySqlConnector;
using FAControl.Common;
using FAControl.Data;
using FAControl.Models;
using FAControl.Services;

namespace FAControl.Data.Tests;

/// <summary>
/// Adopción del comprobante digitado a mano como secuencia predeterminada
/// (pedido del cliente 2026-09-03): "si se digita un NCF en cobros o en un
/// préstamo y la operación sale bien, ese mismo NCF se toma como el
/// predeterminado y se agrega en Configuración para continuar la secuencia".
///
/// Se prueba contra MySQL real porque lo que importa es el efecto en la tabla
/// <c>ncf_secuencia</c>: qué fila queda activa, en qué número queda y qué pasa
/// con el rango autorizado.
///
/// Requiere MySQL local (root/root). Recrea facontrol_ncfpred_test en cada corrida.
/// </summary>
[Collection(ColeccionSesionData.Nombre)]   // SesionActual es global
public class NcfPredeterminadoTests : IAsyncLifetime
{
    private const string CadenaServidor = "Server=localhost;Port=3306;Uid=root;Pwd=root;";
    private const string CadenaTest = CadenaServidor + "Database=facontrol_ncfpred_test;";

    private ConexionFactory _factory = null!;
    private NcfRepository _ncfRepo = null!;
    private NcfService _ncfServicio = null!;
    private long _clienteId;

    public async Task InitializeAsync()
    {
        await CrearBaseDeDatosDePruebaAsync();

        _factory = new ConexionFactory(CadenaTest);
        _ncfRepo = new NcfRepository(_factory);
        var prestamoRepo = new PrestamoRepository(_factory);
        var auditoria = new AuditoriaService(new AuditoriaRepository(_factory),
            new SesionRepository(_factory), new UsuarioRepository(_factory));
        _ncfServicio = new NcfService(_factory, _ncfRepo, prestamoRepo, auditoria);

        using var conexion = await _factory.AbrirAsync();
        using (var cmd = conexion.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO usuario (username, password_hash, nombre)
                VALUES ('test', 'hash-de-prueba', 'Usuario Test');
                SELECT LAST_INSERT_ID();
                """;
            var usuarioId = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            SesionActual.Iniciar(usuarioId, "test", "Usuario Test", Roles.Admin,
                Permisos.Todos, DateTime.UtcNow, 1);
            SesionActual.EstablecerModo(ModoApp.PrestControl);
        }
        using (var cmd = conexion.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO cliente (cedula, nombre, apellido)
                VALUES ('402-0000002-2', 'Cliente', 'Predeterminado');
                SELECT LAST_INSERT_ID();
                """;
            _clienteId = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        }
    }

    public Task DisposeAsync()
    {
        SesionActual.Cerrar();
        return Task.CompletedTask;
    }

    private static async Task CrearBaseDeDatosDePruebaAsync()
    {
        using var conexion = new MySqlConnection(CadenaServidor);
        await conexion.OpenAsync();
        using (var drop = conexion.CreateCommand())
        {
            drop.CommandText = "DROP DATABASE IF EXISTS facontrol_ncfpred_test;";
            await drop.ExecuteNonQueryAsync();
        }
        using (var crear = conexion.CreateCommand())
        {
            crear.CommandText =
                "CREATE DATABASE facontrol_ncfpred_test CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;";
            await crear.ExecuteNonQueryAsync();
        }
        await conexion.ChangeDatabaseAsync("facontrol_ncfpred_test");

        foreach (var bloque in VerificadorBaseDatos.ObtenerBloquesEjecutables())
        {
            using var cmd = conexion.CreateCommand();
            cmd.CommandText = bloque;
            cmd.CommandTimeout = 120;
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private Task GuardarSecuenciaAsync(string prefijo, long proxima,
        long? finRango = null, int largo = 8) =>
        _ncfServicio.GuardarSecuenciaAsync(new NcfSecuencia
        {
            Prefijo = prefijo, Largo = largo, Proxima = proxima,
            FinRango = finRango, Activo = true
        });

    // ====================================================================
    // Sin secuencia previa
    // ====================================================================

    [Fact]
    public async Task SinSecuencia_ElNcfDigitadoLaCrea()
    {
        (await _ncfServicio.ObtenerSecuenciaAsync()).Should().BeNull("arranca sin configurar");

        var movio = await _ncfRepo.AdoptarComoPredeterminadaAsync(ModoApp.PrestControl, "B0200000045");

        movio.Should().BeTrue();
        var secuencia = await _ncfServicio.ObtenerSecuenciaAsync();
        secuencia.Should().NotBeNull();
        secuencia!.Prefijo.Should().Be("B02");
        secuencia.Largo.Should().Be(8);
        secuencia.Proxima.Should().Be(46, "la secuencia sigue a partir del que se usó");
        secuencia.Activo.Should().BeTrue();
    }

    [Fact]
    public async Task SinSecuencia_ElMarcadorEmpiezaVacio()
    {
        // Regla del pedido: si no hay NCF configurado, ninguna caja muestra marcador.
        (await _ncfServicio.ProximoNcfAsync()).Should().BeNull();

        await _ncfRepo.AdoptarComoPredeterminadaAsync(ModoApp.PrestControl, "B0200000045");

        (await _ncfServicio.ProximoNcfAsync()).Should().Be("B0200000046");
    }

    // ====================================================================
    // Misma serie
    // ====================================================================

    [Fact]
    public async Task MismaSerie_MasAdelante_AdelantaLaSecuencia()
    {
        await GuardarSecuenciaAsync("B02", proxima: 10);

        var movio = await _ncfRepo.AdoptarComoPredeterminadaAsync(ModoApp.PrestControl, "B0200000045");

        movio.Should().BeTrue();
        (await _ncfServicio.ObtenerSecuenciaAsync())!.Proxima.Should().Be(46);
    }

    [Fact]
    public async Task MismaSerie_MasAtras_NoRetrocede()
    {
        // Única desviación deliberada del pedido literal: retroceder dentro de la
        // misma serie volvería a entregar números ya consumidos, que la DGII
        // prohíbe reusar y que uq_prestamo_ncf / uq_pago_ncf rechazarían después.
        await GuardarSecuenciaAsync("B02", proxima: 100);

        var movio = await _ncfRepo.AdoptarComoPredeterminadaAsync(ModoApp.PrestControl, "B0200000045");

        movio.Should().BeFalse();
        (await _ncfServicio.ObtenerSecuenciaAsync())!.Proxima.Should().Be(100);
    }

    [Fact]
    public async Task MismaSerie_ConservaElRangoAutorizado()
    {
        await GuardarSecuenciaAsync("B01", proxima: 1, finRango: 15);

        await _ncfRepo.AdoptarComoPredeterminadaAsync(ModoApp.PrestControl, "B0100000007");

        var secuencia = await _ncfServicio.ObtenerSecuenciaAsync();
        secuencia!.Proxima.Should().Be(8);
        secuencia.FinRango.Should().Be(15, "la autorización de la DGII no se pierde");
    }

    // ====================================================================
    // Serie distinta
    // ====================================================================

    [Fact]
    public async Task SerieDistinta_ApagaLaAnteriorYActivaLaNueva()
    {
        await GuardarSecuenciaAsync("B02", proxima: 30);

        var movio = await _ncfRepo.AdoptarComoPredeterminadaAsync(
            ModoApp.PrestControl, "E320000000011");

        movio.Should().BeTrue();
        var secuencia = await _ncfServicio.ObtenerSecuenciaAsync();
        secuencia!.Prefijo.Should().Be("E32", "ObtenerActivaAsync toma la primera activa por id");
        secuencia.Largo.Should().Be(10);
        secuencia.Proxima.Should().Be(12);
        secuencia.FinRango.Should().BeNull("de un talonario nuevo no se conoce el tope");
    }

    [Fact]
    public async Task SerieDistinta_MasAtras_SiSeAdopta()
    {
        // Un talonario nuevo trae su propia numeración: empezar por un número
        // más chico que el de la serie anterior no pisa nada.
        await GuardarSecuenciaAsync("B02", proxima: 900);

        var movio = await _ncfRepo.AdoptarComoPredeterminadaAsync(
            ModoApp.PrestControl, "B0100000003");

        movio.Should().BeTrue();
        var secuencia = await _ncfServicio.ObtenerSecuenciaAsync();
        secuencia!.Prefijo.Should().Be("B01");
        secuencia.Proxima.Should().Be(4);
    }

    [Fact]
    public async Task VolverALaSerieAnterior_RetomaSuPropiaNumeracion()
    {
        // "Si se vuelve a cambiar el NCF se aplica la misma lógica."
        await GuardarSecuenciaAsync("B02", proxima: 30);
        await _ncfRepo.AdoptarComoPredeterminadaAsync(ModoApp.PrestControl, "E320000000011");
        await _ncfRepo.AdoptarComoPredeterminadaAsync(ModoApp.PrestControl, "B0200000050");

        var secuencia = await _ncfServicio.ObtenerSecuenciaAsync();
        secuencia!.Prefijo.Should().Be("B02");
        secuencia.Proxima.Should().Be(51);
    }

    // ====================================================================
    // Lo que NO tiene que mover nada
    // ====================================================================

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no es un ncf")]
    [InlineData("B02")]
    public async Task TextoSinFormaDeComprobante_NoTocaLaSecuencia(string? basura)
    {
        await GuardarSecuenciaAsync("B02", proxima: 30);

        var movio = await _ncfRepo.AdoptarComoPredeterminadaAsync(ModoApp.PrestControl, basura);

        movio.Should().BeFalse();
        (await _ncfServicio.ObtenerSecuenciaAsync())!.Proxima.Should().Be(30);
    }

    [Fact]
    public async Task NoSeMezclaConLaSecuenciaDeOtraEstancia()
    {
        // Cada modo lleva su talonario (030): adoptar en PrestControl no puede
        // tocar el de DealerControl.
        await GuardarSecuenciaAsync("B02", proxima: 30);
        SesionActual.EstablecerModo(ModoApp.DealerControl);
        await GuardarSecuenciaAsync("B15", proxima: 7);

        await _ncfRepo.AdoptarComoPredeterminadaAsync(ModoApp.DealerControl, "B1500000080");

        var dealer = await _ncfRepo.ObtenerActivaAsync(ModoApp.DealerControl);
        dealer!.Prefijo.Should().Be("B15");
        dealer.Proxima.Should().Be(81);

        var prest = await _ncfRepo.ObtenerActivaAsync(ModoApp.PrestControl);
        prest!.Prefijo.Should().Be("B02");
        prest.Proxima.Should().Be(30, "la estancia de al lado no se movió");
    }

    // ====================================================================
    // El marcador
    // ====================================================================

    [Fact]
    public async Task ElMarcadorNoSeMuestra_SiLaSecuenciaNoSirve()
    {
        // Vencida
        await _ncfServicio.GuardarSecuenciaAsync(new NcfSecuencia
        {
            Prefijo = "B02", Largo = 8, Proxima = 1,
            Vencimiento = FechaNegocio.Hoy.AddDays(-1), Activo = true
        });
        (await _ncfServicio.ProximoNcfAsync()).Should().BeNull("está vencida");

        // Agotada. Va por el repositorio y no por el servicio a proposito: la
        // validacion de GuardarSecuenciaAsync no deja guardar proxima > fin, y
        // lo que hay que probar es como se comporta la app cuando la secuencia
        // YA se agoto por haber ido consumiendo numeros.
        await _ncfRepo.GuardarAsync(ModoApp.PrestControl, new NcfSecuencia
        {
            Prefijo = "B02", Largo = 8, Proxima = 16, FinRango = 15, Activo = true
        });
        (await _ncfServicio.ProximoNcfAsync()).Should().BeNull("se agotó el rango");

        // Apagada
        await _ncfServicio.GuardarSecuenciaAsync(new NcfSecuencia
        {
            Prefijo = "B02", Largo = 8, Proxima = 3, Activo = false
        });
        (await _ncfServicio.ProximoNcfAsync()).Should().BeNull("está desactivada");
    }

    [Fact]
    public async Task ElMarcadorMuestraExactamenteElProximo()
    {
        await GuardarSecuenciaAsync("B01", proxima: 7, finRango: 15);

        (await _ncfServicio.ProximoNcfAsync()).Should().Be("B0100000007");
    }

    // ====================================================================
    // Por el camino real
    // ====================================================================

    [Fact]
    public async Task AsignarUnNcfAManoAlPrestamo_LoDejaComoPredeterminado()
    {
        var prestamoRepo = new PrestamoRepository(_factory);
        var auditoria = new AuditoriaService(new AuditoriaRepository(_factory),
            new SesionRepository(_factory), new UsuarioRepository(_factory));
        var prestamos = new PrestamoService(_factory, prestamoRepo, new ContadorRepository(),
            new AmortizacionService(), auditoria, new VehiculoRepository(_factory),
            _ncfRepo, new PagoRepository(_factory), new PrestamoActaRepository(_factory));

        var (id, _) = await prestamos.CrearAsync(new NuevoPrestamo(
            _clienteId, 20_000m, 3m, 6, Modalidad.Mensual, MetodoAmortizacion.CuotaFija,
            FechaNegocio.Hoy, Garantia: null, Notas: "Sin comprobante al crear"));

        await _ncfServicio.AsignarAsync(id, "B0200000045");

        (await _ncfServicio.ProximoNcfAsync()).Should().Be("B0200000046",
            "el comprobante pegado a mano pasa a ser el predeterminado");
    }

    [Fact]
    public async Task CrearUnPrestamoConNcfAMano_LoDejaComoPredeterminado()
    {
        var prestamoRepo = new PrestamoRepository(_factory);
        var auditoria = new AuditoriaService(new AuditoriaRepository(_factory),
            new SesionRepository(_factory), new UsuarioRepository(_factory));
        var prestamos = new PrestamoService(_factory, prestamoRepo, new ContadorRepository(),
            new AmortizacionService(), auditoria, new VehiculoRepository(_factory),
            _ncfRepo, new PagoRepository(_factory), new PrestamoActaRepository(_factory));

        await prestamos.CrearAsync(new NuevoPrestamo(
            _clienteId, 15_000m, 3m, 6, Modalidad.Mensual, MetodoAmortizacion.CuotaFija,
            FechaNegocio.Hoy, Garantia: null, Notas: "Con e-NCF pegado",
            Ncf: "E320000000011"));

        var secuencia = await _ncfServicio.ObtenerSecuenciaAsync();
        secuencia!.Prefijo.Should().Be("E32");
        secuencia.Proxima.Should().Be(12);
    }
}
