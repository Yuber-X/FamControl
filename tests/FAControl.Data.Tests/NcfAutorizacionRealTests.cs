using FluentAssertions;
using MySqlConnector;
using FAControl.Common;
using FAControl.Data;
using FAControl.Models;
using FAControl.Services;

namespace FAControl.Data.Tests;

/// <summary>
/// Integración del comprobante fiscal con la autorización REAL de la DGII
/// (constancia del 29/07/2026, autorización 6005407803): B01 Factura de Crédito
/// Fiscal, B0100000001 a B0100000015, vence 31/12/2027.
///
/// Prueba lo que el cliente va a hacer de verdad — la "vía simple": crear el
/// préstamo pidiendo comprobante y que la app tome el siguiente de la secuencia.
/// Lo crítico es que no se repita ni se salte ninguno, y que al llegar a 15 la
/// app bloquee en vez de inventar el 16.
///
/// Requiere MySQL local (root/root). Recrea facontrol_test en cada corrida.
/// </summary>
[Collection(ColeccionSesionData.Nombre)]   // SesionActual es global
public class NcfAutorizacionRealTests : IAsyncLifetime
{
    private const string CadenaServidor = "Server=localhost;Port=3306;Uid=root;Pwd=root;";
    private const string CadenaTest = CadenaServidor + "Database=facontrol_ncf_test;";

    // La constancia de la DGII, tal cual
    private const string Prefijo = "B01";
    private const int Largo = 8;
    private const int Aprobados = 15;
    private static readonly DateOnly Vencimiento = new(2027, 12, 31);

    private ConexionFactory _factory = null!;
    private NcfRepository _ncfRepo = null!;
    private PrestamoService _prestamos = null!;
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

