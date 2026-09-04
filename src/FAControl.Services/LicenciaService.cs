using System.Security.Cryptography;
using System.Text;
using FAControl.Common;
using Serilog;

namespace FAControl.Services;

/// <summary>Qué hace el código que se digitó.</summary>
public enum AccionCodigo
{
    /// <summary>El código no corresponde a ninguno de los siete.</summary>
    Invalido,
    /// <summary>Código 1 — arranca la prueba de 14 días (toda la suite abierta).</summary>
    IniciarPrueba,
    /// <summary>Código 2 — habilita la suite completa, sin vencimiento.</summary>
    ActivarTodo,
    /// <summary>Código 3 — habilita PrestControl.</summary>
    ActivarPrestControl,
    /// <summary>Código 4 — habilita DealControl.</summary>
    ActivarDealControl,
    /// <summary>Código 5 — habilita POS-500 (producto aparte).</summary>
    ActivarPos500,
    /// <summary>Código 6 — respaldo obligatorio y luego limpiar todo (DESTRUCTIVO).</summary>
    RespaldarYLimpiar,
    /// <summary>Código 7 — eliminar todo sin respaldo, para retirar la instalación (DESTRUCTIVO).</summary>
    EliminarTodo
}

/// <summary>Resultado de digitar un código.</summary>
/// <param name="Aceptado">False = código inválido o no aplicable en este momento.</param>
public record ResultadoCodigo(AccionCodigo Accion, bool Aceptado, string Mensaje);

/// <summary>
/// Los siete códigos del launcher (pedido del cliente 2026-07-29, que amplía los
/// cuatro del 2026-07-27 a activación POR MODO):
///
///   1 prueba · 2 suite completa · 3 PrestControl · 4 DealControl · 5 POS-500
///   6 respaldar y limpiar todo · 7 eliminar todo
///
/// Los códigos por producto (3, 4, 5) se piden recién CUANDO TERMINA LA PRUEBA:
/// durante los 14 días la suite está abierta completa. Eso se decide en
/// LicenciaLocal.PermiteProducto, no aquí.
///
/// Los códigos NO viajan en texto: aquí solo hay su SHA-256 con sal. El
/// desarrollador los guarda aparte (Freelancer - Claude Save\docs\Done\
/// FAControl_CodigosDeActivacion_v2_2026-07-30.md). Que estén hasheados evita
/// que alguien los saque abriendo el .exe con un editor de texto; no pretende
/// resistir ingeniería inversa (ver el comentario de LicenciaLocal).
/// </summary>
public class LicenciaService
{
    private const string Sal = "FAControl.Codigos.v1";

    // Código 1 — prueba de 14 días
    private const string HashPrueba = "2B3ABABF908542BA4DFFD316AF0CC9DCD4BE47BD4FDD7E43487E2B54CDF957D2";
    // Código 2 — suite completa
    private const string HashActivarTodo = "8F50A1B99EF11D966E1873C0414CDA7EF462791703F3447EFFAB878200FCCE8B";
    // Código 3 — PrestControl
    private const string HashPrestControl = "21A11A8752D8E48A8BB602CEF1DC51433143089F80E2955CBCE43E8C6DADAE92";
    // Código 4 — DealControl
    private const string HashDealControl = "6D938CAA98C60A713B351688C902C2F4653B88D69B3D1CCE2FB17BFD2EBB461D";
    // Código 5 — POS-500 (producto aparte)
    private const string HashPos500 = "FA456F474757EC89253F20CBF61EF28D204DDC0E4FBF01B63C45AF33A0B14BC8";
    // Código 6 — respaldar y limpiar todo (DESTRUCTIVO)
    private const string HashRespaldarYLimpiar = "B19A6AC727845FB4AF66484962A28808947A4A0ACA7C8263E213A48971DAC7E0";
    // Código 7 — eliminar todo, sin respaldo (DESTRUCTIVO)
    private const string HashEliminarTodo = "C73E9503FFE8EF0442CF78D5D7A82948856E16345F260950296B318A4DF2FD13";

    private readonly LicenciaLocal _licencia;

    public LicenciaService(LicenciaLocal licencia) => _licencia = licencia;

    public LicenciaLocal Licencia => _licencia;

    public EstadoLicencia Estado => _licencia.EstadoEn(DateTime.UtcNow);
    public bool PermiteUsar => _licencia.PermiteUsar(DateTime.UtcNow);
    public int DiasRestantes => _licencia.DiasRestantesEn(DateTime.UtcNow);

    /// <summary>True si este modo se puede abrir hoy.</summary>
    public bool PermiteModo(ModoApp modo) => _licencia.PermiteModo(modo, DateTime.UtcNow);

    /// <summary>True si el cliente ya compró POS-500 (el launcher lo muestra distinto).</summary>
    public bool Pos500Comprado => _licencia.TieneProducto(ProductosLicencia.Pos500);

    /// <summary>Texto de estado para mostrar en el launcher.</summary>
    public string EstadoTexto => Estado switch
    {
        EstadoLicencia.Activada => "Producto activado",
        EstadoLicencia.EnPrueba => DiasRestantes == 1
            ? "Versión de prueba — queda 1 día"
            : $"Versión de prueba — quedan {DiasRestantes} días",
        EstadoLicencia.PruebaVencida => ProductosTexto is { } comprados
            ? $"Prueba terminada — activado: {comprados}"
            : "La prueba terminó: ingresa el código del módulo que vas a usar",
        _ => ProductosTexto is { } sueltos
            ? $"Activado: {sueltos}"
            : "Sin activar: ingresa un código para empezar"
    };

