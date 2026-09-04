using System.Globalization;
using System.Text;

namespace FAControl.Common;

/// <summary>
/// Números y fechas escritos en letras, como los escribe un notario dominicano.
///
/// Hace falta para el pagaré notarial (plantilla del cliente, 2026-08-26): el
/// acta escribe TODO dos veces —"la suma de DOSCIENTOS CINCUENTA MIL PESOS
/// DOMINICANOS (RD$250,000.00)", "en Veinticuatro (24) cuotas mensuales", "a
/// los Tres (3) días del mes de abril del año Dos Mil Veintiséis (2026)"— y esa
/// duplicación es justamente lo que le da valor probatorio: si alguien altera
/// la cifra, las letras lo delatan.
///
/// Se escribió a mano en vez de traer una dependencia porque las reglas del
/// español son pocas y muy estables (apócope de "uno", "veintiuno" junto,
/// "cien" contra "ciento", concordancia de "un millón/dos millones"), y una
/// librería genérica igual habría que envolverla para el formato notarial.
///
/// Todo el redondeo de dinero respeta la regla del proyecto:
/// <c>Math.Round(v, 2, MidpointRounding.AwayFromZero)</c>.
/// </summary>
public static class NumeroALetras
{
    private static readonly CultureInfo CulturaRd = CultureInfo.GetCultureInfo("es-DO");

    private static readonly string[] Unidades =
    [
        "", "uno", "dos", "tres", "cuatro", "cinco", "seis", "siete", "ocho", "nueve",
        "diez", "once", "doce", "trece", "catorce", "quince", "dieciséis", "diecisiete",
        "dieciocho", "diecinueve", "veinte", "veintiuno", "veintidós", "veintitrés",
        "veinticuatro", "veinticinco", "veintiséis", "veintisiete", "veintiocho", "veintinueve"
    ];

    private static readonly string[] Decenas =
    [
        "", "", "", "treinta", "cuarenta", "cincuenta", "sesenta", "setenta", "ochenta", "noventa"
    ];

    private static readonly string[] Centenas =
    [
        "", "ciento", "doscientos", "trescientos", "cuatrocientos", "quinientos",
        "seiscientos", "setecientos", "ochocientos", "novecientos"
    ];

    private static readonly string[] Meses =
    [
        "", "enero", "febrero", "marzo", "abril", "mayo", "junio",
        "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre"
    ];

    /// <summary>
    /// Género del sustantivo al que acompaña el número. Manda en la apócope:
    /// se dice "un mes" y "una cuota", nunca "uno mes" ni "uno cuota".
    /// </summary>
    public enum GeneroPalabra
    {
        /// <summary>Suelto, sin sustantivo detrás: "veintiuno".</summary>
        Solo,
        /// <summary>"un mes", "veintiún días", "treinta y un años".</summary>
        Masculino,
        /// <summary>"una cuota", "veintiuna cuotas", "treinta y una cuotas".</summary>
        Femenino
    }

    /// <summary>
    /// Un entero en letras: 24 → "veinticuatro", 250000 → "doscientos cincuenta mil".
    /// Soporta hasta billones, que es más de lo que cualquier pagaré va a pedir.
    /// </summary>
    public static string De(long numero)
    {
        if (numero == 0)
            return "cero";
        if (numero < 0)
            return "menos " + De(-numero);
        return Componer(numero).Trim();
    }

    /// <summary>
    /// Un monto como lo escribe el acta: "DOSCIENTOS CINCUENTA MIL PESOS
    /// DOMINICANOS CON 00/100". Los centavos van en fracción, que es la
    /// convención notarial y evita discutir cómo se dice "cincuenta centavos".
    /// </summary>
    public static string Pesos(decimal monto, bool mayusculas = true)
    {
        var redondeado = Math.Round(monto, 2, MidpointRounding.AwayFromZero);
        var negativo = redondeado < 0;
        redondeado = Math.Abs(redondeado);

        var entero = (long)decimal.Truncate(redondeado);
        var centavos = (int)decimal.Truncate((redondeado - entero) * 100m);

        // "un peso", no "uno peso": el apócope aplica igual que con "un millón".
        var letras = entero == 1 ? "un peso dominicano" : $"{De(entero)} pesos dominicanos";
        var texto = $"{(negativo ? "menos " : "")}{letras} con {centavos:00}/100";
        return mayusculas ? texto.ToUpper(CulturaRd) : texto;
    }

    /// <summary>
    /// El monto completo como aparece en el acta, letras y cifra juntas:
    /// "DOSCIENTOS CINCUENTA MIL PESOS DOMINICANOS CON 00/100 (RD$250,000.00)".
    /// </summary>
    public static string PesosConCifra(decimal monto, bool mayusculas = true)
    {
        var cifra = Math.Round(monto, 2, MidpointRounding.AwayFromZero)
            .ToString("N2", CulturaRd);
        return $"{Pesos(monto, mayusculas)} (RD${cifra})";
    }

    /// <summary>
    /// Un número acompañado de su cifra, como "Veinticuatro (24)". Es el patrón
    /// que la plantilla usa para cuotas, plazos, días de gracia y porcentajes.
    /// </summary>
    public static string ConCifra(long numero, bool capitalizar = true,
        GeneroPalabra genero = GeneroPalabra.Solo)
    {
        var letras = Apocopar(De(numero), genero);
        if (capitalizar)
            letras = Capitalizar(letras);
        return $"{letras} ({numero})";
    }

