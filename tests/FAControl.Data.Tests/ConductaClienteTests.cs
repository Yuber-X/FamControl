using FluentAssertions;
using MySqlConnector;
using FAControl.Data;
using FAControl.Models;
using FAControl.Services;

namespace FAControl.Data.Tests;

/// <summary>
/// Historial de buena conducta (pedido del cliente 2026-08-06): "se quiere un
/// historial de buena conducta para los clientes, si un cliente hizo el préstamo".
///
/// Las cuotas y los pagos se insertan por SQL directo a propósito: lo que se
/// prueba son FECHAS exactas, y pasando por PagoService la fecha del abono la
/// pondría el reloj de la máquina que corre la prueba.
///
/// Requiere el servicio MySQL80 local con credenciales Dev (root/root).
/// </summary>
[Collection(ColeccionSesionData.Nombre)]
public class ConductaClienteTests : IAsyncLifetime
{
    private const string CadenaServidor = "Server=localhost;Port=3306;Uid=root;Pwd=root;";
    private const string Bd = "facontrol_conducta_test";
    private const string Cadena = CadenaServidor + $"Database={Bd};";

    /// <summary>Fecha fija para que las pruebas no cambien de resultado según el día.</summary>
    private static readonly DateOnly Hoy = new(2026, 8, 6);

    private ConexionFactory _factory = null!;
    private ClienteRepository _clientes = null!;
    private long _clienteId;
    private int _recibo;

