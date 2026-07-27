using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace FAControl.Common;

/// <summary>Situación de la licencia de esta instalación.</summary>
public enum EstadoLicencia
{
    /// <summary>Recién instalado: no se digitó ningún código todavía.</summary>
    SinActivar,
    /// <summary>Prueba corriendo (14 días desde que se digitó el código 1).</summary>
    EnPrueba,
    /// <summary>La prueba se pasó de la fecha: la app queda bloqueada hasta el código 2.</summary>
    PruebaVencida,
    /// <summary>Producto habilitado en firme (código 2).</summary>
    Activada
}

/// <summary>
/// Licencia LOCAL de la instalación (pedido del cliente 2026-07-27: cuatro
/// códigos digitables en el launcher).
///
/// Vive en <c>licencia.json</c> junto al ejecutable — NO en la base de datos,
/// porque el código 4 (restablecer todo) borra la base y la licencia tiene que
/// sobrevivir a eso.
///
/// ANTI-MANOSEO, con honestidad sobre su alcance:
///  * el archivo va FIRMADO (HMAC-SHA256 sobre su contenido + el nombre del
///    equipo): editarlo a mano lo invalida y la app vuelve a "sin activar";
///  * el inicio de la prueba se anota TAMBIÉN en el registro de Windows
///    (HKCU\Software\FAControl), y se toma siempre la fecha MÁS VIEJA de las
///    dos: borrar el .json no reinicia los 14 días.
/// Esto frena al usuario común, no a alguien con un depurador: la clave viaja
/// dentro del binario. Es protección comercial, no seguridad criptográfica.
/// </summary>
public class LicenciaLocal
{
    /// <summary>Días que dura la prueba del código 1.</summary>
    public const int DiasDePrueba = 14;

    private const string RutaRegistro = @"Software\FAControl";
    private const string ClaveRegistroPrueba = "InicioPrueba";

    // Clave del HMAC. No es un secreto fuerte (está en el binario): sirve para
    // que editar licencia.json con el bloc de notas no cuele.
    private static readonly byte[] ClaveFirma =
        Encoding.UTF8.GetBytes("FAControl.Licencia.v1.FamiliaAlmonteAutoImport");

    public DateTime? PruebaIniciadaUtc { get; set; }
    public bool Activada { get; set; }
    public DateTime? ActivadaUtc { get; set; }
    /// <summary>Firma del contenido. Si no cuadra, la licencia se descarta.</summary>
    public string Firma { get; set; } = string.Empty;

    private static readonly string Ruta = Path.Combine(AppContext.BaseDirectory, "licencia.json");
    private static readonly JsonSerializerOptions Opciones = new() { WriteIndented = true };

    /// <summary>Estado de hoy. <paramref name="ahoraUtc"/> es inyectable para los tests.</summary>
    public EstadoLicencia EstadoEn(DateTime ahoraUtc)
    {
        if (Activada)
            return EstadoLicencia.Activada;
        if (PruebaIniciadaUtc is not { } inicio)
            return EstadoLicencia.SinActivar;
        return ahoraUtc < inicio.AddDays(DiasDePrueba)
            ? EstadoLicencia.EnPrueba
            : EstadoLicencia.PruebaVencida;
    }

    /// <summary>Días que le quedan a la prueba (0 si ya venció o no hay prueba).</summary>
    public int DiasRestantesEn(DateTime ahoraUtc)
    {
        if (Activada || PruebaIniciadaUtc is not { } inicio)
            return 0;
        var restantes = (inicio.AddDays(DiasDePrueba) - ahoraUtc).TotalDays;
        return restantes <= 0 ? 0 : (int)Math.Ceiling(restantes);
    }

    /// <summary>True si la app puede abrirse (prueba viva o producto activado).</summary>
    public bool PermiteUsar(DateTime ahoraUtc) =>
        EstadoEn(ahoraUtc) is EstadoLicencia.EnPrueba or EstadoLicencia.Activada;

    // ---------- Persistencia ----------

    public static LicenciaLocal Cargar()
    {
        var licencia = LeerArchivo();

        // El registro manda si es MÁS VIEJO: borrar el .json no reinicia la prueba
        if (LeerInicioPruebaDelRegistro() is { } delRegistro
            && (licencia.PruebaIniciadaUtc is null || delRegistro < licencia.PruebaIniciadaUtc))
        {
            licencia.PruebaIniciadaUtc = delRegistro;
        }
        return licencia;
    }

    private static LicenciaLocal LeerArchivo()
    {
        try
        {
            if (!File.Exists(Ruta))
                return new LicenciaLocal();

            var licencia = JsonSerializer.Deserialize<LicenciaLocal>(File.ReadAllText(Ruta));
            if (licencia is null)
                return new LicenciaLocal();

            // Firma inválida = archivo tocado a mano → se ignora por completo
            return licencia.Firma == licencia.Calcular() ? licencia : new LicenciaLocal();
        }
        catch (Exception)
        {
            // Archivo corrupto: se trata como instalación nueva sin activar
            return new LicenciaLocal();
        }
    }

    public void Guardar()
    {
        Firma = Calcular();
        File.WriteAllText(Ruta, JsonSerializer.Serialize(this, Opciones));
        if (PruebaIniciadaUtc is { } inicio)
            EscribirInicioPruebaEnRegistro(inicio);
    }

    private string Calcular()
    {
        var contenido = string.Create(CultureInfo.InvariantCulture,
            $"{PruebaIniciadaUtc:O}|{Activada}|{ActivadaUtc:O}|{Environment.MachineName}");
        using var hmac = new HMACSHA256(ClaveFirma);
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(contenido)));
    }

    // ---------- Marca en el registro (respaldo del inicio de prueba) ----------

    private static DateTime? LeerInicioPruebaDelRegistro()
    {
        try
        {
            using var clave = Registry.CurrentUser.OpenSubKey(RutaRegistro);
            if (clave?.GetValue(ClaveRegistroPrueba) is string texto
                && DateTime.TryParse(texto, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var fecha))
            {
                return fecha.ToUniversalTime();
            }
        }
        catch (Exception)
        {
            // Sin permisos de registro: la licencia sigue funcionando con el .json
        }
        return null;
    }

    private static void EscribirInicioPruebaEnRegistro(DateTime inicioUtc)
    {
        try
        {
            using var clave = Registry.CurrentUser.CreateSubKey(RutaRegistro);
            // Solo se escribe la PRIMERA vez: la marca más vieja es la que vale
            if (clave?.GetValue(ClaveRegistroPrueba) is null)
                clave?.SetValue(ClaveRegistroPrueba, inicioUtc.ToString("O", CultureInfo.InvariantCulture));
        }
        catch (Exception)
        {
            // Sin permisos de registro: no es fatal
        }
    }
}