    /// <summary>
    /// Igual que <see cref="ConCifra"/> pero rellenando la cifra con un cero a
    /// la izquierda, como escribe el notario: "Dos (02)", "Cinco (05)".
    /// </summary>
    public static string ConCifraDosDigitos(long numero, bool capitalizar = true,
        GeneroPalabra genero = GeneroPalabra.Solo)
    {
        var letras = Apocopar(De(numero), genero);
        if (capitalizar)
            letras = Capitalizar(letras);
        return $"{letras} ({numero:00})";
    }

    /// <summary>
    /// Un porcentaje como lo escribe el acta: "Cinco (05%)". Los decimales se
    /// escriben con "punto" para no inventar reglas: 2.5 → "dos punto cinco".
    /// </summary>
    public static string Porcentaje(decimal valor)
    {
        var entero = decimal.Truncate(valor);
        if (valor == entero)
            return $"{Capitalizar(De((long)entero))} ({entero:00}%)";

        var decimales = valor.ToString(CultureInfo.InvariantCulture).Split('.')[^1].TrimEnd('0');
        var letrasDecimales = string.Join(" ", decimales.Select(d => De(d - '0')));
        var cifra = valor.ToString("0.##", CulturaRd);
        return $"{Capitalizar(De((long)entero))} punto {letrasDecimales} ({cifra}%)";
    }

    /// <summary>
    /// La fecha del acto, tal como abre el pagaré: "a los Tres (3) días del mes
    /// de abril del año Dos Mil Veintiséis (2026)".
    /// </summary>
    public static string FechaLarga(DateOnly fecha) =>
        $"a los {ConCifra(fecha.Day)} días del mes de {Meses[fecha.Month]} " +
        $"del año {ConCifra(fecha.Year)}";

    /// <summary>
    /// Una fecha dentro del texto de una cláusula: "el Tres (3) de mayo del año
    /// Dos Mil Veintiséis (2026)". La usan la fecha del primer y del último pago.
    /// </summary>
    public static string FechaEnTexto(DateOnly fecha) =>
        $"el {ConCifra(fecha.Day)} de {Meses[fecha.Month]} del año {ConCifra(fecha.Year)}";

    // ================= interno =================

    /// <summary>
    /// Apócope de los números terminados en uno cuando les sigue un sustantivo:
    /// "un mes", "veintiún días", "treinta y una cuotas". Sin esto el acta
    /// decía "uno (01) mes" y "veintiuno (21) cuotas", que en un documento que
    /// se firma ante notario se lee como un descuido.
    ///
    /// El once, el veintiuno acentuado y el "veintiún" van con su propia forma
    /// porque el español los escribe distinto según lo que venga detrás.
    /// </summary>
    private static string Apocopar(string letras, GeneroPalabra genero)
    {
        if (genero == GeneroPalabra.Solo)
            return letras;

        var femenino = genero == GeneroPalabra.Femenino;

        if (letras == "uno")
            return femenino ? "una" : "un";
        if (letras == "veintiuno")
            return femenino ? "veintiuna" : "veintiún";
        if (letras.EndsWith(" y uno", StringComparison.Ordinal))
            return letras[..^3] + (femenino ? "una" : "un");
        // "ciento uno", "mil uno", "veintiún mil uno"…
        if (letras.EndsWith(" uno", StringComparison.Ordinal))
            return letras[..^3] + (femenino ? "una" : "un");
        return letras;
    }

    /// <summary>
    /// Mayuscula inicial en CADA palabra, como escribe el notario: la plantilla
    /// dice "del año Dos Mil Veintiséis (2026)", no "Dos mil veintiséis".
    ///
    /// La conjuncion "y" queda en minuscula ("Treinta y Uno"), que es como se
    /// escribe de verdad: ponerle mayuscula a un conector se ve mal y no lo hace
    /// ningun acta.
    /// </summary>
    private static string Capitalizar(string texto) => string.Join(' ',
        texto.Split(' ').Select(palabra => palabra is "y" || palabra.Length == 0
            ? palabra
            : char.ToUpper(palabra[0], CulturaRd) + palabra[1..]));

    private static string Componer(long n)
    {
        if (n == 0)
            return "";
        if (n < 30)
            return Unidades[n];
        if (n < 100)
        {
            var decena = Decenas[n / 10];
            return n % 10 == 0 ? decena : $"{decena} y {Unidades[n % 10]}";
        }
        if (n == 100)
            return "cien";                       // "cien" exacto; 101 ya es "ciento uno"
        if (n < 1_000)
        {
            var centena = Centenas[n / 100];
            return n % 100 == 0 ? centena : $"{centena} {Componer(n % 100)}";
        }
        if (n < 1_000_000)
            return Agrupar(n, 1_000, "mil", "mil");
        if (n < 1_000_000_000_000L)
            return Agrupar(n, 1_000_000, "un millón", "millones");
        return Agrupar(n, 1_000_000_000_000L, "un billón", "billones");
    }

    /// <summary>
    /// Arma un grupo de miles/millones respetando dos irregularidades del
    /// español: "mil" nunca lleva "un" delante (mil, no "un mil"), y el millón
    /// sí concuerda (un millón, dos millones).
    /// </summary>
    private static string Agrupar(long n, long escala, string singular, string plural)
    {
        var cuantos = n / escala;
        var resto = n % escala;

        var cabeza = cuantos == 1
            ? singular
            : $"{Componer(cuantos)} {plural}";

        var sb = new StringBuilder(cabeza);
        if (resto > 0)
            sb.Append(' ').Append(Componer(resto));
        return sb.ToString();
    }
}
