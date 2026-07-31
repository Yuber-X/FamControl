using FluentAssertions;
using MySqlConnector;
using FAControl.Common;
using FAControl.Data;
using FAControl.Data.Pos;
using FAControl.Models.Pos;
using FAControl.Services;
using FAControl.Services.Pos;

namespace FAControl.Data.Tests;

/// <summary>
/// El aviso por correo del punto de venta (029). Pedido del cliente:
///
///   "Recordatorios por correo (gmail) habra que modificarlo, en vez de enviar
///    un correo por cliente y al dueño, que sea solo al dueño que avise de los
///    productos vencidos."
///
/// Lo que se verifica: quien recibe (uno solo, el dueño), que entra y que no
/// entra en la lista, y que no se manda correo cuando no hay nada que avisar.
///
/// El envio SMTP se sustituye por un doble: probar esto de verdad exigiria
/// abrir una conexion a Gmail.
///
/// Requiere MySQL local (root/root). Recrea su base en cada corrida.
/// </summary>
[Collection(ColeccionSesionData.Nombre)]   // SesionActual es global
public class AvisoCaducidadCorreoTests : IAsyncLifetime
{
    private const string CadenaServidor = "Server=localhost;Port=3306;Uid=root;Pwd=root;";
    private const string Bd = "facontrol_caducidad_correo_test";
    private const string Cadena = CadenaServidor + $"Database={Bd};";

    private ProductoService _productos = null!;
    private CorreoEspia _correo = null!;
    private AjustesLocales _ajustes = null!;
    private RecordatorioCaducidadService _avisos = null!;

    /// <summary>Doble del EmailService: anota lo que se habria enviado.</summary>
    private sealed class CorreoEspia : EmailService
    {
        public CorreoEspia(AjustesLocales ajustes) : base(ajustes) { }

        public List<(string Para, string Asunto, string Cuerpo)> Enviados { get; } = [];

        public override Task EnviarAsync(string destinatario, string asunto, string cuerpo,
            CancellationToken ct = default)
        {
            Enviados.Add((destinatario, asunto, cuerpo));
            return Task.CompletedTask;
        }
    }

