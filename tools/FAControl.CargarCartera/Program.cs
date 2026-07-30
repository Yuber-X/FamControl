using MySqlConnector;
using FAControl.CargarCartera;
using FAControl.Common;
using FAControl.Data;
using FAControl.Models;
using FAControl.Services;

// =============================================================
// FAControl — Cargador de la cartera real de préstamos
//
// Para qué: el cliente entregó su listado de préstamos vigentes el 29/07/2026
// (PDF en "Freelancer - Claude Active\FamControl"). Esta herramienta lo carga en
// la base usando los MISMOS servicios que la aplicación: los códigos P-0001…, la
// tabla de cuotas y la auditoría salen exactamente igual que si se hubieran
// tecleado a mano. Nada de INSERT crudos que se desincronicen del negocio.
//
// Uso:
//   dotnet run --project tools/FAControl.CargarCartera -- --confirmar
//   dotnet run --project tools/FAControl.CargarCartera -- --confirmar --cadena "Server=...;"
//
// SIN --confirmar solo muestra lo que haría (y no toca la base).
//
// ⚠ BORRA los datos de negocio previos (clientes, préstamos, cuotas, pagos,
// vehículos, ventas, alquileres, gastos, auditoría) y REINICIA los contadores.
// NO toca usuarios, roles, permisos ni la secuencia de comprobantes fiscales.
// =============================================================

const string CadenaPorDefecto = "Server=localhost;Port=3306;Uid=root;Pwd=root;Database=facontrol_db;";

var confirmar = args.Contains("--confirmar");
var cadena = LeerCadena(args) ?? CadenaPorDefecto;
var servidor = new MySqlConnectionStringBuilder(cadena);

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.WriteLine("=== FAControl — carga de la cartera real ===");
Console.WriteLine($"Base de datos : {servidor.Database} en {servidor.Server}");
Console.WriteLine($"Préstamos     : {CarteraReal.Filas.Count}");
Console.WriteLine();

if (!confirmar)
{
    foreach (var fila in CarteraReal.Filas)
    {
        Console.WriteLine($"  {fila.Nombre} {fila.Apellido,-12}  " +
                          $"{fila.Capital,12:N2}  {fila.TasaMensual,5}%  " +
                          $"{fila.Cuotas,3} cuotas  {Etiqueta(fila.Metodo)}");
    }
    Console.WriteLine();
    Console.WriteLine("Simulación: no se tocó la base. Agregá --confirmar para cargar de verdad.");
    return 0;
}

