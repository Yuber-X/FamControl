using FluentAssertions;
using MySqlConnector;
using FAControl.Common;
using FAControl.Data;
using FAControl.Models;
using FAControl.Services;

namespace FAControl.Data.Tests;

/// <summary>
/// El escenario que reenvió el cliente el 2026-08-27, corrido de punta a punta
/// contra MySQL real:
///
///   "si un cliente toma RD$1,000,000 y va pagando sus intereses mensualmente,
///    pero en noviembre realiza un abono de RD$200,000 al capital, ese monto
///    debe rebajarse directamente del saldo, quedando un capital pendiente de
///    RD$800,000. A partir del siguiente mes, los intereses deben calcularse
///    únicamente sobre los RD$800,000 restantes, no sobre el millón original."
///
/// Se prueba contra la base y no solo con la lógica pura porque lo que importa
/// es que el interés reescrito QUEDE GUARDADO y que el capital abonado
/// sobreviva a releer las cuotas (043).
///
/// Requiere MySQL local (root/root). Recrea la base en cada corrida.
/// </summary>
[Collection(ColeccionSesionData.Nombre)]   // SesionActual es global
public class PrestamoAbiertoAbonoTests : IAsyncLifetime
{
    private const string CadenaServidor = "Server=localhost;Port=3306;Uid=root;Pwd=root;";
    private const string CadenaTest = CadenaServidor + "Database=facontrol_abierto_test;";

    private const decimal Capital = 1_000_000m;
    private const decimal TasaMensual = 2m;     // 2% mensual → 20,000 sobre el millón
    private const int Plazo = 12;
    private const decimal InteresMensual = 20_000m;
    private const decimal Abono = 200_000m;

    private ConexionFactory _factory = null!;
    private PrestamoRepository _prestamoRepo = null!;
    private PrestamoService _prestamos = null!;
    private PagoService _pagos = null!;
    private long _clienteId;