    public async Task InitializeAsync()
    {
        await BorrarBaseAsync();
        await new VerificadorBaseDatos(Cadena).CrearEsquemaAsync();

        var fabrica = new ConexionFactory(Cadena);
        var auditoria = new AuditoriaService(new AuditoriaRepository(fabrica),
            new SesionRepository(fabrica), new UsuarioRepository(fabrica));
        var productoRepo = new ProductoRepository(fabrica);
        _productos = new ProductoService(productoRepo, auditoria);

        // Correo "configurado" para que el servicio no se plante antes de tiempo.
        // No sale nada a la red: el envio esta sustituido.
        _ajustes = new AjustesLocales
        {
            GmailRemitente = "negocio@gmail.com",
            CorreoDueno = "dueno@gmail.com",
            AvisoCaducidadDias = 30,
            NombreNegocio = "Negocio de Prueba"
        };
        _ajustes.GmailAppPassword = "clave-de-prueba";
        _correo = new CorreoEspia(_ajustes);
        _avisos = new RecordatorioCaducidadService(productoRepo, _correo, _ajustes);

        await using var conexion = new MySqlConnection(Cadena);
        await conexion.OpenAsync();
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = """
            INSERT INTO usuario (username, password_hash, nombre)
            VALUES ('cajero', 'hash-de-prueba', 'Cajero Prueba');
            SELECT LAST_INSERT_ID();
            """;
        var usuarioId = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        SesionActual.Iniciar(usuarioId, "cajero", "Cajero Prueba", Roles.Admin,
            Permisos.Todos, DateTime.UtcNow, 1);
        SesionActual.EstablecerModo(ModoApp.Pos500);
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

    private Task<long> CrearProductoAsync(string nombre, int cantidad, DateOnly? caducidad,
        decimal precio = 100m) =>
        _productos.CrearAsync(new ProductoDatos(
            Codigo: null, Nombre: nombre, Precio: precio, Cantidad: cantidad,
            Descripcion: null, FechaCaducidad: caducidad));

    /// <summary>Un solo correo, al dueño, con lo caducado y lo que esta por caducar.</summary>
    [Fact]
    public async Task AvisaAlDueno_DeLoCaducadoYLoQueEstaPorCaducar()
    {
        var hoy = FechaNegocio.Hoy;
        await CrearProductoAsync("Leche vencida", 5, hoy.AddDays(-3), precio: 60m);
        await CrearProductoAsync("Yogur por vencer", 10, hoy.AddDays(10), precio: 40m);
        await CrearProductoAsync("Arroz lejano", 20, hoy.AddDays(200));
        await CrearProductoAsync("Detergente sin fecha", 8, null);

        var r = await _avisos.EnviarAsync();

        r.Caducados.Should().Be(1);
        r.PorCaducar.Should().Be(1);
        r.CorreoEnviado.Should().BeTrue();

        _correo.Enviados.Should().ContainSingle("el destinatario es uno solo: el dueño");
        var (para, asunto, cuerpo) = _correo.Enviados[0];
        para.Should().Be("dueno@gmail.com");
        asunto.Should().Contain("CADUCADO");

        cuerpo.Should().Contain("Leche vencida").And.Contain("Yogur por vencer");
        cuerpo.Should().NotContain("Arroz lejano", "cae fuera de la ventana de 30 días");
        cuerpo.Should().NotContain("Detergente", "sin fecha de caducidad no corre riesgo");

        // Valor en riesgo: 5 x 60 + 10 x 40 = 700
        cuerpo.Should().Contain("700.00");
    }

    /// <summary>
    /// Sin existencia no hay nada que rematar ni que sacar de gondola: avisar
    /// solo le hace perder tiempo al dueño.
    /// </summary>
    [Fact]
    public async Task NoAvisaPorProductosSinExistencia()
    {
        var hoy = FechaNegocio.Hoy;
        await CrearProductoAsync("Agotado y vencido", 0, hoy.AddDays(-5));

        var r = await _avisos.EnviarAsync();

        r.Total.Should().Be(0);
        r.CorreoEnviado.Should().BeFalse();
        _correo.Enviados.Should().BeEmpty();
    }

    /// <summary>
    /// Sin nada que avisar NO se manda correo. Un mensaje diario diciendo "todo
    /// bien" se vuelve ruido y se deja de leer, que es lo contrario de lo que se
    /// busca.
    /// </summary>
    [Fact]
    public async Task SinNadaQueAvisar_NoMandaCorreo()
    {
        await CrearProductoAsync("Todo en orden", 10, FechaNegocio.Hoy.AddYears(1));

        var r = await _avisos.EnviarAsync();

        r.CorreoEnviado.Should().BeFalse();
        r.Detalle.Should().Contain("No hay productos");
        _correo.Enviados.Should().BeEmpty();
    }

    /// <summary>Sin correo del dueño no hay a quien avisar: se dice claro.</summary>
    [Fact]
    public async Task SinCorreoDelDueno_LoDiceClaro()
    {
        _ajustes.CorreoDueno = string.Empty;
        await CrearProductoAsync("Leche vencida", 5, FechaNegocio.Hoy.AddDays(-1));

        var enviar = async () => await _avisos.EnviarAsync();

        (await enviar.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*correo del dueño*");
    }

    /// <summary>La ventana de dias es la que el dueño configura.</summary>
    [Fact]
    public async Task LaVentanaDeDias_LaFijaElDueno()
    {
        var hoy = FechaNegocio.Hoy;
        await CrearProductoAsync("Vence en 45 dias", 3, hoy.AddDays(45));

        // Con 30 dias no entra
        (await _avisos.EnviarAsync()).Total.Should().Be(0);

        // Con 60 si
        _ajustes.AvisoCaducidadDias = 60;
        var r = await _avisos.EnviarAsync();
        r.PorCaducar.Should().Be(1);
        r.CorreoEnviado.Should().BeTrue();
    }
}
