using FluentAssertions;
using MySqlConnector;
using FAControl.Common;
using FAControl.Data;
using FAControl.Models;
using FAControl.Services;

namespace FAControl.Data.Tests;

/// <summary>
/// Integración contra MySQL real (BD facontrol_test, se recrea en cada corrida).
/// Verifica el flujo completo: crear préstamo (transacción atómica + contador) →
/// abono parcial → adelanto → liquidación anticipada → préstamo pagado.
/// Requiere el servicio MySQL80 local con credenciales Dev (root/root).
/// </summary>
public class FlujoPrestamoPagoTests : IAsyncLifetime
{
    private const string CadenaServidor = "Server=localhost;Port=3306;Uid=root;Pwd=root;";
    private const string CadenaTest = CadenaServidor + "Database=facontrol_test;";

    private ConexionFactory _factory = null!;
    private PrestamoService _prestamos = null!;
    private PagoService _pagos = null!;
    private ClienteService _clientes = null!;
    private long _clienteId;

    public async Task InitializeAsync()
    {
        await CrearBaseDeDatosDePruebaAsync();

        _factory = new ConexionFactory(CadenaTest);
        var prestamoRepo = new PrestamoRepository(_factory);
        var clienteRepo = new ClienteRepository(_factory);
        var pagoRepo = new PagoRepository(_factory);
        var contadorRepo = new ContadorRepository();
        var auditoria = new AuditoriaService(new AuditoriaRepository(_factory),
            new SesionRepository(_factory), new UsuarioRepository(_factory));

        _prestamos = new PrestamoService(_factory, prestamoRepo, contadorRepo,
            new AmortizacionService(), auditoria, new VehiculoRepository(_factory),
            new NcfRepository(_factory));
        _pagos = new PagoService(_factory, prestamoRepo, pagoRepo, clienteRepo,
            contadorRepo, auditoria, new FAControl.Common.AjustesLocales());
        _clientes = new ClienteService(clienteRepo, auditoria);

        // Usuario + cliente de prueba, y sesión activa (la auditoría la exige)
        using var conexion = await _factory.AbrirAsync();
        using (var cmd = conexion.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO usuario (username, password_hash, nombre)
                VALUES ('test', 'hash-de-prueba', 'Usuario Test');
                SELECT LAST_INSERT_ID();
                """;
            var usuarioId = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            // Admin con todos los permisos: este test cubre el flujo de negocio,
            // no la autorización (esa tiene sus propios tests).
            SesionActual.Iniciar(usuarioId, "test", "Usuario Test", Roles.Admin,
                Permisos.Todos, DateTime.UtcNow, 1);
        }
        using (var cmd = conexion.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO cliente (cedula, nombre, apellido)
                VALUES ('001-0000001-1', 'María', 'Prueba');
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
    /// Recrea facontrol_test con el MISMO camino que usa la app en la máquina
    /// del cliente: VerificadorBaseDatos.ObtenerBloquesEjecutables(). Ejecutar
    /// el .sql de un tirón no serviría — el protocolo de MySQL rechaza DELIMITER
    /// y los triggers no se crearían.
    /// </summary>
    private static async Task CrearBaseDeDatosDePruebaAsync()
    {
        using var conexion = new MySqlConnection(CadenaServidor);
        await conexion.OpenAsync();
        using (var drop = conexion.CreateCommand())
        {
            drop.CommandText = "DROP DATABASE IF EXISTS facontrol_test;";
            await drop.ExecuteNonQueryAsync();
        }
        using (var crear = conexion.CreateCommand())
        {
            crear.CommandText =
                "CREATE DATABASE facontrol_test CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;";
            await crear.ExecuteNonQueryAsync();
        }
        await conexion.ChangeDatabaseAsync("facontrol_test");

        foreach (var bloque in VerificadorBaseDatos.ObtenerBloquesEjecutables())
        {
            using var cmd = conexion.CreateCommand();
            cmd.CommandText = bloque;
            cmd.CommandTimeout = 120;
            await cmd.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task FlujoCompleto_CrearPrestamo_Abonar_Adelantar_Liquidar()
    {
        // ========== 1. Crear préstamo: 12,000 al 5% mensual × 12 cuotas ==========
        // Interés simple dominicano: cuota = 1,000 capital + 600 interés = 1,600
        var (prestamoId, codigo) = await _prestamos.CrearAsync(new NuevoPrestamo(
            _clienteId, 12_000m, 5m, 12, Modalidad.Mensual, MetodoAmortizacion.CuotaFija,
            FechaNegocio.Hoy.AddMonths(-1), // 1ra cuota ya vencida (para probar semáforo/liquidación)
            Garantia: null, Notas: "Integración"));

        codigo.Should().Be("P-0001"); // contador atómico desde cero

        var cuotas = await _prestamos.ObtenerCuotasAsync(prestamoId);
        cuotas.Should().HaveCount(12);
        cuotas.Sum(c => c.Capital).Should().Be(12_000m);
        cuotas.Sum(c => c.MontoTotal).Should().Be(19_200m);
        cuotas[0].MontoTotal.Should().Be(1_600m);

        // ========== 2. Abono parcial de 400 (todo a interés) ==========
        var abono = await _pagos.RegistrarPagoAsync(new SolicitudPago(
            prestamoId, 400m, MetodoPago.Efectivo, "Abono parcial"));

        abono.Pagos.Should().HaveCount(1);
        abono.Pagos[0].NumeroRecibo.Should().Be("R-000001");
        abono.Pagos[0].MontoInteres.Should().Be(400m);
        abono.Pagos[0].MontoCapital.Should().Be(0m);
        abono.PrestamoQuedoPagado.Should().BeFalse();
        abono.Recibo.SaldoRestantePrestamo.Should().Be(18_800m);

        // ========== 3. Adelanto de 2,800: completa la cuota 1 y abona a la 2 ==========
        var adelanto = await _pagos.RegistrarPagoAsync(new SolicitudPago(
            prestamoId, 2_800m, MetodoPago.Transferencia, null));

        adelanto.Pagos.Should().HaveCount(2);
        adelanto.Pagos[0].MontoPagado.Should().Be(1_200m);  // resto de la cuota 1
        adelanto.Pagos[0].MontoInteres.Should().Be(200m);   // interés que faltaba
        adelanto.Pagos[0].MontoCapital.Should().Be(1_000m);
        adelanto.Pagos[1].MontoPagado.Should().Be(1_600m);  // cuota 2 completa
        adelanto.Pagos[1].NumeroRecibo.Should().Be("R-000003");

        var trasAdelanto = await _prestamos.ObtenerCuotasAsync(prestamoId);
        trasAdelanto.Count(c => c.Estado == EstadoCuota.Pagada).Should().Be(2);

        // ========== 4. Liquidación anticipada: 10 cuotas futuras solo capital ==========
        var liquidacion = await _pagos.RegistrarPagoAsync(new SolicitudPago(
            prestamoId, 0m, MetodoPago.Efectivo, null, EsLiquidacion: true));

        // 10 cuotas × 1,000 de capital (el interés futuro se exonera)
        liquidacion.Recibo.TotalPagado.Should().Be(10_000m);
        liquidacion.Recibo.InteresExonerado.Should().Be(6_000m);
        liquidacion.PrestamoQuedoPagado.Should().BeTrue();
        liquidacion.Recibo.SaldoRestantePrestamo.Should().Be(0m);

        var prestamoFinal = await _prestamos.ObtenerPorIdAsync(prestamoId);
        prestamoFinal!.Estado.Should().Be(EstadoPrestamo.Pagado);

        var cuotasFinales = await _prestamos.ObtenerCuotasAsync(prestamoId);
        cuotasFinales.Should().OnlyContain(c => c.Estado == EstadoCuota.Pagada);

        // El resumen agregado refleja todo lo cobrado
        var resumenes = await _prestamos.ObtenerResumenesAsync();
        var resumen = resumenes.Single(r => r.Id == prestamoId);
        resumen.CuotasPagadas.Should().Be(12);
        resumen.ProximoVencimiento.Should().BeNull();
    }

    [Fact]
    public async Task CrudCliente_CrearActualizarYEliminarConProteccion()
    {
        // Crear: la cédula se normaliza a 000-0000000-0
        var id = await _clientes.CrearAsync(new ClienteDatos(
            "40212345678", "Pedro", "Integración", "809-555-0000", null, null, null));
        var creado = await _clientes.ObtenerPorIdAsync(id);
        creado!.Cedula.Should().Be("402-1234567-8");

        // Cédula duplicada (aunque venga sin guiones) se rechaza
        var duplicar = () => _clientes.CrearAsync(new ClienteDatos(
            "402-1234567-8", "Otro", "Cliente", null, null, null, null));
        await duplicar.Should().ThrowAsync<ArgumentException>().WithMessage("*Ya existe*");

        // Actualizar
        await _clientes.ActualizarAsync(id, new ClienteDatos(
            "402-1234567-8", "Pedro Luis", "Integración", null, "Calle 1 #2", null, null));
        (await _clientes.ObtenerPorIdAsync(id))!.Nombre.Should().Be("Pedro Luis");

        // Con préstamo activo NO se puede eliminar
        var (prestamoId, _) = await _prestamos.CrearAsync(new NuevoPrestamo(
            id, 1_000m, 5m, 2, Modalidad.Mensual, MetodoAmortizacion.CuotaFija,
            FechaNegocio.Hoy.AddMonths(1), null, null));
        var eliminar = () => _clientes.EliminarAsync(id);
        await eliminar.Should().ThrowAsync<InvalidOperationException>().WithMessage("*activo*");

        // Cancelado el préstamo, el soft delete procede y desaparece de las listas
        await _prestamos.CancelarAsync(prestamoId, "test");
        await _clientes.EliminarAsync(id);
        (await _clientes.ObtenerPorIdAsync(id)).Should().BeNull();
        (await _clientes.ObtenerResumenesAsync()).Should().NotContain(c => c.Id == id);
    }

    [Fact]
    public async Task MetricasDeCliente_ReflejanPrestamosYCobros()
    {
        // Préstamo 12,000 al 5% × 12 con la 1ra cuota vencida, más un abono de 1,600
        var (prestamoId, _) = await _prestamos.CrearAsync(new NuevoPrestamo(
            _clienteId, 12_000m, 5m, 12, Modalidad.Mensual, MetodoAmortizacion.CuotaFija,
            FechaNegocio.Hoy.AddMonths(-1), null, null));
        await _pagos.RegistrarPagoAsync(new SolicitudPago(
            prestamoId, 1_600m, MetodoPago.Efectivo, null));

        var metricas = await _clientes.ObtenerMetricasAsync(_clienteId);
        metricas.TotalPrestado.Should().Be(12_000m);
        metricas.TotalCobrado.Should().Be(1_600m);
        metricas.SaldoPendiente.Should().Be(17_600m); // 19,200 − 1,600
        metricas.PrestamosActivos.Should().Be(1);
        metricas.CuotasVencidas.Should().Be(0);       // la vencida quedó pagada con el abono
    }

    [Fact]
    public async Task CancelarPrestamo_MarcaCuotasImpagasComoCanceladas()
    {
        var (prestamoId, _) = await _prestamos.CrearAsync(new NuevoPrestamo(
            _clienteId, 5_000m, 10m, 5, Modalidad.Semanal, MetodoAmortizacion.CuotaFija,
            FechaNegocio.Hoy.AddDays(7), null, null));

        await _prestamos.CancelarAsync(prestamoId, "Prueba de cancelación");

        var prestamo = await _prestamos.ObtenerPorIdAsync(prestamoId);
        prestamo!.Estado.Should().Be(EstadoPrestamo.Cancelado);

        var cuotas = await _prestamos.ObtenerCuotasAsync(prestamoId);
        cuotas.Should().OnlyContain(c => c.Estado == EstadoCuota.Cancelada);

        // Un préstamo cancelado no se puede cobrar
        var cobrar = () => _pagos.RegistrarPagoAsync(new SolicitudPago(
            prestamoId, 100m, MetodoPago.Efectivo, null));
        await cobrar.Should().ThrowAsync<InvalidOperationException>();
    }
}
