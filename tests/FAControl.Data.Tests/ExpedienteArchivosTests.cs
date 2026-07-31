using FluentAssertions;
using MySqlConnector;
using FAControl.Common;
using FAControl.Data;
using FAControl.Models;
using FAControl.Services;

namespace FAControl.Data.Tests;

/// <summary>
/// Subir un archivo al expediente, de punta a punta y contra MySQL de verdad.
///
/// POR QUÉ EXISTE: el 30/07/2026 el INSERT quedó con nueve columnas y ocho
/// valores tras generalizar el expediente a préstamos. Compilaba, los tests
/// pasaban —ninguno llegaba a la base— y en la máquina la aplicación se cerraba
/// de golpe al subir un contrato. Este test recorre el camino completo: ficha en
/// la base, archivo en el disco y lectura de vuelta.
///
/// Requiere MySQL local (root/root). Recrea su base en cada corrida.
/// </summary>
[Collection(ColeccionSesionData.Nombre)]   // SesionActual es global
public class ExpedienteArchivosTests : IAsyncLifetime
{
    private const string CadenaServidor = "Server=localhost;Port=3306;Uid=root;Pwd=root;";
    private const string Bd = "facontrol_expediente_test";
    private const string Cadena = CadenaServidor + $"Database={Bd};";

    private string _carpeta = null!;
    private ExpedienteService _expedientes = null!;
    private long _prestamoId;
    private long _ventaId;