try
{
    var fabrica = new ConexionFactory(cadena);
    var usuarios = new UsuarioRepository(fabrica);
    var auditoria = new AuditoriaService(new AuditoriaRepository(fabrica),
        new SesionRepository(fabrica), usuarios);
    var clientes = new ClienteService(new ClienteRepository(fabrica), auditoria);
    var prestamos = new PrestamoService(fabrica, new PrestamoRepository(fabrica),
        new ContadorRepository(), new AmortizacionService(), auditoria,
        new VehiculoRepository(fabrica), new NcfRepository(fabrica), new PagoRepository(fabrica));

    await AbrirSesionAdminAsync(fabrica, usuarios);
    await LimpiarDatosDeNegocioAsync(cadena);

    var cargados = 0;
    foreach (var fila in CarteraReal.Filas)
    {
        var clienteId = await clientes.CrearAsync(new ClienteDatos(
            fila.Cedula, fila.Nombre, fila.Apellido, fila.Telefono, fila.Direccion,
            Email: null,
            Notas: $"Cartera vigente al 29/07/2026. Préstamo desde {fila.FechaPrestamo:dd/MM/yyyy}."));

        var (_, codigo) = await prestamos.CrearAsync(new NuevoPrestamo(
            clienteId, fila.Capital, fila.TasaMensual, fila.Cuotas,
            Modalidad.Mensual, fila.Metodo, fila.PrimerPago,
            Garantia: fila.Garantia,
            Notas: fila.Notas));

        Console.WriteLine($"  ✔ {codigo}  {fila.Nombre} {fila.Apellido}  " +
                          $"{fila.Capital:N2} al {fila.TasaMensual}%  ({Etiqueta(fila.Metodo)})");
        cargados++;
    }

    Console.WriteLine();
    Console.WriteLine($"Listo: {cargados} clientes con su préstamo.");
    Console.WriteLine("Los PAGOS ya hechos NO se cargaron a propósito: están anotados en las notas " +
                      "de cada préstamo para confirmarlos con el cliente y registrarlos desde Cobros.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine($"✖ Falló la carga: {ex.Message}");
    return 1;
}

// -------------------------------------------------------------

static string? LeerCadena(string[] args)
{
    var i = Array.IndexOf(args, "--cadena");
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

static string Etiqueta(MetodoAmortizacion metodo) => metodo switch
{
    MetodoAmortizacion.SoloInteres => "abierto (solo interés)",
    MetodoAmortizacion.CuotaFija => "a rédito (cuota fija)",
    _ => metodo.ToString()
};

/// <summary>
/// La auditoría exige sesión activa y crear préstamos exige permiso. Se toma el
/// primer usuario Admin que exista en la base; si no hay ninguno, se avisa en vez
/// de reventar con un error de base de datos.
/// </summary>
static async Task AbrirSesionAdminAsync(ConexionFactory fabrica, UsuarioRepository usuarios)
{
    var todos = await usuarios.ObtenerTodosAsync(incluirProgramadores: true);
    var admin = todos.FirstOrDefault(u => u.RolNombre is Roles.Admin or Roles.Programador)
        ?? throw new InvalidOperationException(
            "No hay ningún usuario Admin o Programador en esta base. Abrí FAControl una vez y " +
            "creá la cuenta inicial antes de cargar la cartera.");

    var sesionId = await new SesionRepository(fabrica)
        .RegistrarLoginAsync(admin.Id, DateTime.UtcNow, ipLocal: null);
    SesionActual.Iniciar(admin.Id, admin.Username, admin.Nombre, Roles.Admin,
        Permisos.Todos, DateTime.UtcNow, sesionId);
    SesionActual.EstablecerModo(ModoApp.PrestControl);

    Console.WriteLine($"Sesión de carga: {admin.Username} ({admin.RolNombre})");
}

/// <summary>
/// Deja la base sin datos de negocio pero con los usuarios (pedido del cliente:
/// "eliminá los datos anteriores excepto los usuarios ya creados").
///
/// Se apagan las FK durante el TRUNCATE porque las tablas se referencian entre
/// sí y no hay orden que sirva para todas.
/// </summary>
static async Task LimpiarDatosDeNegocioAsync(string cadena)
{
    string[] tablas =
    [
        DbNames.Pago, DbNames.Cuota, DbNames.Prestamo,
        DbNames.VentaPlazoPago, DbNames.VentaPlazo, DbNames.DocumentoVenta,
        DbNames.Alquiler, DbNames.VentaVehiculo, DbNames.VehiculoGasto,
        DbNames.VehiculoReparacion, DbNames.Vehiculo,
        DbNames.Cliente, DbNames.Auditoria
    ];

    await using var conexion = new MySqlConnection(cadena);
    await conexion.OpenAsync();

    await using (var apagar = conexion.CreateCommand())
    {
        apagar.CommandText = "SET FOREIGN_KEY_CHECKS = 0;";
        await apagar.ExecuteNonQueryAsync();
    }

    foreach (var tabla in tablas)
    {
        await using var truncar = conexion.CreateCommand();
        // Nombre de tabla: viene de DbNames (constantes del propio código), no de entrada externa
        truncar.CommandText = $"TRUNCATE TABLE `{tabla}`;";
        await truncar.ExecuteNonQueryAsync();
    }

    await using (var contadores = conexion.CreateCommand())
    {
        // Sin esto los códigos seguirían desde donde quedaron las pruebas (P-0037…)
        contadores.CommandText = "UPDATE contador SET valor = 0;";
        await contadores.ExecuteNonQueryAsync();
    }

    await using (var prender = conexion.CreateCommand())
    {
        prender.CommandText = "SET FOREIGN_KEY_CHECKS = 1;";
        await prender.ExecuteNonQueryAsync();
    }

    Console.WriteLine($"Datos de prueba borrados ({tablas.Length} tablas). Usuarios, roles, " +
                      "permisos y comprobantes fiscales intactos.");
    Console.WriteLine();
}