    public async Task InitializeAsync()
    {
        await RecrearBaseAsync();
        _factory = new ConexionFactory(Cadena);
        _clientes = new ClienteRepository(_factory);

        using var conexion = await _factory.AbrirAsync();
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = """
            INSERT INTO cliente (cedula, nombre, apellido)
            VALUES ('001-0000001-1', 'Ramón', 'Peña');
            SELECT LAST_INSERT_ID();
            """;
        _clienteId = Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    public async Task DisposeAsync()
    {
        using var conexion = new MySqlConnection(CadenaServidor);
        await conexion.OpenAsync();
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"DROP DATABASE IF EXISTS {Bd};";
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task RecrearBaseAsync()
    {
        using var conexion = new MySqlConnection(CadenaServidor);
        await conexion.OpenAsync();
        using (var drop = conexion.CreateCommand())
        {
            drop.CommandText = $"DROP DATABASE IF EXISTS {Bd};";
            await drop.ExecuteNonQueryAsync();
        }
        using (var crear = conexion.CreateCommand())
        {
            crear.CommandText = $"CREATE DATABASE {Bd} CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;";
            await crear.ExecuteNonQueryAsync();
        }
        await conexion.ChangeDatabaseAsync(Bd);

        foreach (var bloque in VerificadorBaseDatos.ObtenerBloquesEjecutables())
        {
            using var cmd = conexion.CreateCommand();
            cmd.CommandText = bloque;
            cmd.CommandTimeout = 120;
            await cmd.ExecuteNonQueryAsync();
        }
    }

    // ---------- Ayudantes de armado ----------

    private async Task<long> CrearPrestamoAsync(string codigo, string estado = "activo")
    {
        using var conexion = await _factory.AbrirAsync();
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = """
            INSERT INTO prestamo (codigo, cliente_id, monto_capital, tasa_interes,
                                  plazo_cuotas, modalidad, metodo_amortizacion,
                                  fecha_inicio, estado)
            VALUES (@codigo, @clienteId, 12000.00, 5.0000, 12, 'mensual', 'cuota_fija',
                    '2026-01-15', @estado);
            SELECT LAST_INSERT_ID();
            """;
        cmd.Parameters.AddWithValue("@codigo", codigo);
        cmd.Parameters.AddWithValue("@clienteId", _clienteId);
        cmd.Parameters.AddWithValue("@estado", estado);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private async Task<long> CrearCuotaAsync(long prestamoId, int numero, DateOnly vence,
        string estado = "pendiente", decimal pagado = 0m)
    {
        using var conexion = await _factory.AbrirAsync();
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = """
            INSERT INTO cuota (prestamo_id, numero_cuota, fecha_vencimiento, capital,
                               interes, monto_total, saldo_despues, monto_pagado, estado)
            VALUES (@prestamoId, @numero, @vence, 1000.00, 600.00, 1600.00, 0.00, @pagado, @estado);
            SELECT LAST_INSERT_ID();
            """;
        cmd.Parameters.AddWithValue("@prestamoId", prestamoId);
        cmd.Parameters.AddWithValue("@numero", numero);
        cmd.Parameters.AddWithValue("@vence", vence.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@estado", estado);
        cmd.Parameters.AddWithValue("@pagado", pagado);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    /// <summary><paramref name="fechaPagoUtc"/> va tal cual a la columna: la BD guarda UTC.</summary>
    private async Task RegistrarPagoAsync(long cuotaId, DateTime fechaPagoUtc, decimal monto = 1600m,
        bool anulado = false)
    {
        using var conexion = await _factory.AbrirAsync();
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = """
            INSERT INTO pago (cuota_id, numero_recibo, fecha_pago, monto_pagado, deleted_at)
            VALUES (@cuotaId, @recibo, @fecha, @monto, @borrado);
            """;
        cmd.Parameters.AddWithValue("@cuotaId", cuotaId);
        cmd.Parameters.AddWithValue("@recibo", $"R-{++_recibo:000000}");
        cmd.Parameters.AddWithValue("@fecha", fechaPagoUtc);
        cmd.Parameters.AddWithValue("@monto", monto);
        cmd.Parameters.AddWithValue("@borrado", anulado ? DateTime.UtcNow : (object)DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Cuota saldada el día que se indica, a las 10 de la mañana hora RD.</summary>
    private async Task CuotaSaldadaAsync(long prestamoId, int numero, DateOnly vence, DateOnly seSaldo)
    {
        var cuotaId = await CrearCuotaAsync(prestamoId, numero, vence, "pagada", 1600m);
        await RegistrarPagoAsync(cuotaId, seSaldo.ToDateTime(new TimeOnly(10, 0)).AddHours(4));
    }

    private Task<ClienteConducta> ConductaAsync() => _clientes.ObtenerConductaAsync(_clienteId, Hoy);

    // ---------- Pruebas ----------

    [Fact]
    public async Task ClienteNuevo_NoTieneNadaQueJuzgar()
    {
        var c = await ConductaAsync();

        c.Calificacion.Should().Be(ConductaCliente.SinHistorial);
        c.EsClienteConocido.Should().BeFalse();
        c.PrestamosTotales.Should().Be(0);
        c.CuotasSaldadas.Should().Be(0);
        c.PorcentajeATiempo.Should().Be(0);
        c.PrimerPrestamo.Should().BeNull();
        c.UltimoPago.Should().BeNull();
    }

    [Fact]
    public async Task PagoTodoEnFecha_EsExcelente()
    {
        var p = await CrearPrestamoAsync("P-0001", "pagado");
        for (var i = 1; i <= 6; i++)
        {
            var vence = new DateOnly(2026, i, 15);
            await CuotaSaldadaAsync(p, i, vence, vence);
        }

        var c = await ConductaAsync();

        c.CuotasSaldadas.Should().Be(6);
        c.CuotasATiempo.Should().Be(6);
        c.CuotasTarde.Should().Be(0);
        c.PorcentajeATiempo.Should().Be(100);
        c.PeorAtrasoDias.Should().Be(0);
        c.PrestamosSaldados.Should().Be(1);
        c.Calificacion.Should().Be(ConductaCliente.Excelente);
    }

    /// <summary>
    /// EL CASO QUE SE ROMPE SOLO: los vencimientos son fecha local del negocio y
    /// los pagos se guardan en UTC. Un cobro a las 9 de la noche del día de
    /// vencimiento queda grabado como la 1 de la madrugada del día siguiente. Si
    /// se comparan crudos, ese cliente aparece atrasado sin serlo — y a fin de
    /// mes casi todos los cobros de la tarde lo estarían.
    /// </summary>
    [Fact]
    public async Task PagoDeNoche_NoCuentaComoAtraso()
    {
        var p = await CrearPrestamoAsync("P-0001");
        var vence = new DateOnly(2026, 3, 15);
        var cuotaId = await CrearCuotaAsync(p, 1, vence, "pagada", 1600m);
        // 9:00 PM del 15 en RD = 01:00 UTC del 16
        await RegistrarPagoAsync(cuotaId, new DateTime(2026, 3, 16, 1, 0, 0, DateTimeKind.Utc));

        var c = await ConductaAsync();

        c.CuotasATiempo.Should().Be(1, "pagó el mismo día, aunque de noche");
        c.CuotasTarde.Should().Be(0);
        c.PeorAtrasoDias.Should().Be(0);
    }

    [Fact]
    public async Task PagosTarde_PromedianYGuardanElPeor()
    {
        var p = await CrearPrestamoAsync("P-0001", "pagado");
        // 2 en fecha, 2 tarde (4 y 10 días) → promedio 7, peor 10
        await CuotaSaldadaAsync(p, 1, new DateOnly(2026, 1, 15), new DateOnly(2026, 1, 15));
        await CuotaSaldadaAsync(p, 2, new DateOnly(2026, 2, 15), new DateOnly(2026, 2, 15));
        await CuotaSaldadaAsync(p, 3, new DateOnly(2026, 3, 15), new DateOnly(2026, 3, 19));
        await CuotaSaldadaAsync(p, 4, new DateOnly(2026, 4, 15), new DateOnly(2026, 4, 25));

        var c = await ConductaAsync();

        c.CuotasSaldadas.Should().Be(4);
        c.CuotasATiempo.Should().Be(2);
        c.CuotasTarde.Should().Be(2);
        c.DiasPromedioAtraso.Should().Be(7, "solo promedia las que se pagaron tarde");
        c.PeorAtrasoDias.Should().Be(10);
        c.PorcentajeATiempo.Should().Be(50);
        c.Calificacion.Should().Be(ConductaCliente.Regular);
    }

    /// <summary>
    /// Lo de hoy manda sobre el promedio histórico: no sirve de nada decir "buen
    /// pagador" de alguien que en este momento está debiendo.
    /// </summary>
    [Fact]
    public async Task ConCuotaVencidaHoy_EsRiesgosoAunqueAntesPagaraBien()
    {
        var p = await CrearPrestamoAsync("P-0001");
        for (var i = 1; i <= 5; i++)
        {
            var vence = new DateOnly(2026, i, 15);
            await CuotaSaldadaAsync(p, i, vence, vence);
        }
        // Cuota de junio sin pagar: al 06/08/2026 está vencida
        await CrearCuotaAsync(p, 6, new DateOnly(2026, 6, 15), "vencida");

        var c = await ConductaAsync();

        c.PorcentajeATiempo.Should().Be(100, "lo que pagó, lo pagó bien");
        c.CuotasVencidasHoy.Should().Be(1);
        c.Calificacion.Should().Be(ConductaCliente.Riesgosa);
    }

    /// <summary>
    /// Una cuota pagada en tres abonos se juzga por el ÚLTIMO: la deuda quedó
    /// saldada cuando entró el último peso, no cuando entró el primero.
    /// </summary>
    [Fact]
    public async Task CuotaPagadaEnPartes_SeJuzgaPorElUltimoAbono()
    {
        var p = await CrearPrestamoAsync("P-0001");
        var cuotaId = await CrearCuotaAsync(p, 1, new DateOnly(2026, 3, 15), "pagada", 1600m);
        await RegistrarPagoAsync(cuotaId, new DateTime(2026, 3, 15, 14, 0, 0, DateTimeKind.Utc), 500m);
        await RegistrarPagoAsync(cuotaId, new DateTime(2026, 3, 18, 14, 0, 0, DateTimeKind.Utc), 500m);
        await RegistrarPagoAsync(cuotaId, new DateTime(2026, 3, 20, 14, 0, 0, DateTimeKind.Utc), 600m);

        var c = await ConductaAsync();

        c.CuotasSaldadas.Should().Be(1, "los tres abonos son UNA cuota, no tres");
        c.CuotasTarde.Should().Be(1);
        c.PeorAtrasoDias.Should().Be(5, "el 20 de marzo, no el 15");
    }

    [Fact]
    public async Task PagoAnulado_NoCuentaParaLaConducta()
    {
        var p = await CrearPrestamoAsync("P-0001");
        var cuotaId = await CrearCuotaAsync(p, 1, new DateOnly(2026, 3, 15), "pagada", 1600m);
        // El abono bueno, en fecha; y uno anulado muy posterior que no debe pesar
        await RegistrarPagoAsync(cuotaId, new DateTime(2026, 3, 15, 14, 0, 0, DateTimeKind.Utc));
        await RegistrarPagoAsync(cuotaId, new DateTime(2026, 7, 30, 14, 0, 0, DateTimeKind.Utc), anulado: true);

        var c = await ConductaAsync();

        c.CuotasATiempo.Should().Be(1);
        c.PeorAtrasoDias.Should().Be(0, "el pago anulado no existe para la conducta");
    }

    [Fact]
    public async Task CuentaLosContratosPorEstadoYRecuerdaDesdeCuandoEsCliente()
    {
        await CrearPrestamoAsync("P-0001", "pagado");
        await CrearPrestamoAsync("P-0002", "activo");
        await CrearPrestamoAsync("P-0003", "cancelado");

        var c = await ConductaAsync();

        c.PrestamosTotales.Should().Be(3);
        c.PrestamosSaldados.Should().Be(1);
        c.PrestamosActivos.Should().Be(1);
        c.PrestamosCancelados.Should().Be(1);
        c.EsClienteConocido.Should().BeTrue();
        c.PrimerPrestamo.Should().Be(new DateOnly(2026, 1, 15));
        c.Calificacion.Should().Be(ConductaCliente.SinHistorial,
            "tiene contratos pero no terminó de pagar ninguna cuota");
    }

    [Fact]
    public async Task UnSoloAtrasoChico_SigueSiendoBuenPagador()
    {
        var p = await CrearPrestamoAsync("P-0001", "pagado");
        for (var i = 1; i <= 9; i++)
        {
            var vence = new DateOnly(2026, 1, 15).AddDays(i * 30);
            await CuotaSaldadaAsync(p, i, vence, vence);
        }
        // La décima, 3 días tarde → 90% a tiempo, promedio 3 días
        var ultima = new DateOnly(2026, 1, 15).AddDays(300);
        await CuotaSaldadaAsync(p, 10, ultima, ultima.AddDays(3));

        var c = await ConductaAsync();

        c.PorcentajeATiempo.Should().Be(90);
        c.DiasPromedioAtraso.Should().Be(3);
        c.Calificacion.Should().Be(ConductaCliente.Buena);
    }
}
