using FluentAssertions;
using MySqlConnector;
using FAControl.Common;
using FAControl.Data;
using FAControl.Models;
using FAControl.Services;

namespace FAControl.Data.Tests;

/// <summary>
/// Cierre y correccion de alquileres (031), contra MySQL real.
///
/// LO QUE SE VERIFICA, y por que importa:
///  * Devolver TARDE recalcula lo que corresponde cobrar. Sin esto el sistema
///    seguiria mostrando el monto pactado como si nada hubiera cambiado.
///  * Cancelar NO cuenta como ingreso, aunque por dentro se parezca a devolver.
///    Es la razon por la que los dos botones se fundieron en uno que PREGUNTA
///    en vez de en una sola accion a ciegas.
///  * Un alquiler cerrado no se corrige: ya se liquido y el cliente pago sobre
///    esos numeros.
///  * En las dos formas de cerrar, el vehiculo vuelve al inventario.
///
/// Requiere MySQL local (root/root). Recrea su base en cada corrida.
/// </summary>
[Collection(ColeccionSesionData.Nombre)]   // SesionActual es global
public class FlujoAlquilerTests : IAsyncLifetime
{
    private const string CadenaServidor = "Server=localhost;Port=3306;Uid=root;Pwd=root;";
    private const string Bd = "facontrol_alquiler_test";
    private const string Cadena = CadenaServidor + $"Database={Bd};";

    private AlquilerService _alquileres = null!;
    private VehiculoRepository _vehiculos = null!;
    private long _clienteId;
    private long _vehiculoId;

