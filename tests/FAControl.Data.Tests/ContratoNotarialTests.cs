using FluentAssertions;
using MySqlConnector;
using FAControl.Common;
using FAControl.Data;
using FAControl.Models;
using FAControl.Services;

namespace FAControl.Data.Tests;

/// <summary>
/// Los datos del pagaré notarial contra MySQL real (044): que se guarden al
/// crear el préstamo, que vuelvan enteros al leerlo, y que la garantía larga
/// del modelo del cliente entre en la columna.
///
/// Lo último es lo que motivó la migración: la descripción legal del inmueble
/// de la plantilla mide casi 400 caracteres y `garantia` era VARCHAR(255), así
/// que MySQL la truncaba en silencio o rechazaba el INSERT según el sql_mode.
///
/// Requiere MySQL local (root/root). Recrea facontrol_notarial_test.
/// </summary>
[Collection(ColeccionSesionData.Nombre)]
public class ContratoNotarialTests : IAsyncLifetime
{
    private const string CadenaServidor = "Server=localhost;Port=3306;Uid=root;Pwd=root;";
    private const string CadenaTest = CadenaServidor + "Database=facontrol_notarial_test;";

    /// <summary>La garantía del modelo que mandó el cliente, tal cual.</summary>
    private const string GarantiaLarga =
        "UN SOLAR, CON UNA EXTENSIÓN SUPERFICIAL DE 200M2, DENTRO DEL ÁMBITO DE LA PARCELA " +
        "DESIGNACIÓN CATASTRAL NO. 401850735326, UBICADO EN LA VEGA, LUGAR CALLE EN PROYECTO " +
        "BARRIO NUEVO. EN EL INMUEBLE SE ENCUENTRA UNA MEJORA, CONSISTENTE EN UNA CASA DE " +
        "BLOCK, TECHADA DE ALUZINC, PISO DE CEMENTO, DISTRIBUIDAS EN SALA Y DOS (02) " +
        "HABITACIONES, DOS (2) BAÑOS, EN PROCESO DE EXPANSIÓN";

    private ConexionFactory _factory = null!;
    private PrestamoService _prestamos = null!;
    private PrestamoRepository _repo = null!;
    private ContratoService _contratos = null!;
    private AjustesLocales _ajustes = null!;
    private long _clienteId;

    public async Task InitializeAsync()
    {
        await CrearBaseDeDatosDePruebaAsync();

        _factory = new ConexionFactory(CadenaTest);
        _repo = new PrestamoRepository(_factory);
        var auditoria = new AuditoriaService(new AuditoriaRepository(_factory),
            new SesionRepository(_factory), new UsuarioRepository(_factory));
        _prestamos = new PrestamoService(_factory, _repo, new ContadorRepository(),
            new AmortizacionService(), auditoria, new VehiculoRepository(_factory),
            new NcfRepository(_factory), new PagoRepository(_factory), new PrestamoActaRepository(_factory));

        _ajustes = new AjustesLocales
        {
            NombreNegocio = "Familia Almonte Auto Import SRL",
            RncNegocio = "133696592",
            DireccionNegocio = "la calle Manuel Ubaldo Gómez No. 14, La Vega",
            MunicipioActo = "La Vega",
            NotarioNombre = "Juan José Castillo Coste",
            NotarioMatricula = "6594",
            NotarioCedula = "047-0035382-6",
            RepresentanteNombre = "Marleny del Carmen Abreu de Familia",
            RepresentanteCedula = "402-2796799-5",
            RepresentanteSexo = 2,
            Testigo1Nombre = "Verónica Núñez Familia",
            Testigo1Cedula = "402-1188504-7",
            Testigo2Nombre = "Quírico Roberto Caminero Mejía",
            Testigo2Cedula = "047-0140835-5",
            CuotasParaExigibilidad = 2,
            DiasDeGracia = 5,
            MoraPorcentaje = 20m,
            RegistroTitulos = "Registro de Títulos de La Vega"
        };
        var clientes = new ClienteService(new ClienteRepository(_factory), auditoria);
        _contratos = new ContratoService(_prestamos, clientes, _ajustes,
            new PrestamoActaRepository(_factory));

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
            SesionActual.EstablecerModo(ModoApp.PrestControl);
        }
        using (var cmd = conexion.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO cliente (cedula, nombre, apellido, direccion)
                VALUES ('001-1234567-8', 'José', 'Martínez',
                        'la calle Hermanos Estrellas número 4, La Vega');
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
            drop.CommandText = "DROP DATABASE IF EXISTS facontrol_notarial_test;";
            await drop.ExecuteNonQueryAsync();
        }
        using (var crear = conexion.CreateCommand())
        {
            crear.CommandText =
                "CREATE DATABASE facontrol_notarial_test CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;";
            await crear.ExecuteNonQueryAsync();
        }
        await conexion.ChangeDatabaseAsync("facontrol_notarial_test");