    public async Task InitializeAsync()
    {
        // xUnit estrena instancia por test: la base se rehace en cada uno, si no
        // el segundo choca con las tablas del primero.
        await BorrarBaseAsync();
        await new VerificadorBaseDatos(Cadena).CrearEsquemaAsync();
        var fabrica = new ConexionFactory(Cadena);

        // Carpeta propia: el expediente escribe archivos de verdad
        _carpeta = Path.Combine(Path.GetTempPath(), $"facontrol_exp_{Guid.NewGuid():N}");
        var ajustes = new AjustesLocales { CarpetaExpedientes = _carpeta };

        var auditoria = new AuditoriaService(new AuditoriaRepository(fabrica),
            new SesionRepository(fabrica), new UsuarioRepository(fabrica));
        _expedientes = new ExpedienteService(new DocumentoVentaRepository(fabrica), auditoria, ajustes);

        await using var conexion = new MySqlConnection(Cadena);
        await conexion.OpenAsync();

        long usuarioId;
        await using (var cmd = conexion.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO usuario (username, password_hash, nombre)
                VALUES ('exp', 'hash-de-prueba', 'Usuario Test');
                SELECT LAST_INSERT_ID();
                """;
            usuarioId = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        }
        SesionActual.Iniciar(usuarioId, "exp", "Usuario Test", Roles.Admin,
            Permisos.Todos, DateTime.UtcNow, 1);
        SesionActual.EstablecerModo(ModoApp.PrestControl);

        // Un préstamo y una venta reales: el expediente cuelga de uno de los dos
        // y las claves foráneas exigen que existan.
        // Sin variables de sesión (@x): MySqlConnector las rechaza salvo que se
        // habiliten en la cadena. Cada id se lee y se pasa como parámetro.
        long clienteId, vehiculoId;
        await using (var cmd = conexion.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO cliente (cedula, nombre, apellido) VALUES ('001-0000009-9', 'Ana', 'Prueba');
                SELECT LAST_INSERT_ID();
                """;
            clienteId = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        }
        await using (var cmd = conexion.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO prestamo (cliente_id, codigo, monto_capital, tasa_interes, plazo_cuotas,
                                      modalidad, metodo_amortizacion, fecha_inicio)
                VALUES (@cliente, 'P-9001', 10000, 5, 6, 'mensual', 'cuota_fija', '2026-01-01');
                SELECT LAST_INSERT_ID();
                """;
            cmd.Parameters.AddWithValue("@cliente", clienteId);
            _prestamoId = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        }
        await using (var cmd = conexion.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO vehiculo (codigo, marca, modelo, anio, precio_venta)
                VALUES ('V-9001', 'Toyota', 'Corolla', 2020, 500000);
                SELECT LAST_INSERT_ID();
                """;
            vehiculoId = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        }
        await using (var cmd = conexion.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO venta_vehiculo (codigo, vehiculo_id, cliente_id, fecha_venta, precio, created_by)
                VALUES ('VC-9001', @vehiculo, @cliente, '2026-01-01', 500000, @usuario);
                SELECT LAST_INSERT_ID();
                """;
            cmd.Parameters.AddWithValue("@vehiculo", vehiculoId);
            cmd.Parameters.AddWithValue("@cliente", clienteId);
            cmd.Parameters.AddWithValue("@usuario", usuarioId);
            _ventaId = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        }
    }

    public async Task DisposeAsync()
    {
        SesionActual.Cerrar();
        try { if (Directory.Exists(_carpeta)) Directory.Delete(_carpeta, recursive: true); }
        catch (IOException) { /* carpeta temporal: si queda, no importa */ }

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

    /// <summary>
    /// Archivo de origen con su nombre REAL: el expediente guarda el nombre tal
    /// como lo ve el usuario, así que no se le puede anteponer un identificador.
    /// Va en una carpeta propia para que dos tests no se pisen.
    /// </summary>
    private string CrearArchivo(string nombre)
    {
        var carpeta = Path.Combine(Path.GetTempPath(), $"facontrol_origen_{Guid.NewGuid():N}");
        Directory.CreateDirectory(carpeta);
        var ruta = Path.Combine(carpeta, nombre);
        File.WriteAllText(ruta, "contrato de prueba");
        return ruta;
    }

    /// <summary>El caso que se rompía: subir un papel al expediente de un préstamo.</summary>
    [Fact]
    public async Task SubirUnContrato_AlExpedienteDeUnPrestamo_GuardaFichaYArchivo()
    {
        var dueno = DuenoExpediente.DePrestamo(_prestamoId);
        var origen = CrearArchivo("contrato firmado.pdf");

        var documento = await _expedientes.AgregarAsync(dueno, origen, TipoDocumento.Contrato);

        documento.PrestamoId.Should().Be(_prestamoId);
        documento.VentaId.Should().BeNull("el expediente es de un préstamo");
        documento.Nombre.Should().Be("contrato firmado.pdf");
        documento.RutaRelativa.Should().StartWith($"prestamos/{_prestamoId}/");
        File.Exists(_expedientes.RutaAbsoluta(documento)).Should().BeTrue("el archivo se copió");

        var listado = await _expedientes.ObtenerAsync(dueno);
        listado.Should().ContainSingle().Which.Id.Should().Be(documento.Id);

        Directory.Delete(Path.GetDirectoryName(origen)!, recursive: true);
    }

    [Fact]
    public async Task SubirUnArchivo_AlExpedienteDeUnaVenta_SigueFuncionando()
    {
        var dueno = DuenoExpediente.DeVenta(_ventaId);
        var origen = CrearArchivo("factura firmada.pdf");

        var documento = await _expedientes.AgregarAsync(dueno, origen, TipoDocumento.FacturaEscaneada);

        documento.VentaId.Should().Be(_ventaId);
        documento.PrestamoId.Should().BeNull();
        documento.RutaRelativa.Should().StartWith($"ventas/{_ventaId}/");

        Directory.Delete(Path.GetDirectoryName(origen)!, recursive: true);
    }

    /// <summary>
    /// Los expedientes no se ven entre sí: el papel de un préstamo no aparece en
    /// la venta ni al revés, aunque los ids coincidan.
    /// </summary>
    [Fact]
    public async Task LosExpedientesNoSeMezclan()
    {
        var origenPrestamo = CrearArchivo("pagare.pdf");
        var origenVenta = CrearArchivo("conduce.pdf");

        await _expedientes.AgregarAsync(DuenoExpediente.DePrestamo(_prestamoId), origenPrestamo);
        await _expedientes.AgregarAsync(DuenoExpediente.DeVenta(_ventaId), origenVenta);

        var delPrestamo = await _expedientes.ObtenerAsync(DuenoExpediente.DePrestamo(_prestamoId));
        var deLaVenta = await _expedientes.ObtenerAsync(DuenoExpediente.DeVenta(_ventaId));

        delPrestamo.Should().ContainSingle().Which.Nombre.Should().Be("pagare.pdf");
        deLaVenta.Should().ContainSingle().Which.Nombre.Should().Be("conduce.pdf");

        Directory.Delete(Path.GetDirectoryName(origenPrestamo)!, recursive: true);
        Directory.Delete(Path.GetDirectoryName(origenVenta)!, recursive: true);
    }
}
