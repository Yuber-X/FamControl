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
                                "Alquileres", "Cobros de alquiler", "Renovaciones de alquiler",
                                "Gastos de importación"]);
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
    /// El archivo lleva el modo al final (2026-08-01). Sin eso, tres exports
    /// del mismo dia caian con el mismo nombre en la misma carpeta y el ultimo
    /// pisaba a los otros dos.
    /// </summary>
    [Theory]
    [InlineData(ModoApp.PrestControl, "FAControl_Export_2026-08-01 PrestControl.xlsx")]
    [InlineData(ModoApp.DealerControl, "FAControl_Export_2026-08-01 DealControl.xlsx")]
    [InlineData(ModoApp.Pos500, "FAControl_Export_2026-08-01 POS-500.xlsx")]
    public void ElNombreDelArchivoTerminaConElModo(ModoApp modo, string esperado) =>
        NombreExport.De(modo, new DateOnly(2026, 8, 1)).Should().Be(esperado);

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

    // ================= Estado de las ventas del dealer =================

    /// <summary>
    /// En la BD una venta cobrada por completo sigue diciendo 'activa': ese
    /// enum distingue valida de anulada, no cobrada de pendiente. En el Excel
    /// que el dueño abre para revisar, "activa" al lado de una venta terminada
    /// se lee como si el cliente todavia debiera plata, asi que se traduce a
    /// 'pagado' (pedido del cliente, 2026-08-01).
    ///
    /// La cuenta es la misma que EstadoFinanciamiento.EstaSaldada. Si alguien
    /// la cambia de un lado y no del otro, el detalle de la venta y el Excel
    /// diran cosas distintas de la misma venta.
    /// </summary>
    [Fact]
    public async Task Ventas_LasCobradasPorCompletoDicenPagado()
    {
        await SembrarVentasAsync();

        var ventas = (await _export.ObtenerTodoAsync(ModoApp.DealerControl))
            .First(h => h.Nombre == "Ventas");
        var codigo = Columna(ventas, "codigo");
        var estado = Columna(ventas, "estado");
        var porCodigo = ventas.Filas.ToDictionary(
            f => (string)f[codigo]!, f => (string)f[estado]!);

        porCodigo["VC-0001"].Should().Be("pagado", "al contado la plata entro en el acto");
        porCodigo["VC-0002"].Should().Be("pagado", "los dos plazos estan saldados");
        porCodigo["VC-0003"].Should().Be("activa", "todavia debe 60,000");
        porCodigo["VC-0004"].Should().Be("cancelada", "una venta anulada no es una venta pagada");
    }

    private static int Columna(TablaExportada tabla, string encabezado)
    {
        var indice = tabla.Encabezados.ToList().IndexOf(encabezado);
        indice.Should().BeGreaterThanOrEqualTo(0, $"la hoja debe traer la columna {encabezado}");
        return indice;
    }

    /// <summary>
    /// Cuatro ventas del dealer, una por cada rama del CASE. Se insertan con SQL
    /// directo y no con los Services: acá se prueba la CONSULTA del export, y
    /// pasar por la logica de negocio metaria de por medio validaciones que no
    /// tienen nada que ver con lo que se quiere ver.
    /// </summary>
    private static async Task SembrarVentasAsync()
    {
        await using var conexion = new MySqlConnection(Cadena);
        await conexion.OpenAsync();
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = """
            INSERT INTO cliente (id, ambito, cedula, nombre, apellido)
                 VALUES (1, 'dealercontrol', '001-1111111-1', 'Ana', 'Pérez');
            INSERT INTO vehiculo (id, codigo, marca, modelo)
                 VALUES (1, 'V-0001', 'Toyota', 'Corolla'),
                        (2, 'V-0002', 'Honda',  'CR-V'),
                        (3, 'V-0003', 'Kia',    'Sportage'),
                        (4, 'V-0004', 'Nissan', 'Frontier');

            -- Al contado: sin plazos y sin inicial, pero cobrada
            INSERT INTO venta_vehiculo (id, codigo, vehiculo_id, cliente_id, precio, tipo_venta, inicial)
                 VALUES (1, 'VC-0001', 1, 1, 500000.00, 'contado', 0.00);
            -- Por plazos, saldada: 100,000 de inicial + 2 plazos de 200,000 cobrados
            INSERT INTO venta_vehiculo (id, codigo, vehiculo_id, cliente_id, precio, tipo_venta, inicial)
                 VALUES (2, 'VC-0002', 2, 1, 500000.00, 'plazos', 100000.00);
            INSERT INTO venta_plazo (venta_id, numero, fecha_vencimiento, monto, monto_pagado, estado)
                 VALUES (2, 1, '2026-07-01', 200000.00, 200000.00, 'pagado'),
                        (2, 2, '2026-08-01', 200000.00, 200000.00, 'pagado');
            -- Por plazos, a medio cobrar: falta el segundo plazo
            INSERT INTO venta_vehiculo (id, codigo, vehiculo_id, cliente_id, precio, tipo_venta, inicial)
                 VALUES (3, 'VC-0003', 3, 1, 300000.00, 'plazos', 60000.00);
            INSERT INTO venta_plazo (venta_id, numero, fecha_vencimiento, monto, monto_pagado, estado)
                 VALUES (3, 1, '2026-07-01', 120000.00, 120000.00, 'pagado'),
                        (3, 2, '2026-08-01', 120000.00,      0.00, 'pendiente');
            -- Anulada: al contado, pero el cliente devolvio el vehiculo
            INSERT INTO venta_vehiculo (id, codigo, vehiculo_id, cliente_id, precio, tipo_venta, inicial, estado)
                 VALUES (4, 'VC-0004', 4, 1, 400000.00, 'contado', 0.00, 'cancelada');
            """;
        await cmd.ExecuteNonQueryAsync();
    }
}