    public async Task InitializeAsync()
    {
        await BorrarBaseAsync();
        await new VerificadorBaseDatos(Cadena).CrearEsquemaAsync();

        var fabrica = new ConexionFactory(Cadena);
        _vehiculos = new VehiculoRepository(fabrica);
        var auditoria = new AuditoriaService(new AuditoriaRepository(fabrica),
            new SesionRepository(fabrica), new UsuarioRepository(fabrica));

        _alquileres = new AlquilerService(new AlquilerRepository(fabrica), _vehiculos,
            new ClienteRepository(fabrica), new ContadorRepository(), fabrica, auditoria);

        await using var conexion = new MySqlConnection(Cadena);
        await conexion.OpenAsync();

        await using (var cmd = conexion.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO usuario (username, password_hash, nombre)
                VALUES ('encargado', 'hash-de-prueba', 'Encargado Test');
                SELECT LAST_INSERT_ID();
                """;
            var usuarioId = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            SesionActual.Iniciar(usuarioId, "encargado", "Encargado Test", Roles.Admin,
                Permisos.Todos, DateTime.UtcNow, 1);
            SesionActual.EstablecerModo(ModoApp.DealerControl);
        }
        await using (var cmd = conexion.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO cliente (cedula, nombre, apellido, ambito)
                VALUES ('001-0000007-7', 'Pedro', 'Alquila', 'dealercontrol');
                SELECT LAST_INSERT_ID();
                """;
            _clienteId = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        }
        await using (var cmd = conexion.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO vehiculo (codigo, marca, modelo, anio, precio_venta, estado)
                VALUES ('V-7001', 'Kia', 'Rio', 2022, 800000, 'disponible');
                SELECT LAST_INSERT_ID();
                """;
            _vehiculoId = Convert.ToInt64(await cmd.ExecuteScalarAsync());
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

    /// <summary>Alquiler de 5 dias a 2,000 = 10,000, empezando hace 5 dias.</summary>
    private Task<(long Id, string Codigo)> CrearAlquilerAsync(int diasAtras = 5, int dias = 5) =>
        _alquileres.RegistrarAsync(new AlquilerDatos(
            _vehiculoId, _clienteId,
            FechaNegocio.Hoy.AddDays(-diasAtras),
            FechaNegocio.Hoy.AddDays(-diasAtras + dias),
            TarifaDia: 2_000m,
            Notas: "Integracion"));

    [Fact]
    public async Task DevolverATiempo_CobraLoPactado_YLiberaElVehiculo()
    {
        var (id, codigo) = await CrearAlquilerAsync();

        // Sale del inventario al alquilar
        (await _vehiculos.ObtenerPorIdAsync(_vehiculoId))!.Estado
            .Should().Be(EstadoVehiculo.Alquilado);

        var r = await _alquileres.CerrarAsync(new CierreAlquilerDatos(
            id, CierreAlquiler.Devuelto, "Devolucion normal",
            FechaDevolucion: FechaNegocio.Hoy));

        r.Codigo.Should().Be(codigo);
        r.DiasReales.Should().Be(5);
        r.MontoFinal.Should().Be(10_000m);
        r.Diferencia.Should().Be(0m);
        r.DevolvioTarde.Should().BeFalse();

        (await _vehiculos.ObtenerPorIdAsync(_vehiculoId))!.Estado
            .Should().Be(EstadoVehiculo.Disponible, "el vehiculo vuelve al inventario");
    }

    /// <summary>
    /// El caso que hace falta el recalculo: el cliente se quedo 3 dias de mas.
    /// </summary>
    [Fact]
    public async Task DevolverTarde_RecalculaLoQueCorrespondeCobrar()
    {
        // Empezo hace 8 dias, pactado por 5 -> vencia hace 3
        var (id, _) = await CrearAlquilerAsync(diasAtras: 8, dias: 5);

        var r = await _alquileres.CerrarAsync(new CierreAlquilerDatos(
            id, CierreAlquiler.Devuelto, "Se quedo tres dias de mas",
            FechaDevolucion: FechaNegocio.Hoy));

        r.DiasPactados.Should().Be(5);
        r.DiasReales.Should().Be(8);
        r.MontoPactado.Should().Be(10_000m);
        r.MontoFinal.Should().Be(16_000m, "8 dias x 2,000");
        r.Diferencia.Should().Be(6_000m);
        r.DevolvioTarde.Should().BeTrue();

        // Y queda guardado: el monto pactado NO se pisa, se guarda aparte
        var alquiler = await _alquileres.ObtenerPorIdAsync(id);
        alquiler!.MontoTotal.Should().Be(10_000m, "lo pactado no se reescribe");
        alquiler.MontoFinal.Should().Be(16_000m);
        alquiler.DiasReales.Should().Be(8);
        alquiler.Estado.Should().Be(EstadoAlquiler.Finalizado);
    }

    /// <summary>
    /// Cancelar y devolver NO son lo mismo, aunque por dentro se parezcan: es
    /// justo la razon por la que el boton unico PREGUNTA cual es.
    /// </summary>
    [Fact]
    public async Task Cancelar_NoCuentaComoIngreso_PeroIgualLiberaElVehiculo()
    {
        var (id, _) = await CrearAlquilerAsync();

        var r = await _alquileres.CerrarAsync(new CierreAlquilerDatos(
            id, CierreAlquiler.Cancelado, "El cliente no vino a retirar"));

        r.MontoFinal.Should().Be(0m, "el contrato no corrio");
        r.DiasReales.Should().Be(0);

        var alquiler = await _alquileres.ObtenerPorIdAsync(id);
        alquiler!.Estado.Should().Be(EstadoAlquiler.Cancelado);
        alquiler.FechaDevolucion.Should().BeNull("no hubo devolucion: el auto nunca corrio el contrato");
        alquiler.CerradoMotivo.Should().Be("El cliente no vino a retirar");

        (await _vehiculos.ObtenerPorIdAsync(_vehiculoId))!.Estado
            .Should().Be(EstadoVehiculo.Disponible);
    }

    [Fact]
    public async Task NoSePuedeCerrarDosVeces()
    {
        var (id, _) = await CrearAlquilerAsync();
        await _alquileres.CerrarAsync(new CierreAlquilerDatos(
            id, CierreAlquiler.Devuelto, "Primera vez"));

        var otra = async () => await _alquileres.CerrarAsync(new CierreAlquilerDatos(
            id, CierreAlquiler.Cancelado, "Segunda vez"));

        (await otra.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*ya esta cerrado*");
    }

    [Fact]
    public async Task CerrarSinMotivo_SeRechaza()
    {
        var (id, _) = await CrearAlquilerAsync();

        var cerrar = async () => await _alquileres.CerrarAsync(new CierreAlquilerDatos(
            id, CierreAlquiler.Devuelto, "   "));

        await cerrar.Should().ThrowAsync<ArgumentException>();

        (await _alquileres.ObtenerPorIdAsync(id))!.Estado
            .Should().Be(EstadoAlquiler.Activo, "no se cerro nada");
    }

    /// <summary>Corregir un tipeo mientras el contrato sigue abierto.</summary>
    [Fact]
    public async Task CorregirAlquilerActivo_RecalculaDiasYTotal()
    {
        var (id, _) = await CrearAlquilerAsync(diasAtras: 2, dias: 5);

        // Se habia escrito 2,000 y era 2,500; y eran 10 dias, no 5
        var inicio = FechaNegocio.Hoy.AddDays(-2);
        await _alquileres.EditarAsync(new EdicionAlquiler(
            id, inicio, inicio.AddDays(10), TarifaDia: 2_500m,
            Notas: "Corregido", Motivo: "Se digito mal la tarifa y el plazo"));

        var alquiler = await _alquileres.ObtenerPorIdAsync(id);
        alquiler!.TarifaDia.Should().Be(2_500m);
        alquiler.Dias.Should().Be(10);
        alquiler.MontoTotal.Should().Be(25_000m, "dias y total se recalculan, no se digitan");
        alquiler.Notas.Should().Be("Corregido");
    }

    /// <summary>
    /// Cerrado y liquidado, los numeros quedan como quedaron: el cliente ya pago
    /// sobre ellos y cambiarlos haria que la caja del dia deje de cuadrar.
    /// </summary>
    [Fact]
    public async Task NoSePuedeCorregirUnAlquilerYaCerrado()
    {
        var (id, _) = await CrearAlquilerAsync();
        await _alquileres.CerrarAsync(new CierreAlquilerDatos(
            id, CierreAlquiler.Devuelto, "Devuelto"));

        var inicio = FechaNegocio.Hoy.AddDays(-5);
        var editar = async () => await _alquileres.EditarAsync(new EdicionAlquiler(
            id, inicio, inicio.AddDays(9), 3_000m, null, "Intento tardio"));

        (await editar.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*ya esta cerrado y liquidado*");
    }

    /// <summary>Sin el permiso no se cierra, aunque la pantalla lo dejara pasar.</summary>
    [Fact]
    public async Task CerrarSinPermiso_SeRechaza()
    {
        var (id, _) = await CrearAlquilerAsync();

        // Un vendedor: alquila, pero no cierra contratos ajenos
        SesionActual.Iniciar(SesionActual.Id, "vendedor", "Vendedor", Roles.Vendedor,
            [Permisos.Alquileres, Permisos.Inventario], DateTime.UtcNow, 1);

        var cerrar = async () => await _alquileres.CerrarAsync(new CierreAlquilerDatos(
            id, CierreAlquiler.Devuelto, "Prueba"));

        await cerrar.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    // ---------- Cobros del alquiler (034) ----------

    /// <summary>
    /// El caso real: adelanto al retirar el vehiculo y el resto al devolverlo.
    /// Antes esto no tenia donde anotarse.
    /// </summary>
    [Fact]
    public async Task SeCobraEnDosVeces_ConReciboPropioCadaUno()
    {
        var (id, _) = await CrearAlquilerAsync();   // 5 dias x 2,000 = 10,000

        var adelanto = await _alquileres.RegistrarCobroAsync(
            new CobroAlquiler(id, 4_000m, MetodoPago.Efectivo, "Adelanto al retirar"));
        adelanto.NumeroRecibo.Should().Be("RA-000001");

        var estado = await _alquileres.ObtenerEstadoCobroAsync(id);
        estado.Cobrado.Should().Be(4_000m);
        estado.Pendiente.Should().Be(6_000m);
        estado.EstaSaldado.Should().BeFalse();

        var resto = await _alquileres.RegistrarCobroAsync(
            new CobroAlquiler(id, 6_000m, MetodoPago.Transferencia, "Resto al devolver"));
        resto.NumeroRecibo.Should().Be("RA-000002", "cada cobro lleva su propio recibo");

        estado = await _alquileres.ObtenerEstadoCobroAsync(id);
        estado.Cobrado.Should().Be(10_000m);
        estado.EstaSaldado.Should().BeTrue();
        estado.Pagos.Should().HaveCount(2);
    }

    /// <summary>No se cobra mas de lo que falta.</summary>
    [Fact]
    public async Task NoSePuedeCobrarMasDeLoQueFalta()
    {
        var (id, _) = await CrearAlquilerAsync();
        await _alquileres.RegistrarCobroAsync(new CobroAlquiler(id, 8_000m, MetodoPago.Efectivo));

        var excesivo = async () => await _alquileres.RegistrarCobroAsync(
            new CobroAlquiler(id, 5_000m, MetodoPago.Efectivo));

        (await excesivo.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*solo le faltan*");

        // El rollback dejo todo intacto y NO quemo un numero de recibo
        var estado = await _alquileres.ObtenerEstadoCobroAsync(id);
        estado.Cobrado.Should().Be(8_000m);
        estado.Pagos.Should().ContainSingle().Which.NumeroRecibo.Should().Be("RA-000001");
    }

    /// <summary>
    /// Devolvio TARDE: el monto a cobrar sube al cerrar (031) y lo ya cobrado
    /// se mide contra el monto real, no contra el pactado.
    /// </summary>
    [Fact]
    public async Task AlCerrarTarde_SubeLoQueFaltaPorCobrar()
    {
        // Empezo hace 8 dias, pactado por 5 -> 10,000 pactados
        var (id, _) = await CrearAlquilerAsync(diasAtras: 8, dias: 5);
        await _alquileres.RegistrarCobroAsync(new CobroAlquiler(id, 10_000m, MetodoPago.Efectivo));

        (await _alquileres.ObtenerEstadoCobroAsync(id)).EstaSaldado
            .Should().BeTrue("contra lo pactado esta al dia");

        // Devuelve hoy: 8 dias reales x 2,000 = 16,000
        await _alquileres.CerrarAsync(new CierreAlquilerDatos(
            id, CierreAlquiler.Devuelto, "Se quedo tres dias de mas",
            FechaDevolucion: FechaNegocio.Hoy));

        var estado = await _alquileres.ObtenerEstadoCobroAsync(id);
        estado.MontoACobrar.Should().Be(16_000m, "manda el monto REAL, no el pactado");
        estado.Pendiente.Should().Be(6_000m);
        estado.EstaSaldado.Should().BeFalse();

        // Y se le puede cobrar la diferencia
        var extra = await _alquileres.RegistrarCobroAsync(
            new CobroAlquiler(id, 6_000m, MetodoPago.Efectivo, "Dias de atraso"));
        extra.Monto.Should().Be(6_000m);
        (await _alquileres.ObtenerEstadoCobroAsync(id)).EstaSaldado.Should().BeTrue();
    }

    /// <summary>
    /// Devolvio ANTES y ya habia pagado todo: queda saldo a favor. La plata no
    /// se mueve sola — la pantalla avisa y el dueño decide.
    /// </summary>
    [Fact]
    public async Task SiPagoDeMas_QuedaSaldoAFavor()
    {
        // Pactado 5 dias = 10,000; paga todo por adelantado
        var (id, _) = await CrearAlquilerAsync(diasAtras: 5, dias: 5);
        await _alquileres.RegistrarCobroAsync(new CobroAlquiler(id, 10_000m, MetodoPago.Efectivo));

        // Devuelve a los 2 dias: 2 x 2,000 = 4,000
        await _alquileres.CerrarAsync(new CierreAlquilerDatos(
            id, CierreAlquiler.Devuelto, "Devolvio antes",
            FechaDevolucion: FechaNegocio.Hoy.AddDays(-3)));

        var estado = await _alquileres.ObtenerEstadoCobroAsync(id);
        estado.MontoACobrar.Should().Be(4_000m);
        estado.Pendiente.Should().Be(0m);
        estado.SaldoAFavor.Should().Be(6_000m, "pago 10,000 y correspondian 4,000");
    }

    /// <summary>A un alquiler cancelado no se le cobra.</summary>
    [Fact]
    public async Task NoSeCobraUnAlquilerCancelado()
    {
        var (id, _) = await CrearAlquilerAsync();
        await _alquileres.CerrarAsync(new CierreAlquilerDatos(
            id, CierreAlquiler.Cancelado, "El cliente no vino"));

        var cobrar = async () => await _alquileres.RegistrarCobroAsync(
            new CobroAlquiler(id, 1_000m, MetodoPago.Efectivo));

        (await cobrar.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*cancelado*");
    }

    // ---------- Calendario mensual (037) ----------

    /// <summary>
    /// Un alquiler largo se cobra MES A MES, no de una: "la idea del grid es
    /// que almacene la cantidad de cobros mensuales hasta el dia pactado".
    /// </summary>
    [Fact]
    public async Task UnAlquilerDeTresMeses_SePartesEnTresCuotasMensuales()
    {
        var inicio = FechaNegocio.Hoy;
        var (id, _) = await _alquileres.RegistrarAsync(new AlquilerDatos(
            _vehiculoId, _clienteId, inicio, inicio.AddMonths(3), TarifaDia: 1_000m, Notas: null));

        var estado = await _alquileres.ObtenerEstadoCobroAsync(id);

        estado.Calendario.Should().HaveCount(3, "tres meses, tres cuotas");
        estado.Calendario[0].Desde.Should().Be(inicio);
        estado.Calendario[0].Hasta.Should().Be(inicio.AddMonths(1));
        estado.Calendario[2].Hasta.Should().Be(inicio.AddMonths(3));

        // La suma de las cuotas da EXACTO el monto a cobrar: repartir por
        // division deja centavos sueltos y la ultima los absorbe.
        estado.Calendario.Sum(c => c.Monto).Should().Be(estado.MontoACobrar);
    }

    /// <summary>
    /// Lo cobrado se aplica en cascada sobre el calendario: satura la primera
    /// cuota y sigue con la segunda. Y el alquiler NO queda saldado por pagar
    /// un mes — que era justo lo que hacia desaparecer el formulario.
    /// </summary>
    [Fact]
    public async Task CobrarUnMes_NoSaldaElAlquilerLargo()
    {
        var inicio = FechaNegocio.Hoy;
        var (id, _) = await _alquileres.RegistrarAsync(new AlquilerDatos(
            _vehiculoId, _clienteId, inicio, inicio.AddMonths(3), TarifaDia: 1_000m, Notas: null));

        var estado = await _alquileres.ObtenerEstadoCobroAsync(id);
        var primerMes = estado.Calendario[0].Monto;

        await _alquileres.RegistrarCobroAsync(
            new CobroAlquiler(id, primerMes, MetodoPago.Efectivo, "Primer mes"));

        estado = await _alquileres.ObtenerEstadoCobroAsync(id);
        estado.EstaSaldado.Should().BeFalse("faltan dos meses");
        estado.Calendario[0].EstaPagada.Should().BeTrue();
        estado.Calendario[1].EstaPagada.Should().BeFalse();
        estado.Calendario[1].Pagado.Should().Be(0m);
    }

    /// <summary>Un abono parcial deja la cuota cubierta a medias, no saldada.</summary>
    [Fact]
    public async Task UnAbonoParcial_CubreLaCuotaAMedias()
    {
        var inicio = FechaNegocio.Hoy;
        var (id, _) = await _alquileres.RegistrarAsync(new AlquilerDatos(
            _vehiculoId, _clienteId, inicio, inicio.AddMonths(2), TarifaDia: 1_000m, Notas: null));

        await _alquileres.RegistrarCobroAsync(new CobroAlquiler(id, 5_000m, MetodoPago.Efectivo));

        var estado = await _alquileres.ObtenerEstadoCobroAsync(id);
        estado.Calendario[0].Pagado.Should().Be(5_000m);
        estado.Calendario[0].EstaPagada.Should().BeFalse();
        estado.Calendario[0].Pendiente.Should().Be(estado.Calendario[0].Monto - 5_000m);
    }

    /// <summary>Un alquiler corto (menos de un mes) da una sola cuota.</summary>
    [Fact]
    public async Task UnAlquilerCorto_DaUnaSolaCuota()
    {
        var (id, _) = await CrearAlquilerAsync();   // 5 dias

        var estado = await _alquileres.ObtenerEstadoCobroAsync(id);

        estado.Calendario.Should().ContainSingle();
        estado.Calendario[0].Monto.Should().Be(10_000m);
        estado.Calendario[0].Dias.Should().Be(5);
    }
}
