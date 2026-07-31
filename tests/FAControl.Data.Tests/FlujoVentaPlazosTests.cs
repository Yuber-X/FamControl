using FluentAssertions;
using MySqlConnector;
using FAControl.Common;
using FAControl.Data;
using FAControl.Models;
using FAControl.Services;

namespace FAControl.Data.Tests;

/// <summary>
/// Integración del financiamiento del dealer (016) contra MySQL real: venta por
/// plazos con inicial → cobros → venta saldada, y separación que reserva el
/// vehículo. Comparte la BD facontrol_test con el resto de la suite.
/// Requiere el servicio MySQL80 local con credenciales Dev (root/root).
/// </summary>
[Collection(ColeccionSesionData.Nombre)]   // SesionActual es global: ver ColeccionSesionData
public class FlujoVentaPlazosTests : IAsyncLifetime
{
    private const string CadenaServidor = "Server=localhost;Port=3306;Uid=root;Pwd=root;";
    private const string CadenaTest = CadenaServidor + "Database=facontrol_plazos_test;";

    private ConexionFactory _factory = null!;
    private VentaVehiculoService _ventas = null!;
    private VentaPlazoService _plazos = null!;
    private VehiculoRepository _vehiculos = null!;
    private long _clienteId;

    public async Task InitializeAsync()
    {
        await CrearBaseDeDatosDePruebaAsync();

        _factory = new ConexionFactory(CadenaTest);
        _vehiculos = new VehiculoRepository(_factory);
        var contador = new ContadorRepository();
        var auditoria = new AuditoriaService(new AuditoriaRepository(_factory),
            new SesionRepository(_factory), new UsuarioRepository(_factory));
        var plazoRepo = new VentaPlazoRepository(_factory);

        _ventas = new VentaVehiculoService(new VentaVehiculoRepository(_factory), _vehiculos,
            new ClienteRepository(_factory), contador, _factory, auditoria, plazoRepo);
        _plazos = new VentaPlazoService(_factory, plazoRepo, new VentaVehiculoRepository(_factory),
            contador, auditoria, new VehiculoRepository(_factory));

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
                INSERT INTO cliente (cedula, nombre, apellido, ambito)
                VALUES ('001-0000002-2', 'Jhonny', 'Comprador', 'dealercontrol');
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
            drop.CommandText = "DROP DATABASE IF EXISTS facontrol_plazos_test;";
            await drop.ExecuteNonQueryAsync();
        }
        using (var crear = conexion.CreateCommand())
        {
            crear.CommandText =
                "CREATE DATABASE facontrol_plazos_test CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;";
            await crear.ExecuteNonQueryAsync();
        }
        await conexion.ChangeDatabaseAsync("facontrol_plazos_test");

