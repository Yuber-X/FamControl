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
            contador, auditoria);

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
        var recibo = await _plazos.CobrarPlazoAsync(primero.Id, 50_000m, MetodoPago.Transferencia);
        recibo.Should().StartWith("RV-");

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

    [Fact]
    public async Task NoSePuedeCobrar_MasDeLoQueFaltaDelPlazo()
    {
        var vehiculoId = await CrearVehiculoAsync("V-9002", 300_000m);
        var (ventaId, _) = await _ventas.RegistrarAsync(new VentaVehiculoDatos(
            vehiculoId, _clienteId, 300_000m, MetodoPago.Efectivo, null,
            TipoVenta: TipoVenta.Plazos,
            Plan: new PlanPlazos(0m, 2, FechaNegocio.Hoy.AddDays(30))));

        var estado = await _plazos.ObtenerEstadoAsync(ventaId);
        var plazo = estado.Plazos[0];   // 150,000

        var cobroExcesivo = () => _plazos.CobrarPlazoAsync(plazo.Id, 200_000m, MetodoPago.Efectivo);
        await cobroExcesivo.Should().ThrowAsync<InvalidOperationException>();

        // El rollback dejó el plazo intacto y NO consumió el número de recibo
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
}
