using System.Text;
using FAControl.Services;
using FluentAssertions;
using MySqlConnector;

namespace FAControl.Data.Tests;

/// <summary>
/// Integración real de respaldo y restauración: se levanta una base, se
/// respalda con mysqldump, se destroza y se restaura con mysql.exe.
///
/// POR QUE EXISTE: el 02/08/2026, restaurando en la PC del cliente, la app
/// mostró "Se está cerrando la canalización" — el síntoma de escribir en una
/// tubería que mysql.exe ya había cerrado. El motivo real lo había escrito en
/// stderr y el código nunca llegaba a leerlo. No falló ninguna prueba porque
/// no había ninguna: RespaldoService no tenía cobertura.
///
/// Requiere el servicio MySQL80 local con credenciales Dev.
/// </summary>
public class RespaldoRestauracionTests : IAsyncLifetime
{
    private const string CadenaServidor = "Server=localhost;Port=3306;Uid=root;Pwd=root;";
    private const string Bd = "facontrol_respaldo_test";
    private const string Cadena = CadenaServidor + $"Database={Bd};";

    private string _carpeta = string.Empty;

    public async Task InitializeAsync()
    {
        // Se borra ANTES y no solo al final: una corrida interrumpida deja la
        // base viva y rompe todas las corridas siguientes.
        await BorrarBaseAsync();
        _carpeta = Path.Combine(Path.GetTempPath(), "fa_respaldo_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_carpeta);
    }

    public async Task DisposeAsync()
    {
        await BorrarBaseAsync();
        if (Directory.Exists(_carpeta))
            try { Directory.Delete(_carpeta, recursive: true); } catch (IOException) { }
    }

    private static async Task BorrarBaseAsync()
    {
        await using var conexion = new MySqlConnection(CadenaServidor);
        await conexion.OpenAsync();
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"DROP DATABASE IF EXISTS {Bd};";
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<T?> EscalarAsync<T>(string sql)
    {
        await using var conexion = new MySqlConnection(Cadena);
        await conexion.OpenAsync();
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = sql;
        var valor = await cmd.ExecuteScalarAsync();
        return valor is null or DBNull ? default : (T)Convert.ChangeType(valor, typeof(T));
    }

    private static async Task EjecutarAsync(string sql)
    {
        await using var conexion = new MySqlConnection(Cadena);
        await conexion.OpenAsync();
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Base con esquema y un cliente de acentos difíciles.</summary>
    private static async Task PrepararBaseConDatosAsync()
    {
        await new VerificadorBaseDatos(Cadena).CrearEsquemaAsync();
        await EjecutarAsync(
            """
            INSERT INTO cliente (ambito, cedula, nombre, apellido, telefono, direccion)
            VALUES ('prestcontrol', '053-0038510-0', 'José Ángel', 'Sánchez Peña',
                    '809-494-4144', 'Sabina, Constanza');
            """);
    }

    [Fact]
    public async Task RespaldarYRestaurar_DevuelveLosDatosTalCual()
    {
        await PrepararBaseConDatosAsync();
        var servicio = new RespaldoService(Cadena);
        var ruta = Path.Combine(_carpeta, "respaldo.sql");

        await servicio.RespaldarAsync(ruta);

        File.Exists(ruta).Should().BeTrue();
        new FileInfo(ruta).Length.Should().BeGreaterThan(0);

        // Se borra el cliente: si la restauración no hace nada, el test miente.
        await EjecutarAsync("DELETE FROM cliente;");
        (await EscalarAsync<long>("SELECT COUNT(*) FROM cliente;")).Should().Be(0);

        await servicio.RestaurarAsync(ruta);

        (await EscalarAsync<long>("SELECT COUNT(*) FROM cliente;")).Should().Be(1);
    }

    /// <summary>
    /// Los acentos son el fallo silencioso clásico: el respaldo "funciona", y
    /// meses después los nombres aparecen como JosÃ© Ãngel. Por eso ahora las
    /// dos herramientas van con --default-character-set=utf8mb4.
    /// </summary>
    [Fact]
    public async Task RespaldarYRestaurar_ConservaLosAcentos()
    {
        await PrepararBaseConDatosAsync();
        var servicio = new RespaldoService(Cadena);
        var ruta = Path.Combine(_carpeta, "acentos.sql");

        await servicio.RespaldarAsync(ruta);
        await EjecutarAsync("DELETE FROM cliente;");
        await servicio.RestaurarAsync(ruta);

        (await EscalarAsync<string>("SELECT nombre FROM cliente LIMIT 1;"))
            .Should().Be("José Ángel");
        (await EscalarAsync<string>("SELECT apellido FROM cliente LIMIT 1;"))
            .Should().Be("Sánchez Peña");
    }

    /// <summary>
    /// El fallo del 02/08/2026. mysql.exe corta en el primer error y cierra la
    /// tubería; antes eso salía como "Se está cerrando la canalización" y el
    /// motivo real se perdía. Ahora el mensaje tiene que traer el error de MySQL.
    /// </summary>
    [Fact]
    public async Task Restaurar_ArchivoConSqlInvalido_DiceElErrorRealDeMySql()
    {
        await PrepararBaseConDatosAsync();
        var ruta = Path.Combine(_carpeta, "roto.sql");
        // Suficientes lineas para que el proceso muera con la escritura en curso
        var sql = new StringBuilder("SET NAMES utf8mb4;\n");
        sql.AppendLine("INSERT INTO tabla_que_no_existe VALUES (1);");
        for (var i = 0; i < 5000; i++)
            sql.AppendLine($"INSERT INTO tabla_que_no_existe VALUES ({i});");
        await File.WriteAllTextAsync(ruta, sql.ToString());

        var accion = async () => await new RespaldoService(Cadena).RestaurarAsync(ruta);

        var ex = await accion.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain("tabla_que_no_existe",
            "el usuario tiene que ver QUE fallo, no el sintoma de la tuberia rota");
        ex.Which.Message.Should().NotContain("canalización");
    }

    [Fact]
    public async Task Restaurar_ArchivoQueNoEsSql_SeRechazaAntesDeTocarLaBase()
    {
        await PrepararBaseConDatosAsync();
        var ruta = Path.Combine(_carpeta, "no-es-respaldo.xlsx");
        await File.WriteAllBytesAsync(ruta, new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x14, 0x00 });

        var accion = async () => await new RespaldoService(Cadena).RestaurarAsync(ruta);

        (await accion.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("no parece un respaldo");

        // Lo importante: no llego a tocar nada
        (await EscalarAsync<long>("SELECT COUNT(*) FROM cliente;")).Should().Be(1);
    }

    [Fact]
    public async Task Restaurar_ArchivoVacio_NoTocaLaBase()
    {
        await PrepararBaseConDatosAsync();
        var ruta = Path.Combine(_carpeta, "vacio.sql");
        await File.WriteAllTextAsync(ruta, string.Empty);

        var accion = async () => await new RespaldoService(Cadena).RestaurarAsync(ruta);

        (await accion.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("vacío");
        (await EscalarAsync<long>("SELECT COUNT(*) FROM cliente;")).Should().Be(1);
    }

    /// <summary>
    /// Un respaldo a medio escribir es peor que ninguno: parece bueno hasta el
    /// dia que hace falta. Se escribe a .parcial y se renombra al final, asi que
    /// no puede quedar ningun .parcial suelto tras un respaldo exitoso.
    /// </summary>
    [Fact]
    public async Task Respaldar_NoDejaArchivosParcialesTirados()
    {
        await PrepararBaseConDatosAsync();
        var ruta = Path.Combine(_carpeta, "limpio.sql");

        await new RespaldoService(Cadena).RespaldarAsync(ruta);

        Directory.GetFiles(_carpeta, "*.parcial").Should().BeEmpty();
        File.Exists(ruta).Should().BeTrue();
    }

    [Fact]
    public async Task Respaldar_SobreUnRespaldoAnterior_LoReemplaza()
    {
        await PrepararBaseConDatosAsync();
        var servicio = new RespaldoService(Cadena);
        var ruta = Path.Combine(_carpeta, "repetido.sql");

        await servicio.RespaldarAsync(ruta);
        var primero = new FileInfo(ruta).Length;

        await EjecutarAsync(
            """
            INSERT INTO cliente (ambito, cedula, nombre, apellido)
            VALUES ('prestcontrol', '402-2622469-5', 'Segundo', 'Cliente');
            """);
        await servicio.RespaldarAsync(ruta);

        new FileInfo(ruta).Length.Should().BeGreaterThan(primero,
            "el segundo respaldo tiene un cliente mas");
    }

    [Fact]
    public void BuscarHerramienta_Inexistente_ExplicaQueHacer()
    {
        var accion = () => RespaldoService.BuscarHerramienta("mysqlnoexiste.exe");

        accion.Should().Throw<FileNotFoundException>()
            .WithMessage("*PATH*");
    }
}
