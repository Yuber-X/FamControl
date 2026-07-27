using System.Security.Cryptography;
using System.Text;
using FAControl.Common;
using Serilog;

namespace FAControl.Services;

/// <summary>Qué hace el código que se digitó.</summary>
public enum AccionCodigo
{
    /// <summary>El código no corresponde a ninguno de los cuatro.</summary>
    Invalido,
    /// <summary>Código 1 — arranca la prueba de 14 días.</summary>
    IniciarPrueba,
    /// <summary>Código 2 — habilita el producto en firme.</summary>
    Activar,
    /// <summary>Código 3 — recuperar el acceso sin perder datos.</summary>
    RecuperarAcceso,
    /// <summary>Código 4 — restablecer todo desde cero (DESTRUCTIVO).</summary>
    RestablecerTodo
}

/// <summary>Resultado de digitar un código.</summary>
/// <param name="Aceptado">False = código inválido o no aplicable en este momento.</param>
public record ResultadoCodigo(AccionCodigo Accion, bool Aceptado, string Mensaje);

/// <summary>
/// Los cuatro códigos del launcher (pedido del cliente 2026-07-27).
///
/// Los códigos NO viajan en texto: acá solo hay su SHA-256 con sal. El
/// desarrollador los guarda aparte (Freelancer - Claude Save\docs\Done\
/// FAControl_CodigosDeActivacion_v1_2026-07-27.md). Que estén hasheados evita
/// que alguien los saque abriendo el .exe con un editor de texto; no pretende
/// resistir ingeniería inversa (ver el comentario de LicenciaLocal).
/// </summary>
public class LicenciaService
{
    private const string Sal = "FAControl.Codigos.v1";

    // Código 1 — prueba de 14 días
    private const string HashPrueba = "2B3ABABF908542BA4DFFD316AF0CC9DCD4BE47BD4FDD7E43487E2B54CDF957D2";
    // Código 2 — habilitar el producto totalmente
    private const string HashActivar = "8F50A1B99EF11D966E1873C0414CDA7EF462791703F3447EFFAB878200FCCE8B";
    // Código 3 — el cliente perdió las contraseñas y NO quiere perder datos
    private const string HashRecuperar = "9765F620ABC8DA57632C0974102ACAC487591A976E1DC677D0810992DFF5D450";
    // Código 4 — restablecer todo desde el inicio (DESTRUCTIVO)
    private const string HashRestablecer = "B19A6AC727845FB4AF66484962A28808947A4A0ACA7C8263E213A48971DAC7E0";

    private readonly LicenciaLocal _licencia;

    public LicenciaService(LicenciaLocal licencia) => _licencia = licencia;

    public LicenciaLocal Licencia => _licencia;

    public EstadoLicencia Estado => _licencia.EstadoEn(DateTime.UtcNow);
    public bool PermiteUsar => _licencia.PermiteUsar(DateTime.UtcNow);
    public int DiasRestantes => _licencia.DiasRestantesEn(DateTime.UtcNow);

    /// <summary>Texto de estado para mostrar en el launcher.</summary>
    public string EstadoTexto => Estado switch
    {
        EstadoLicencia.Activada => "Producto activado",
        EstadoLicencia.EnPrueba => DiasRestantes == 1
            ? "Versión de prueba — queda 1 día"
            : $"Versión de prueba — quedan {DiasRestantes} días",
        EstadoLicencia.PruebaVencida => "La prueba se venció: ingresá el código de activación",
        _ => "Sin activar: ingresá un código para empezar"
    };

    /// <summary>Qué código es, sin ejecutar nada.</summary>
    public static AccionCodigo Reconocer(string? codigo)
    {
        var hash = Hashear(codigo);
        if (hash.Length == 0)
            return AccionCodigo.Invalido;

        // Comparación en tiempo fijo: no le regala pistas a quien pruebe a mano
        if (Igual(hash, HashPrueba)) return AccionCodigo.IniciarPrueba;
        if (Igual(hash, HashActivar)) return AccionCodigo.Activar;
        if (Igual(hash, HashRecuperar)) return AccionCodigo.RecuperarAcceso;
        if (Igual(hash, HashRestablecer)) return AccionCodigo.RestablecerTodo;
        return AccionCodigo.Invalido;
    }

    /// <summary>
    /// Aplica los códigos 1 y 2 (los que solo tocan la licencia local).
    /// Los códigos 3 y 4 los ejecuta RecuperacionService: piden confirmación
    /// y datos extra, así que este método solo los RECONOCE y los devuelve.
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
                    $"Prueba activada por {LicenciaLocal.DiasDePrueba} días. Ya podés entrar.");

            case AccionCodigo.Activar:
                if (_licencia.Activada)
                    return new(accion, true, "El producto ya estaba activado.");
                _licencia.Activada = true;
                _licencia.ActivadaUtc = DateTime.UtcNow;
                _licencia.Guardar();
                Log.Information("Licencia: producto activado");
                return new(accion, true, "Producto activado. Gracias por confiar en FAControl.");

            case AccionCodigo.RecuperarAcceso:
                return new(accion, true, "Código de recuperación válido.");

            case AccionCodigo.RestablecerTodo:
                return new(accion, true, "Código de restablecimiento válido.");

            default:
                return new(AccionCodigo.Invalido, false, "Ese código no es válido.");
        }
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