    public async Task InitializeAsync()
    {
        await CrearBaseDeDatosDePruebaAsync();

        _factory = new ConexionFactory(CadenaTest);
        _prestamoRepo = new PrestamoRepository(_factory);
        var pagoRepo = new PagoRepository(_factory);
        var contador = new ContadorRepository();
        var auditoria = new AuditoriaService(new AuditoriaRepository(_factory),
            new SesionRepository(_factory), new UsuarioRepository(_factory));

        _prestamos = new PrestamoService(_factory, _prestamoRepo, contador,
            new AmortizacionService(), auditoria, new VehiculoRepository(_factory),
            new NcfRepository(_factory), pagoRepo);
        _pagos = new PagoService(_factory, _prestamoRepo, pagoRepo,
            new ClienteRepository(_factory), contador, auditoria,
            new AjustesLocales(), new NcfRepository(_factory));

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
        }
        using (var cmd = conexion.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO cliente (cedula, nombre, apellido)
                VALUES ('001-0000009-9', 'Cliente', 'Abierto');
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

    /// <summary>
    /// Préstamo abierto que vence el 1 de cada mes desde JUNIO de 2026, para
    /// que el escenario tenga las dos mitades que importan: cuotas ya vencidas
    /// (junio, julio, agosto) y cuotas por vencer (septiembre en adelante). Con
    /// un préstamo que arranca en el futuro no habría interés devengado que
    /// proteger, y la regla de alcance quedaría sin probar.
    /// </summary>
    private Task<(long Id, string Codigo)> CrearPrestamoAbiertoAsync() =>
        _prestamos.CrearAsync(new NuevoPrestamo(
            _clienteId, Capital, TasaMensual, Plazo, Modalidad.Mensual,
            MetodoAmortizacion.SoloInteres, new DateOnly(2026, 6, 1),
            Garantia: null, Notas: "Préstamo abierto de prueba"));

    /// <summary>
    /// El caso completo del cliente: paga interés, abona capital, y de ahí en
    /// adelante el interés baja.
    /// </summary>
    [Fact]
    public async Task El_abono_a_capital_baja_el_interes_de_las_cuotas_por_venir()
    {
        var (prestamoId, _) = await CrearPrestamoAbiertoAsync();

        // --- Antes del abono: 20,000 de interés en todas las cuotas ---
        var antes = await _prestamos.ObtenerCuotasAsync(prestamoId);
        antes.Should().OnlyContain(c => c.Interes == InteresMensual,
            "el interés arranca calculado sobre el millón entero");

        // --- Noviembre: paga su interés del mes Y abona 200,000 al capital ---
        await _pagos.RegistrarPagoAsync(new SolicitudPago(
            prestamoId, InteresMensual, MetodoPago.Efectivo,
            "Interés de noviembre + abono a capital", AbonoCapital: Abono));

        var despues = await _prestamos.ObtenerCuotasAsync(prestamoId);

        // --- El capital pendiente bajó a 800,000 y quedó GUARDADO (043) ---
        despues.Sum(c => c.CapitalPagado).Should().Be(Abono,
            "el abono se guarda como capital, no se deduce con 'primero interés'");
        (Capital - despues.Sum(c => c.CapitalPagado)).Should().Be(800_000m);

        // --- El interés de las cuotas POR VENCER pasó a 16,000 ---
        var hoy = FechaNegocio.Hoy;
        var porVencer = despues.Where(c => c.FechaVencimiento > hoy
                                        && c.Estado != EstadoCuota.Pagada).ToList();
        porVencer.Should().NotBeEmpty();
        porVencer.Should().OnlyContain(c => c.Interes == 16_000m,
            "2% de los 800,000 que quedan — es lo que pidió el cliente");

        // --- Y el monto total de la cuota acompaña al interés nuevo ---
        porVencer.Should().OnlyContain(c => c.MontoTotal == c.Capital + 16_000m);
    }

    /// <summary>
    /// La regla de alcance, que es la parte que puede costar plata si se hace
    /// mal: el interés YA DEVENGADO no se toca. Una cuota vencida se calculó
    /// sobre dinero que el deudor efectivamente tenía.
    /// </summary>
    [Fact]
    public async Task El_recalculo_no_toca_el_interes_ya_devengado()
    {
        var (prestamoId, _) = await CrearPrestamoAbiertoAsync();
        var hoy = FechaNegocio.Hoy;

        await _pagos.RegistrarPagoAsync(new SolicitudPago(
            prestamoId, InteresMensual, MetodoPago.Efectivo, null, AbonoCapital: Abono));

        var cuotas = await _prestamos.ObtenerCuotasAsync(prestamoId);
        var vencidas = cuotas.Where(c => c.FechaVencimiento <= hoy).ToList();

        vencidas.Should().NotBeEmpty("junio, julio y agosto de 2026 ya pasaron");
        vencidas.Should().OnlyContain(c => c.Interes == InteresMensual,
            "el interés de una cuota vencida no se baja hacia atrás: ya se devengó");
    }

    /// <summary>
    /// Dos abonos seguidos. Es el caso que delataba el modelo viejo: como la
    /// deducción "primero interés" se comía una cuota de interés de cada abono,
    /// el segundo se calculaba contra un capital inflado.
    /// </summary>
    [Fact]
    public async Task Dos_abonos_seguidos_acumulan_bien_el_capital()
    {
        var (prestamoId, _) = await CrearPrestamoAbiertoAsync();

        await _pagos.RegistrarPagoAsync(new SolicitudPago(
            prestamoId, InteresMensual, MetodoPago.Efectivo, null, AbonoCapital: Abono));
        await _pagos.RegistrarPagoAsync(new SolicitudPago(
            prestamoId, 16_000m, MetodoPago.Efectivo, null, AbonoCapital: 100_000m));

        var cuotas = await _prestamos.ObtenerCuotasAsync(prestamoId);

        cuotas.Sum(c => c.CapitalPagado).Should().Be(300_000m,
            "200,000 + 100,000, sin que la vieja deducción se comiera nada");

        var hoy = FechaNegocio.Hoy;
        cuotas.Where(c => c.FechaVencimiento > hoy && c.Estado != EstadoCuota.Pagada)
              .Should().OnlyContain(c => c.Interes == 14_000m,
                  "2% de los 700,000 que quedan");
    }

    /// <summary>
    /// Un cobro normal SIN abono no dispara nada: el interés queda como estaba.
    /// Sin esto, cualquier pago reescribiría la tabla del préstamo.
    /// </summary>
    [Fact]
    public async Task Un_cobro_sin_abono_no_recalcula_nada()
    {
        var (prestamoId, _) = await CrearPrestamoAbiertoAsync();

        await _pagos.RegistrarPagoAsync(new SolicitudPago(
            prestamoId, InteresMensual, MetodoPago.Efectivo, "Solo el interés del mes"));

        var cuotas = await _prestamos.ObtenerCuotasAsync(prestamoId);
        cuotas.Should().OnlyContain(c => c.Interes == InteresMensual,
            "sin abono a capital no hay nada que recalcular");
        cuotas.Sum(c => c.CapitalPagado).Should().Be(0m);
    }

    /// <summary>
    /// El recálculo es SOLO del préstamo abierto (decisión de Yuber 2026-08-27):
    /// en cuota fija la tabla se pacta al firmar y reescribirla cambiaría un
    /// contrato ya acordado con el cliente.
    /// </summary>
    [Fact]
    public async Task En_cuota_fija_el_abono_no_reescribe_la_tabla_pactada()
    {
        var (prestamoId, _) = await _prestamos.CrearAsync(new NuevoPrestamo(
            _clienteId, 100_000m, TasaMensual, 10, Modalidad.Mensual,
            MetodoAmortizacion.CuotaFija, new DateOnly(2026, 9, 1),
            Garantia: null, Notas: "Cuota fija de control"));

        var antes = await _prestamos.ObtenerCuotasAsync(prestamoId);
        var interesesAntes = antes.Select(c => c.Interes).ToList();

        await _pagos.RegistrarPagoAsync(new SolicitudPago(
            prestamoId, 0m, MetodoPago.Efectivo, null, AbonoCapital: 10_000m));

        var despues = await _prestamos.ObtenerCuotasAsync(prestamoId);
        despues.Select(c => c.Interes).Should().Equal(interesesAntes,
            "la tabla de un préstamo de cuota fija no se toca");
    }

    private static async Task CrearBaseDeDatosDePruebaAsync()
    {
        using var conexion = new MySqlConnection(CadenaServidor);
        await conexion.OpenAsync();
        using (var drop = conexion.CreateCommand())
        {
            drop.CommandText = "DROP DATABASE IF EXISTS facontrol_abierto_test;";
            await drop.ExecuteNonQueryAsync();
        }
        using (var crear = conexion.CreateCommand())
        {
            crear.CommandText = "CREATE DATABASE facontrol_abierto_test " +
                                "CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;";
            await crear.ExecuteNonQueryAsync();
        }
        await conexion.ChangeDatabaseAsync("facontrol_abierto_test");

        foreach (var bloque in VerificadorBaseDatos.ObtenerBloquesEjecutables())
        {
            using var cmd = conexion.CreateCommand();
            cmd.CommandText = bloque;
            cmd.CommandTimeout = 120;
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