        foreach (var bloque in VerificadorBaseDatos.ObtenerBloquesEjecutables())
        {
            using var cmd = conexion.CreateCommand();
            cmd.CommandText = bloque;
            cmd.CommandTimeout = 120;
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private Task<(long Id, string Codigo)> CrearAsync(ContratoNotarialNuevo? notarial,
        string? garantia = null) =>
        _prestamos.CrearAsync(new NuevoPrestamo(
            _clienteId, 250_000m, 5m, 24, Modalidad.Mensual, MetodoAmortizacion.CuotaFija,
            new DateOnly(2026, 5, 3), Garantia: garantia, Notas: null,
            Notarial: notarial));

    /// <summary>Las partes tal como estarian el dia de la firma.</summary>
    private static DatosNotariales PartesDelDia() => new()
    {
        EmpresaDireccion = "la calle Manuel Ubaldo Gómez No. 14, La Vega",
        Municipio = "La Vega",
        Notario = new ParteDelActo("Juan José Castillo Coste", "047-0035382-6",
            SexoPersona.Masculino, "dominicano", "soltero", "abogado notario público",
            "la calle Manuel Ubaldo Gómez No. 48, La Vega"),
        NotarioMatricula = "6594",
        Representante = new ParteDelActo("Marleny del Carmen Abreu de Familia", "402-2796799-5",
            SexoPersona.Femenino, "dominicano", "casado", "comerciante",
            "la calle Jeremías No. 2, La Vega"),
        Testigos =
        [
            new ParteDelActo("Verónica Núñez Familia", "402-1188504-7", SexoPersona.Femenino),
            new ParteDelActo("Quírico Roberto Caminero Mejía", "047-0140835-5", SexoPersona.Masculino)
        ]
    };

    private static ContratoNotarialNuevo Completo() => new(
        ActoNo: "12",
        FolioNo: "34",
        FechaActo: new DateOnly(2026, 4, 3),
        MunicipioActo: "La Vega",
        DeudorSexo: SexoPersona.Masculino,
        DeudorNacionalidad: "dominicano",
        DeudorEstadoCivil: "soltero",
        DeudorOcupacion: "comerciante",
        CuotasExigibilidad: 3,
        DiasGracia: 7,
        MoraPorcentaje: 15m,
        RegistroTitulos: "Registro de Títulos de Santiago",
        Partes: PartesDelDia());

    // ==================================================================

    [Fact]
    public async Task LosDatosDelActa_VanYVuelvenEnteros()
    {
        var (id, _) = await CrearAsync(Completo(), GarantiaLarga);

        var prestamo = await _repo.ObtenerPorIdAsync(id);

        prestamo!.ActoNo.Should().Be("12");
        prestamo.FolioNo.Should().Be("34");
        prestamo.FechaActo.Should().Be(new DateOnly(2026, 4, 3));
        prestamo.MunicipioActo.Should().Be("La Vega");
        prestamo.DeudorSexo.Should().Be(SexoPersona.Masculino);
        prestamo.DeudorNacionalidad.Should().Be("dominicano");
        prestamo.DeudorEstadoCivil.Should().Be("soltero");
        prestamo.DeudorOcupacion.Should().Be("comerciante");
        prestamo.CuotasExigibilidad.Should().Be(3);
        prestamo.DiasGracia.Should().Be(7);
        prestamo.MoraPorcentaje.Should().Be(15m);
        prestamo.RegistroTitulos.Should().Be("Registro de Títulos de Santiago");
    }

    [Fact]
    public async Task LaGarantiaLargaDelModelo_EntraCompleta()
    {
        // Con VARCHAR(255) esto se truncaba en silencio y el acta salía cortada
        // a la mitad de la descripción del inmueble.
        GarantiaLarga.Length.Should().BeGreaterThan(255, "si no, la prueba no prueba nada");

        var (id, _) = await CrearAsync(Completo(), GarantiaLarga);

        var prestamo = await _repo.ObtenerPorIdAsync(id);
        prestamo!.Garantia.Should().Be(GarantiaLarga);
    }

    [Fact]
    public async Task SinDatosDelActa_ElPrestamoSeCreaIgual()
    {
        // Son datos de un papel, no condiciones del préstamo: ninguno puede
        // impedir que el préstamo se cree.
        var (id, codigo) = await CrearAsync(notarial: null);

        codigo.Should().NotBeNullOrWhiteSpace();
        var prestamo = await _repo.ObtenerPorIdAsync(id);
        prestamo!.ActoNo.Should().BeNull();
        prestamo.DeudorSexo.Should().Be(SexoPersona.NoIndicado);
        prestamo.CuotasExigibilidad.Should().BeNull();
    }

    [Fact]
    public async Task LosCamposEnBlanco_SeGuardanComoNull()
    {
        // Guardar "   " haría que la ficha muestre un campo que parece cargado.
        var (id, _) = await CrearAsync(new ContratoNotarialNuevo(
            ActoNo: "   ", DeudorOcupacion: "  "));

        var prestamo = await _repo.ObtenerPorIdAsync(id);
        prestamo!.ActoNo.Should().BeNull();
        prestamo.DeudorOcupacion.Should().BeNull();
    }

    // ==================================================================
    // El acta armada
    // ==================================================================

    [Fact]
    public async Task ElActaCombina_LoDelPrestamoYLoDeConfiguracion()
    {
        var (id, codigo) = await CrearAsync(Completo(), GarantiaLarga);

        var acta = await _contratos.ArmarNotarialAsync(id);

        // Del préstamo
        acta.Acto.ActoNo.Should().Be("12");
        acta.Acto.CuotasParaExigibilidad.Should().Be(3, "lo del préstamo manda");
        acta.Acto.DiasDeGracia.Should().Be(7);
        acta.Acto.MoraPorcentaje.Should().Be(15m);
        acta.Acto.Garantia.Should().Be(GarantiaLarga);

        // De Configuración
        acta.Acto.Notario.Nombre.Should().Be("Juan José Castillo Coste");
        acta.Acto.NotarioMatricula.Should().Be("6594");
        acta.Acto.Representante.Nombre.Should().Be("Marleny del Carmen Abreu de Familia");
        acta.Acto.Representante.Sexo.Should().Be(SexoPersona.Femenino);
        acta.Acto.EmpresaDireccion.Should().Be("la calle Manuel Ubaldo Gómez No. 14, La Vega");
        acta.Acto.TestigosConNombre.Should().HaveCount(2);

        // Del cliente
        acta.Acto.Deudor.Nombre.Should().Be("José Martínez");
        acta.Acto.Deudor.Domicilio.Should().Be("la calle Hermanos Estrellas número 4, La Vega");

        // De la deuda
        acta.Deuda.CodigoPrestamo.Should().Be(codigo);
        acta.Deuda.MontoPrestado.Should().Be(250_000m);
        acta.CantidadCuotas.Should().Be(24);
        acta.TasaMensual.Should().Be(5m);
        acta.FechaPrimerPago.Should().Be(new DateOnly(2026, 5, 3));
        acta.Acto.QueFalta().Should().BeEmpty();
    }

    [Fact]
    public async Task SinCondicionesEnElPrestamo_CaenLasDeConfiguracion()
    {
        var (id, _) = await CrearAsync(new ContratoNotarialNuevo(), GarantiaLarga);

        var acta = await _contratos.ArmarNotarialAsync(id);

        acta.Acto.CuotasParaExigibilidad.Should().Be(2);
        acta.Acto.DiasDeGracia.Should().Be(5);
        acta.Acto.MoraPorcentaje.Should().Be(20m);
        acta.Acto.RegistroTitulos.Should().Be("Registro de Títulos de La Vega");
        acta.Acto.Municipio.Should().Be("La Vega");
    }

    [Fact]
    public async Task LaFechaDelActo_CaeAlDiaDeCreacionSiNoSeCargo()
    {
        var (id, _) = await CrearAsync(new ContratoNotarialNuevo());

        var acta = await _contratos.ArmarNotarialAsync(id);

        acta.Acto.FechaActo.Should().Be(FechaNegocio.Hoy,
            "sin fecha cargada, el acta se firmó el día que nació el préstamo");
    }

    [Fact]
    public async Task LaUltimaCuota_EsLaFechaDeCierreDelActa()
    {
        // 24 cuotas mensuales desde el 3 de mayo de 2026 terminan en abril de
        // 2028, no en mayo. El modelo del cliente decía mayo: es un error de la
        // plantilla, y el sistema calcula la fecha en vez de copiarla.
        var (id, _) = await CrearAsync(Completo());

        var acta = await _contratos.ArmarNotarialAsync(id);

        acta.FechaUltimoPago.Should().Be(new DateOnly(2028, 4, 3));
    }

    [Fact]
    public async Task LaCuotaDelActa_EsLaPactada_NoLaUltima()
    {
        // La última cuota suele traer el ajuste de centavos del redondeo;
        // escribirla en el contrato confundiría al deudor.
        var (id, _) = await CrearAsync(Completo());

        var acta = await _contratos.ArmarNotarialAsync(id);
        var cuotas = await _prestamos.ObtenerCuotasAsync(id);

        acta.MontoCuota.Should().Be(cuotas.OrderBy(c => c.NumeroCuota).First().MontoTotal);
    }
    // ==================================================================
    // La copia congelada (045)
    // ==================================================================

    [Fact]
    public async Task ReimprimirDaElMismoPapel_AunqueCambieLaConfiguracion()
    {
        // Es el pedido textual del cliente: "debe guardar esos datos por si el
        // usuario necesitara una copia exacta (no se quiere tal error)".
        var (id, _) = await CrearAsync(Completo(), GarantiaLarga);

        var alFirmar = await _contratos.ArmarNotarialAsync(id);
        alFirmar.Acto.Notario.Nombre.Should().Be("Juan José Castillo Coste");
        alFirmar.Acto.Testigos[0].Nombre.Should().Be("Verónica Núñez Familia");

        // El negocio cambia de notario y de testigos al año siguiente
        _ajustes.NotarioNombre = "Otro Notario Distinto";
        _ajustes.NotarioMatricula = "9999";
        _ajustes.Testigo1Nombre = "Testigo Nuevo Uno";
        _ajustes.Testigo2Nombre = "Testigo Nuevo Dos";
        _ajustes.RepresentanteNombre = "Otro Representante";
        _ajustes.DireccionNegocio = "otra dirección";

        var alReimprimir = await _contratos.ArmarNotarialAsync(id);

        alReimprimir.Acto.Notario.Nombre.Should().Be("Juan José Castillo Coste",
            "el acta ya firmada no puede cambiar de notario");
        alReimprimir.Acto.NotarioMatricula.Should().Be("6594");
        alReimprimir.Acto.Representante.Nombre.Should().Be("Marleny del Carmen Abreu de Familia");
        alReimprimir.Acto.EmpresaDireccion.Should().Be("la calle Manuel Ubaldo Gómez No. 14, La Vega");
        alReimprimir.Acto.TestigosConNombre.Select(t => t.Nombre)
            .Should().Equal("Verónica Núñez Familia", "Quírico Roberto Caminero Mejía");
    }

    [Fact]
    public async Task ElGeneroDeLasPartes_TambienSeCongela()
    {
        var (id, _) = await CrearAsync(Completo(), GarantiaLarga);

        var acta = await _contratos.ArmarNotarialAsync(id);

        acta.Acto.Representante.Sexo.Should().Be(SexoPersona.Femenino,
            "el acta la declina en femenino y eso no puede perderse");
        acta.Acto.Testigos[0].Sexo.Should().Be(SexoPersona.Femenino);
        acta.Acto.Testigos[1].Sexo.Should().Be(SexoPersona.Masculino);
        acta.Acto.Notario.Ocupacion.Should().Be("abogado notario público");
    }

    [Fact]
    public async Task SinCopiaCongelada_SeUsaLaConfiguracionVigente()
    {
        // Es el caso de los préstamos anteriores a 045: de esos no hay copia y
        // no se puede inventar una.
        var (id, _) = await CrearAsync(notarial: null);

        (await _contratos.TieneActaCongeladaAsync(id)).Should().BeFalse();

        _ajustes.NotarioNombre = "Notario De Configuración";
        var acta = await _contratos.ArmarNotarialAsync(id);

        acta.Acto.Notario.Nombre.Should().Be("Notario De Configuración");
    }

    [Fact]
    public async Task CorregirElActa_ReemplazaLaCopia()
    {
        // Pedido del cliente: poder arreglar desde Préstamos un dato que se
        // cargó mal el día de la firma.
        var (id, _) = await CrearAsync(Completo(), GarantiaLarga);

        var corregida = PartesDelDia() with
        {
            Notario = new ParteDelActo("Notario Corregido", "047-0035382-6",
                SexoPersona.Masculino, "dominicano", "soltero", "abogado notario público", "su oficina")
        };
        await _contratos.GuardarActaAsync(id, corregida);

        var acta = await _contratos.ArmarNotarialAsync(id);
        acta.Acto.Notario.Nombre.Should().Be("Notario Corregido");
        acta.Acto.Testigos[0].Nombre.Should().Be("Verónica Núñez Familia",
            "corregir el notario no puede borrar a los testigos");
    }

    [Fact]
    public async Task LaCopiaSeGuardaJuntoConElPrestamo()
    {
        var (id, _) = await CrearAsync(Completo(), GarantiaLarga);

        (await _contratos.TieneActaCongeladaAsync(id)).Should().BeTrue();
    }

    // ==================================================================
    // Corregir el acta desde Prestamos > Detalle > Editar
    // ==================================================================

    [Fact]
    public async Task CorregirDesdeElDetalle_CambiaElActaYLaCopia()
    {
        // El caso del pedido: se lleno mal el notario el dia de la firma y hay
        // que poder arreglarlo sin rehacer el prestamo.
        var (id, _) = await CrearAsync(Completo(), GarantiaLarga);

        var partesCorregidas = PartesDelDia() with
        {
            Notario = new ParteDelActo("Notario Que Si Firmo", "047-1111111-1",
                SexoPersona.Masculino, "dominicano", "casado", "abogado notario público",
                "su oficina de La Vega"),
            NotarioMatricula = "7777"
        };

        await _prestamos.EditarAsync(new EdicionPrestamo(
            id, 250_000m, 5m, 24, Modalidad.Mensual, MetodoAmortizacion.CuotaFija,
            new DateOnly(2026, 5, 3), Garantia: GarantiaLarga, Notas: null,
            Motivo: "Se cargó mal el notario el día de la firma",
            Notarial: Completo() with { ActoNo = "99", Partes = partesCorregidas }));

        var acta = await _contratos.ArmarNotarialAsync(id);
        acta.Acto.ActoNo.Should().Be("99");
        acta.Acto.Notario.Nombre.Should().Be("Notario Que Si Firmo");
        acta.Acto.NotarioMatricula.Should().Be("7777");
        acta.Acto.Testigos[0].Nombre.Should().Be("Verónica Núñez Familia",
            "corregir el notario no puede borrar a los testigos");
    }

    [Fact]
    public async Task CorregirElActa_SeAnotaEnLaAuditoria()
    {
        var (id, codigo) = await CrearAsync(Completo(), GarantiaLarga);

        await _prestamos.EditarAsync(new EdicionPrestamo(
            id, 250_000m, 5m, 24, Modalidad.Mensual, MetodoAmortizacion.CuotaFija,
            new DateOnly(2026, 5, 3), Garantia: GarantiaLarga, Notas: null,
            Motivo: "Corrección del acta",
            Notarial: Completo() with { DeudorOcupacion = "chofer" }));

        using var conexion = await _factory.AbrirAsync();
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = """
            SELECT descripcion FROM auditoria
            WHERE entidad = 'prestamo' AND accion = 'modificar'
            ORDER BY id DESC LIMIT 1;
            """;
        var descripcion = (string?)await cmd.ExecuteScalarAsync();

        descripcion.Should().Contain(codigo);
        descripcion.Should().Contain("datos del pagaré notarial");
        descripcion.Should().Contain("Corrección del acta");
    }

    [Fact]
    public async Task CorregirSoloLaGarantia_NoDiceQueSeToco_ElActa()
    {
        // Si la auditoría dijera que se tocó el acta cada vez que se guarda,
        // el historial dejaría de servir para saber qué pasó de verdad.
        var (id, _) = await CrearAsync(Completo(), GarantiaLarga);

        await _prestamos.EditarAsync(new EdicionPrestamo(
            id, 250_000m, 5m, 24, Modalidad.Mensual, MetodoAmortizacion.CuotaFija,
            new DateOnly(2026, 5, 3), Garantia: "Otra garantía", Notas: null,
            Motivo: "Solo la garantía",
            Notarial: Completo() with { Partes = PartesDelDia() }));

        using var conexion = await _factory.AbrirAsync();
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = """
            SELECT descripcion FROM auditoria
            WHERE entidad = 'prestamo' AND accion = 'modificar'
            ORDER BY id DESC LIMIT 1;
            """;
        var descripcion = (string?)await cmd.ExecuteScalarAsync();

        descripcion.Should().Contain("garantía");
        descripcion.Should().NotContain("datos del pagaré notarial");
    }

}