    /// <summary>Nombres de los productos comprados sueltos, o null si no hay ninguno.</summary>
    private string? ProductosTexto
    {
        get
        {
            if (_licencia.Productos.Count == 0)
                return null;
            var nombres = _licencia.Productos.Select(NombreDeProducto);
            return string.Join(", ", nombres);
        }
    }

    private static string NombreDeProducto(string clave) =>
        clave.Equals(ProductosLicencia.Pos500, StringComparison.OrdinalIgnoreCase)
            ? "POS-500"
            : Enum.TryParse<ModoApp>(clave, ignoreCase: true, out var modo)
                ? IdentidadModo.De(modo).Nombre
                : clave;

    /// <summary>Qué código es, sin ejecutar nada.</summary>
    public static AccionCodigo Reconocer(string? codigo)
    {
        var hash = Hashear(codigo);
        if (hash.Length == 0)
            return AccionCodigo.Invalido;

        // Comparación en tiempo fijo: no le regala pistas a quien pruebe a mano
        if (Igual(hash, HashPrueba)) return AccionCodigo.IniciarPrueba;
        if (Igual(hash, HashActivarTodo)) return AccionCodigo.ActivarTodo;
        if (Igual(hash, HashPrestControl)) return AccionCodigo.ActivarPrestControl;
        if (Igual(hash, HashDealControl)) return AccionCodigo.ActivarDealControl;
        if (Igual(hash, HashPos500)) return AccionCodigo.ActivarPos500;
        if (Igual(hash, HashRespaldarYLimpiar)) return AccionCodigo.RespaldarYLimpiar;
        if (Igual(hash, HashEliminarTodo)) return AccionCodigo.EliminarTodo;
        return AccionCodigo.Invalido;
    }

    /// <summary>
    /// Aplica los códigos de licencia (1 a 5), los que solo tocan
    /// <c>licencia.json</c>. Los destructivos (6 y 7) los ejecuta
    /// RecuperacionService: piden carpeta y confirmaciones, así que este método
    /// solo los RECONOCE y los devuelve.
    /// </summary>
    public ResultadoCodigo Aplicar(string? codigo)
    {
        var accion = Reconocer(codigo);
        switch (accion)
        {
            case AccionCodigo.IniciarPrueba:
                if (_licencia.Activada)
                    return new(accion, false, "El producto ya está activado: no hace falta la prueba.");
                if (_licencia.PruebaIniciadaUtc is not null)
                {
                    // La prueba se usa UNA sola vez por instalación
                    return new(accion, false, _licencia.EstadoEn(DateTime.UtcNow) == EstadoLicencia.EnPrueba
                        ? $"La prueba ya está corriendo: quedan {DiasRestantes} día(s)."
                        : "La prueba de esta instalación ya se usó. Hace falta el código de activación.");
                }
                _licencia.PruebaIniciadaUtc = DateTime.UtcNow;
                _licencia.Guardar();
                Log.Information("Licencia: prueba de {Dias} días iniciada", LicenciaLocal.DiasDePrueba);
                return new(accion, true,
                    $"Prueba activada por {LicenciaLocal.DiasDePrueba} días, con todo abierto. " +
                    "Al terminar, cada módulo pide su código.");

            case AccionCodigo.ActivarTodo:
                if (_licencia.Activada)
                    return new(accion, true, "La suite completa ya estaba activada.");
                _licencia.Activada = true;
                _licencia.ActivadaUtc = DateTime.UtcNow;
                _licencia.Guardar();
                Log.Information("Licencia: suite completa activada");
                return new(accion, true,
                    "Suite completa activada: PrestControl y DealControl, sin vencimiento. " +
                    "Gracias por confiar en FAControl.");

            case AccionCodigo.ActivarPrestControl:
                return ActivarProducto(accion, ProductosLicencia.De(ModoApp.PrestControl));

            case AccionCodigo.ActivarDealControl:
                return ActivarProducto(accion, ProductosLicencia.De(ModoApp.DealerControl));

            case AccionCodigo.ActivarPos500:
                return ActivarProducto(accion, ProductosLicencia.Pos500);

            case AccionCodigo.RespaldarYLimpiar:
            case AccionCodigo.EliminarTodo:
                return new(accion, true, "Código válido.");

            default:
                return new(AccionCodigo.Invalido, false, "Ese código no es válido.");
        }
    }

    /// <summary>
    /// Habilita un producto suelto. Aunque la suite ya esté activada o la prueba
    /// esté corriendo se guarda igual: es una compra, y tiene que seguir valiendo
    /// cuando la prueba termine.
    /// </summary>
    private ResultadoCodigo ActivarProducto(AccionCodigo accion, string clave)
    {
        var nombre = NombreDeProducto(clave);

        if (!_licencia.AgregarProducto(clave))
            return new(accion, true, $"{nombre} ya estaba activado.");

        _licencia.Guardar();
        Log.Information("Licencia: producto {Producto} activado", clave);

        if (_licencia.Activada)
            return new(accion, true, $"{nombre} activado (la suite completa ya lo incluía).");

        return _licencia.EstadoEn(DateTime.UtcNow) == EstadoLicencia.EnPrueba
            ? new(accion, true, $"{nombre} activado. Queda habilitado también cuando termine la prueba.")
            : new(accion, true, $"{nombre} activado. Ya puedes entrar.");
    }

    private static string Hashear(string? codigo)
    {
        // Se normaliza: espacios y minúsculas no deberían invalidar un código dictado
        var limpio = (codigo ?? string.Empty).Trim().Replace(" ", string.Empty).ToUpperInvariant();
        if (limpio.Length == 0)
            return string.Empty;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Sal + limpio)));
    }

    private static bool Igual(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(a), Encoding.ASCII.GetBytes(b));
}
