using FluentAssertions;
using MySqlConnector;
using FAControl.Common;
using FAControl.Data;

namespace FAControl.Data.Tests;

/// <summary>
/// La exportacion a Excel saca lo del MODO ACTIVO (2026-08-01).
///
/// Antes exportaba siempre las tablas de prestamos: dentro del dealer o del
/// punto de venta se bajaba un Excel con cuotas y pagos que no tenian nada que
/// ver con lo que el usuario estaba mirando, y faltaba todo lo suyo.
///
/// Requiere MySQL local (root/root). Recrea su base en cada corrida.
/// </summary>
[Collection(ColeccionSesionData.Nombre)]   // SesionActual es global
public class ExportacionPorModoTests : IAsyncLifetime
{
    private const string CadenaServidor = "Server=localhost;Port=3306;Uid=root;Pwd=root;";
    private const string Bd = "facontrol_export_modo_test";
    private const string Cadena = CadenaServidor + $"Database={Bd};";

    private ExportacionRepository _export = null!;

    public async Task InitializeAsync()
    {
        await BorrarBaseAsync();
        await new VerificadorBaseDatos(Cadena).CrearEsquemaAsync();
        _export = new ExportacionRepository(new ConexionFactory(Cadena));
    }

    public async Task DisposeAsync() => await BorrarBaseAsync();

    private static async Task BorrarBaseAsync()
    {
        await using var conexion = new MySqlConnection(CadenaServidor);
        await conexion.OpenAsync();
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"DROP DATABASE IF EXISTS {Bd};";
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task PrestControl_ExportaLaCarteraDePrestamos()
    {
        var hojas = (await _export.ObtenerTodoAsync(ModoApp.PrestControl))
            .Select(h => h.Nombre).ToList();

        hojas.Should().Contain(["Clientes", "Préstamos", "Cuotas", "Pagos", "Historial"]);
        hojas.Should().NotContain("Vehículos", "los autos son del dealer");
        hojas.Should().NotContain("Productos", "el catalogo es del punto de venta");
    }

    [Fact]
    public async Task DealControl_ExportaInventarioVentasYAlquileres()
    {
        var hojas = (await _export.ObtenerTodoAsync(ModoApp.DealerControl))
            .Select(h => h.Nombre).ToList();

        hojas.Should().Contain(["Vehículos", "Ventas", "Plazos", "Cobros de plazos",
                                "Alquileres", "Cobros de alquiler", "Gastos de importación"]);
        hojas.Should().NotContain("Cuotas", "las cuotas son de prestamos");
        hojas.Should().NotContain("Productos");
    }

    [Fact]
    public async Task Pos500_ExportaCatalogoFacturacionYCaja()
    {
        var hojas = (await _export.ObtenerTodoAsync(ModoApp.Pos500))
            .Select(h => h.Nombre).ToList();

        hojas.Should().Contain(["Productos", "Facturas", "Detalle de facturas",
                                "Cuadres de caja", "Clientes", "Historial"]);
        hojas.Should().NotContain("Préstamos");
        hojas.Should().NotContain("Vehículos");
    }

    /// <summary>
    /// Toda hoja trae encabezados: si una consulta se rompiera al agregar una
    /// columna, el Excel saldria con una hoja vacia y nadie se enteraria hasta
    /// necesitarla.
    /// </summary>
    [Theory]
    [InlineData(ModoApp.PrestControl)]
    [InlineData(ModoApp.DealerControl)]
    [InlineData(ModoApp.Pos500)]
    public async Task TodasLasHojasTraenEncabezados(ModoApp modo)
    {
        var hojas = await _export.ObtenerTodoAsync(modo);

        hojas.Should().NotBeEmpty();
        hojas.Should().OnlyContain(h => h.Encabezados.Count > 0);
    }
}