        foreach (var bloque in VerificadorBaseDatos.ObtenerBloquesEjecutables())
        {
            using var cmd = conexion.CreateCommand();
            cmd.CommandText = bloque;
            cmd.CommandTimeout = 120;
            await cmd.ExecuteNonQueryAsync();
        }
    }

    /// <summary>Crea un vehículo disponible directo en BD (el alta tiene su propio test).</summary>
    private async Task<long> CrearVehiculoAsync(string codigo, decimal precio)
    {
        using var conexion = await _factory.AbrirAsync();
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = """
            INSERT INTO vehiculo (codigo, marca, modelo, anio, precio_venta, costo_adquisicion)
            VALUES (@codigo, 'Hyundai', 'Verna', 2022, @precio, 500000);
            SELECT LAST_INSERT_ID();
            """;
        cmd.Parameters.AddWithValue("@codigo", codigo);
        cmd.Parameters.AddWithValue("@precio", precio);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    [Fact]
    public async Task VentaPorPlazos_CobraHastaSaldarla()
    {
        var vehiculoId = await CrearVehiculoAsync("V-9001", 700_000m);

        // 700,000 con 100,000 de inicial en 3 plazos → 200,000 cada uno
        var (ventaId, codigo) = await _ventas.RegistrarAsync(new VentaVehiculoDatos(
            vehiculoId, _clienteId, 700_000m, MetodoPago.Efectivo, "Garantía 1 año",
            TipoVenta: TipoVenta.Plazos,
            Plan: new PlanPlazos(100_000m, 3, FechaNegocio.Hoy.AddDays(30))));

        codigo.Should().StartWith("VC-");
        (await _vehiculos.ObtenerPorIdAsync(vehiculoId))!.Estado.Should().Be(EstadoVehiculo.Vendido);

        var estado = await _plazos.ObtenerEstadoAsync(ventaId);
        estado.Tipo.Should().Be(TipoVenta.Plazos);
        estado.TotalAPlazos.Should().Be(600_000m);
        estado.Pendiente.Should().Be(600_000m);
        estado.CantidadPlazos.Should().Be(3);
        estado.Plazos.Should().OnlyContain(p => p.Monto == 200_000m);

        // Abono parcial al primer plazo: queda pendiente, no pagado
        var primero = estado.Plazos[0];
        var abono = await _plazos.CobrarPlazoAsync(primero.Id, 50_000m, MetodoPago.Transferencia);
        abono.Recibos.Should().ContainSingle().Which.Should().StartWith("RV-");
        abono.TocoVariosPlazos.Should().BeFalse("50,000 entra entero en el primer plazo");

        estado = await _plazos.ObtenerEstadoAsync(ventaId);
        estado.Pagado.Should().Be(50_000m);
        estado.Pendiente.Should().Be(550_000m);
        estado.PlazosPagados.Should().Be(0);
        estado.Plazos[0].SaldoPendiente.Should().Be(150_000m);

        // Completar el primero y cobrar los otros dos: la venta queda saldada
        await _plazos.CobrarPlazoAsync(estado.Plazos[0].Id, 150_000m, MetodoPago.Efectivo);
        await _plazos.CobrarPlazoAsync(estado.Plazos[1].Id, 200_000m, MetodoPago.Efectivo);
        await _plazos.CobrarPlazoAsync(estado.Plazos[2].Id, 200_000m, MetodoPago.Cheque);

        estado = await _plazos.ObtenerEstadoAsync(ventaId);
        estado.Pagado.Should().Be(600_000m);
        estado.Pendiente.Should().Be(0m);
        estado.PlazosPagados.Should().Be(3);
        estado.EstaSaldada.Should().BeTrue();
        estado.RecibidoTotal.Should().Be(700_000m);   // inicial + plazos = precio

        // Cada abono dejó su recibo con número único
        var abonos = await _plazos.ObtenerPagosAsync(ventaId);
        abonos.Should().HaveCount(4);
        abonos.Select(a => a.NumeroRecibo).Should().OnlyHaveUniqueItems();
    }

    /// <summary>
    /// Lo que pidió Yuber (2026-07-31): si paga de más, el excedente baja al
    /// plazo siguiente y reduce lo que le toca pagar la próxima vez.
    /// </summary>
    [Fact]
    public async Task PagarDeMas_BajaElExcedenteAlPlazoSiguiente()
    {
        var vehiculoId = await CrearVehiculoAsync("V-9002", 300_000m);
        var (ventaId, _) = await _ventas.RegistrarAsync(new VentaVehiculoDatos(
            vehiculoId, _clienteId, 300_000m, MetodoPago.Efectivo, null,
            TipoVenta: TipoVenta.Plazos,
            Plan: new PlanPlazos(0m, 2, FechaNegocio.Hoy.AddDays(30))));

        var estado = await _plazos.ObtenerEstadoAsync(ventaId);
        var plazo = estado.Plazos[0];   // 150,000 de 2 plazos

        // Paga 200,000: salda el primero (150,000) y deja 50,000 en el segundo
        var abono = await _plazos.CobrarPlazoAsync(plazo.Id, 200_000m, MetodoPago.Efectivo);

        abono.Aplicado.Should().Be(200_000m);
        abono.PlazosSaldados.Should().Be(1);
        abono.TocoVariosPlazos.Should().BeTrue();
        abono.Recibos.Should().HaveCount(2, "un recibo por plazo tocado");
        abono.Recibos.Should().OnlyHaveUniqueItems();
        abono.SaldoRestante.Should().Be(100_000m);

        estado = await _plazos.ObtenerEstadoAsync(ventaId);
        estado.Plazos[0].Estado.Should().Be(EstadoPlazo.Pagado);
        estado.Plazos[1].SaldoPendiente.Should().Be(100_000m,
            "de 150,000 ya tiene 50,000 abonados: la próxima paga 100,000");
    }

    /// <summary>Pagar más que TODA la deuda sigue estando prohibido.</summary>
    [Fact]
    public async Task NoSePuedeCobrar_MasDeLoQueFaltaDeLaVenta()
    {
        var vehiculoId = await CrearVehiculoAsync("V-9003", 300_000m);
        var (ventaId, _) = await _ventas.RegistrarAsync(new VentaVehiculoDatos(
            vehiculoId, _clienteId, 300_000m, MetodoPago.Efectivo, null,
            TipoVenta: TipoVenta.Plazos,
            Plan: new PlanPlazos(0m, 2, FechaNegocio.Hoy.AddDays(30))));

        var estado = await _plazos.ObtenerEstadoAsync(ventaId);

        var cobroExcesivo = () => _plazos.CobrarPlazoAsync(
            estado.Plazos[0].Id, 400_000m, MetodoPago.Efectivo);
        await cobroExcesivo.Should().ThrowAsync<InvalidOperationException>();

        // El rollback dejó todo intacto y NO consumió números de recibo
        (await _plazos.ObtenerEstadoAsync(ventaId)).Pagado.Should().Be(0m);
        (await _plazos.ObtenerPagosAsync(ventaId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Separacion_ReservaElVehiculoConFechaLimite()
    {
        var vehiculoId = await CrearVehiculoAsync("V-9003", 800_000m);

        var (ventaId, _) = await _ventas.RegistrarAsync(new VentaVehiculoDatos(
            vehiculoId, _clienteId, 800_000m, MetodoPago.Efectivo, null,
            TipoVenta: TipoVenta.Separacion,
            DiasSeparacion: 15,
            AdelantoSeparacion: 50_000m));

        // Separado ≠ vendido: el vehículo queda RESERVADO hasta completar el pago
        (await _vehiculos.ObtenerPorIdAsync(vehiculoId))!.Estado.Should().Be(EstadoVehiculo.Reservado);

        var estado = await _plazos.ObtenerEstadoAsync(ventaId);
        estado.Tipo.Should().Be(TipoVenta.Separacion);
        estado.Inicial.Should().Be(50_000m);
        estado.FechaLimite.Should().Be(FechaNegocio.Hoy.AddDays(15));
        estado.SeparacionVencida(FechaNegocio.Hoy).Should().BeFalse();
        estado.SeparacionVencida(FechaNegocio.Hoy.AddDays(16)).Should().BeTrue();
    }

    /// <summary>
    /// Cancelar la venta porque el cliente devolvió el vehículo (028). Lo que
    /// importa: la venta NO se borra, lo cobrado se reparte con el porcentaje que
    /// digitó el dueño, los plazos pagados quedan intactos y el vehículo vuelve
    /// al inventario listo para venderse de nuevo.
    /// </summary>
    [Fact]
    public async Task CancelarVenta_RepartraLoCobrado_YDevuelveElVehiculoAlInventario()
    {
        var vehiculoId = await CrearVehiculoAsync("V-9004", 400_000m);
        var (ventaId, _) = await _ventas.RegistrarAsync(new VentaVehiculoDatos(
            vehiculoId, _clienteId, 400_000m, MetodoPago.Efectivo, null,
            TipoVenta: TipoVenta.Plazos,
            Plan: new PlanPlazos(100_000m, 3, FechaNegocio.Hoy.AddDays(30))));

        // Inicial 100,000 + un plazo de 100,000 = 200,000 cobrados
        var estado = await _plazos.ObtenerEstadoAsync(ventaId);
        await _plazos.CobrarPlazoAsync(estado.Plazos[0].Id, 100_000m, MetodoPago.Efectivo);

        var resultado = await _plazos.CancelarVentaAsync(new CancelacionVenta(
            ventaId, "El cliente devolvió el vehículo", RetencionPorcentaje: 25m));

        // 25% de 200,000 se queda el negocio, el resto se le devuelve
        resultado.Cobrado.Should().Be(200_000m);
        resultado.Retenido.Should().Be(50_000m);
        resultado.Devuelto.Should().Be(150_000m);

        // El vehículo vuelve a estar a la venta
        (await _vehiculos.ObtenerPorIdAsync(vehiculoId))!.Estado
            .Should().Be(EstadoVehiculo.Disponible);

        // Los plazos que se debían quedan cancelados; el pagado NO se toca
        estado = await _plazos.ObtenerEstadoAsync(ventaId);
        estado.Plazos[0].Estado.Should().Be(EstadoPlazo.Pagado, "ya se había cobrado");
        estado.Plazos.Skip(1).Should().OnlyContain(p => p.Estado == EstadoPlazo.Cancelado);

        // Y los recibos siguen ahí: lo cobrado no se borra, se reparte
        (await _plazos.ObtenerPagosAsync(ventaId)).Should().ContainSingle();
    }

    /// <summary>No se cancela dos veces: la segunda tiene que fallar.</summary>
    [Fact]
    public async Task NoSePuedeCancelarDosVeces()
    {
        var vehiculoId = await CrearVehiculoAsync("V-9005", 200_000m);
        var (ventaId, _) = await _ventas.RegistrarAsync(new VentaVehiculoDatos(
            vehiculoId, _clienteId, 200_000m, MetodoPago.Efectivo, null,
            TipoVenta: TipoVenta.Plazos,
            Plan: new PlanPlazos(0m, 2, FechaNegocio.Hoy.AddDays(30))));

        await _plazos.CancelarVentaAsync(new CancelacionVenta(ventaId, "Devolución", 20m));

        var segunda = () => _plazos.CancelarVentaAsync(new CancelacionVenta(ventaId, "Otra vez", 20m));
        await segunda.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>El motivo es obligatorio: queda en el historial.</summary>
    [Fact]
    public async Task CancelarSinMotivo_SeRechaza()
    {
        var vehiculoId = await CrearVehiculoAsync("V-9006", 200_000m);
        var (ventaId, _) = await _ventas.RegistrarAsync(new VentaVehiculoDatos(
            vehiculoId, _clienteId, 200_000m, MetodoPago.Efectivo, null,
            TipoVenta: TipoVenta.Plazos,
            Plan: new PlanPlazos(0m, 2, FechaNegocio.Hoy.AddDays(30))));

        var sinMotivo = () => _plazos.CancelarVentaAsync(new CancelacionVenta(ventaId, "  ", 20m));
        await sinMotivo.Should().ThrowAsync<ArgumentException>();
    }
}