        _prestamos = new PrestamoService(_factory, prestamoRepo, new ContadorRepository(),
            new AmortizacionService(), auditoria, new VehiculoRepository(_factory),
            _ncfRepo, new PagoRepository(_factory), new PrestamoActaRepository(_factory));
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
            // La secuencia es POR MODO (030): se fija explícito para no depender
            // de lo que haya dejado otro test de la colección.
            SesionActual.EstablecerModo(ModoApp.PrestControl);
        }
        using (var cmd = conexion.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO cliente (cedula, nombre, apellido)
                VALUES ('402-0000001-1', 'Cliente', 'Fiscal');
                SELECT LAST_INSERT_ID();
                """;
            _clienteId = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        }

        // La autorización real, como la deja 019_ncf_autorizacion_dgii.sql
        await _ncfServicio.GuardarSecuenciaAsync(new NcfSecuencia
        {
            Prefijo = Prefijo, Largo = Largo, Proxima = 1,
            FinRango = Aprobados, Vencimiento = Vencimiento, Activo = true
        });
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
            drop.CommandText = "DROP DATABASE IF EXISTS facontrol_ncf_test;";
            await drop.ExecuteNonQueryAsync();
        }
        using (var crear = conexion.CreateCommand())
        {
            crear.CommandText =
                "CREATE DATABASE facontrol_ncf_test CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;";
            await crear.ExecuteNonQueryAsync();
        }
        await conexion.ChangeDatabaseAsync("facontrol_ncf_test");

        foreach (var bloque in VerificadorBaseDatos.ObtenerBloquesEjecutables())
        {
            using var cmd = conexion.CreateCommand();
            cmd.CommandText = bloque;
            cmd.CommandTimeout = 120;
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private Task<(long Id, string Codigo)> CrearPrestamoConComprobanteAsync() =>
        _prestamos.CrearAsync(new NuevoPrestamo(
            _clienteId, 20_000m, 3m, 6, Modalidad.Mensual, MetodoAmortizacion.CuotaFija,
            FechaNegocio.Hoy, Garantia: null, Notas: "Prueba de comprobante fiscal",
            AsignarNcfAuto: true));

    [Fact]
    public async Task Los15ComprobantesSalenEnOrden_YElNumero16SeBloquea()
    {
        var emitidos = new List<string>();

        // ===== Los 15 autorizados, por el camino real (crear préstamo) =====
        for (var i = 1; i <= Aprobados; i++)
        {
            var (id, _) = await CrearPrestamoConComprobanteAsync();
            var prestamo = await new PrestamoRepository(_factory).ObtenerPorIdAsync(id);
            prestamo!.Ncf.Should().NotBeNullOrWhiteSpace();
            emitidos.Add(prestamo.Ncf!);
        }

        // Exactamente los de la constancia: del primero al último, sin repetir
        emitidos.Should().HaveCount(Aprobados);
        emitidos.Should().OnlyHaveUniqueItems();
        emitidos[0].Should().Be("B0100000001");
        emitidos[^1].Should().Be("B0100000015");
        emitidos.Should().BeInAscendingOrder();

        // ===== El 16: la app tiene que negarse, no inventar un número =====
        var extra = () => CrearPrestamoConComprobanteAsync();

        (await extra.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*agotó*");

        // Y la secuencia quedó donde debía: 16 pedida sobre un tope de 15
        var secuencia = await _ncfRepo.ObtenerActivaAsync(SesionActual.Modo);
        secuencia!.Proxima.Should().Be(Aprobados + 1);
        secuencia.EstaAgotada.Should().BeTrue();
        secuencia.Restantes.Should().Be(0);
    }

    /// <summary>
    /// Si la operación se cae después de reservar, el número NO se quema: el
    /// UPDATE de `proxima` vive dentro de la misma transacción del préstamo.
    /// Sin esto se perderían comprobantes autorizados, y solo hay 15.
    /// </summary>
    [Fact]
    public async Task UnRollbackNoQuemaElComprobante()
    {
        using var conexion = await _factory.AbrirAsync();
        using (var transaccion = await conexion.BeginTransactionAsync())
        {
            var ncf = await _ncfRepo.ReservarSiguienteAsync(SesionActual.Modo, conexion, transaccion, FechaNegocio.Hoy);
            ncf.Should().Be("B0100000001");
            await transaccion.RollbackAsync();
        }

        var secuencia = await _ncfRepo.ObtenerActivaAsync(SesionActual.Modo);
        secuencia!.Proxima.Should().Be(1);   // sigue disponible
    }

    private PagoService CrearServicioDePagos() =>
        new(_factory, new PrestamoRepository(_factory),
            new PagoRepository(_factory), new ClienteRepository(_factory),
            new ContadorRepository(),
            new AuditoriaService(new AuditoriaRepository(_factory),
                new SesionRepository(_factory), new UsuarioRepository(_factory)),
            new AjustesLocales(), _ncfRepo);

    // Cuota de 20,000 al 3% x 6 = 3,333.33 capital + 600 interes
    private const decimal UnaCuota = 3_933.33m;

    /// <summary>
    /// El último eslabón: el comprobante tiene que llegar al RECIBO que se le
    /// entrega al cliente. Si se queda en la base no sirve de nada ante la DGII.
    /// </summary>
    [Fact]
    public async Task El_comprobante_sale_impreso_en_el_recibo_del_cobro()
    {
        var (prestamoId, _) = await CrearPrestamoConComprobanteAsync();
        var pagos = CrearServicioDePagos();

        var resultado = await pagos.RegistrarPagoAsync(new SolicitudPago(
            prestamoId, UnaCuota, MetodoPago.Efectivo, "Prueba de comprobante",
            AsignarNcfAuto: true));

        // El préstamo se llevó el B0100000001 al crearse; este cobro consume
        // el SIGUIENTE de la secuencia, no repite el del préstamo.
        resultado.Recibo.Ncf.Should().Be("B0100000002");
    }

    /// <summary>
    /// El reporte de Verónica (2026-08-26): "las facturas de ese préstamos
    /// todas salen con es NCF y eso se debe cambiar con cada factura".
    ///
    /// Hasta el 041 el recibo copiaba `prestamo.Ncf`, así que las 24 facturas
    /// de un préstamo salían con UN solo comprobante repetido. Ante la DGII
    /// cada factura ampara su propio comprobante: dos cobros, dos números.
    /// </summary>
    [Fact]
    public async Task Cada_cobro_del_mismo_prestamo_consume_su_propio_comprobante()
    {
        var (prestamoId, _) = await CrearPrestamoConComprobanteAsync();
        var pagos = CrearServicioDePagos();

        var primero = await pagos.RegistrarPagoAsync(new SolicitudPago(
            prestamoId, UnaCuota, MetodoPago.Efectivo, null, AsignarNcfAuto: true));
        var segundo = await pagos.RegistrarPagoAsync(new SolicitudPago(
            prestamoId, UnaCuota, MetodoPago.Efectivo, null, AsignarNcfAuto: true));

        primero.Recibo.Ncf.Should().Be("B0100000002");
        segundo.Recibo.Ncf.Should().Be("B0100000003");
        segundo.Recibo.Ncf.Should().NotBe(primero.Recibo.Ncf,
            "cada factura consume su propio comprobante (regla DGII)");
    }

    /// <summary>
    /// Un cobro SIN comprobante no hereda el del préstamo. Antes del 041 sí lo
    /// hacía, y ese era exactamente el número repetido que el cliente reportó.
    /// </summary>
    [Fact]
    public async Task Un_cobro_sin_comprobante_no_hereda_el_del_prestamo()
    {
        var (prestamoId, _) = await CrearPrestamoConComprobanteAsync();
        var pagos = CrearServicioDePagos();

        var prestamo = await new PrestamoRepository(_factory).ObtenerPorIdAsync(prestamoId);
        prestamo!.Ncf.Should().Be("B0100000001", "el préstamo sí tiene el suyo");

        var resultado = await pagos.RegistrarPagoAsync(new SolicitudPago(
            prestamoId, UnaCuota, MetodoPago.Efectivo, null));

        resultado.Recibo.Ncf.Should().BeNull(
            "el recibo de un cobro sin comprobante no muestra ninguno");
    }

    /// <summary>
    /// El e-NCF pegado a mano (Facturador Gratuito de la DGII) manda tal cual y
    /// en mayúsculas, y desde el 2026-09-03 ADEMÁS pasa a ser el predeterminado.
    ///
    /// Esta prueba afirmaba lo contrario ("no toca la secuencia local"), que era
    /// el contrato hasta el 2026-09-02: el número lo había emitido el portal de
    /// la DGII, no la app, así que la secuencia local no se movía. El cliente
    /// pidió justo lo opuesto —"si se digita un NCF y la operación sale bien,
    /// ese mismo NCF se toma como el predeterminado para continuar la
    /// secuencia"— porque en la práctica dejaron de usar el talonario local y
    /// numeran desde el Facturador: obligarlos a corregir Configuración a mano
    /// después de cada cobro no tenía sentido.
    ///
    /// Se reescribe en vez de borrarse para que quede asentado que el cambio
    /// fue pedido y no un descuido.
    /// </summary>
    [Fact]
    public async Task El_comprobante_pegado_a_mano_va_al_recibo_y_queda_de_predeterminado()
    {
        var (prestamoId, _) = await CrearPrestamoConComprobanteAsync();
        var pagos = CrearServicioDePagos();

        var resultado = await pagos.RegistrarPagoAsync(new SolicitudPago(
            prestamoId, UnaCuota, MetodoPago.Efectivo, null, Ncf: "e320000000045"));

        resultado.Recibo.Ncf.Should().Be("E320000000045");

        var secuencia = (await _ncfRepo.ObtenerActivaAsync(SesionActual.Modo))!;
        secuencia.Prefijo.Should().Be("E32", "la serie pegada a mano pasa a ser la activa");
        secuencia.Largo.Should().Be(10);
        secuencia.Proxima.Should().Be(46, "la secuencia continúa a partir del que se usó");
    }

    /// <summary>
    /// Cada estancia lleva su PROPIA secuencia (030). Es el pedido del cliente:
    /// "en comprobante fiscal debe de estar vacío por cada modo... puede generar
    /// conflictos por si son una empresa de multi-desempeños".
    ///
    /// Lo que se prueba: configurar el dealer no toca lo de PrestControl, los
    /// dos pueden usar el mismo prefijo con rangos distintos, y consumir de uno
    /// no mueve el contador del otro.
    /// </summary>
    [Fact]
    public async Task CadaEstanciaLlevaSuPropiaSecuencia()
    {
        // PrestControl ya tiene la suya del setup (B01, próxima 1, tope 15).
        // El dealer arranca VACÍO: es lo que se pidió.
        SesionActual.EstablecerModo(ModoApp.DealerControl);
        (await _ncfServicio.ObtenerSecuenciaAsync())
            .Should().BeNull("cada estancia arranca sin secuencia configurada");

        // Se le configura una con el MISMO prefijo pero otro rango: pasa,
        // porque la clave única es (modo, prefijo).
        await _ncfServicio.GuardarSecuenciaAsync(new NcfSecuencia
        {
            Prefijo = Prefijo, Largo = Largo, Proxima = 500, FinRango = 600, Activo = true
        });

        var delDealer = await _ncfServicio.ObtenerSecuenciaAsync();
        delDealer!.Proxima.Should().Be(500);

        // Y lo de préstamos quedó intacto
        SesionActual.EstablecerModo(ModoApp.PrestControl);
        var dePrestamos = await _ncfServicio.ObtenerSecuenciaAsync();
        dePrestamos!.Proxima.Should().Be(1, "configurar el dealer no pisa la de préstamos");

        // Consumir uno del dealer no mueve el contador de préstamos
        SesionActual.EstablecerModo(ModoApp.DealerControl);
        using (var conexion = await _factory.AbrirAsync())
        using (var transaccion = await conexion.BeginTransactionAsync())
        {
            var ncf = await _ncfRepo.ReservarSiguienteAsync(
                ModoApp.DealerControl, conexion, transaccion, FechaNegocio.Hoy);
            ncf.Should().Be("B0100000500");
            await transaccion.CommitAsync();
        }

        SesionActual.EstablecerModo(ModoApp.PrestControl);
        (await _ncfServicio.ObtenerSecuenciaAsync())!.Proxima
            .Should().Be(1, "los rangos no se comparten");
    }

    /// <summary>Vencida la autorización, la app no emite aunque queden números.</summary>
    [Fact]
    public async Task VencidaLaAutorizacion_NoSeEmiteAunqueQuedenNumeros()
    {
        using var conexion = await _factory.AbrirAsync();
        using var transaccion = await conexion.BeginTransactionAsync();

        var despuesDelVencimiento = Vencimiento.AddDays(1);
        var reservar = () => _ncfRepo.ReservarSiguienteAsync(SesionActual.Modo, conexion, transaccion, despuesDelVencimiento);

        (await reservar.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*venció*");
    }
}
