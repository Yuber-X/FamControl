namespace FAControl.Models;

/// <summary>
/// Los tres documentos que se pueden emitir por un préstamo (pedido del cliente
/// 2026-09-03). Se guardan por nombre en la configuración, así que los valores
/// no se reordenan.
/// </summary>
public enum TipoContrato
{
    /// <summary>El pagaré que la app viene emitiendo desde el 2026-07-17.</summary>
    Pagare = 0,
    /// <summary>El acta notarial de la plantilla que mandó el cliente el 2026-08-26.</summary>
    Notarial = 1,
    /// <summary>El acta notarial seguida de la tabla de cuotas del pagaré.</summary>
    Combinado = 2
}

public static class TiposDeContrato
{
    public static readonly TipoContrato[] Todos =
        [TipoContrato.Pagare, TipoContrato.Notarial, TipoContrato.Combinado];

    public static string Nombre(TipoContrato tipo) => tipo switch
    {
        TipoContrato.Pagare => "Pagaré",
        TipoContrato.Notarial => "Pagaré notarial",
        _ => "Pagaré notarial + cuotas"
    };

    public static string Descripcion(TipoContrato tipo) => tipo switch
    {
        TipoContrato.Pagare => "El de siempre: encabezado del negocio, la deuda y la tabla de cuotas.",
        TipoContrato.Notarial => "El acta para firmar ante notario, con testigos y garantía.",
        _ => "El acta notarial y, atrás, la tabla de cuotas completa."
    };

    /// <summary>Nombre de archivo sin extensión, para el PDF y el expediente.</summary>
    public static string NombreArchivo(TipoContrato tipo, string codigoPrestamo) => tipo switch
    {
        TipoContrato.Pagare => $"Pagare_{codigoPrestamo}",
        TipoContrato.Notarial => $"PagareNotarial_{codigoPrestamo}",
        _ => $"Contrato_{codigoPrestamo}"
    };
}

/// <summary>
/// Sexo del deudor. NO es un dato demográfico: el acta notarial está declinada
/// en género de punta a punta ("dominicano/a", "domiciliado/a", "el señor / la
/// señora", "EL DEUDOR / LA DEUDORA"), y sin esto el documento sale mal escrito
/// la mitad de las veces.
///
/// <see cref="NoIndicado"/> es el default y hace que el acta use la forma
/// masculina genérica, que es como está redactada la plantilla original.
/// </summary>
public enum SexoPersona
{
    NoIndicado = 0,
    Masculino = 1,
    Femenino = 2
}

/// <summary>
/// Concordancia de género para el texto del acta. Vive en Models porque es
/// parte del documento, no de la interfaz.
/// </summary>
public static class Genero
{
    public static bool EsFemenino(SexoPersona sexo) => sexo == SexoPersona.Femenino;

    /// <summary>"el señor" / "la señora".</summary>
    public static string Tratamiento(SexoPersona sexo) =>
        EsFemenino(sexo) ? "la señora" : "el señor";

    /// <summary>"dominicano" / "dominicana", partiendo de la nacionalidad escrita.</summary>
    public static string Gentilicio(string nacionalidad, SexoPersona sexo)
    {
        var texto = nacionalidad.Trim();
        if (texto.Length == 0)
            texto = "dominicano";
        if (!EsFemenino(sexo))
            return texto;
        // "dominicano" -> "dominicana". Si ya viene en femenino o no termina en
        // -o (ej. "estadounidense"), se deja tal cual.
        return texto.EndsWith('o') ? texto[..^1] + "a" : texto;
    }

    /// <summary>"domiciliado y residente" / "domiciliada y residente".</summary>
    public static string Domiciliado(SexoPersona sexo) =>
        EsFemenino(sexo) ? "domiciliada y residente" : "domiciliado y residente";

    /// <summary>"portador" / "portadora".</summary>
    public static string Portador(SexoPersona sexo) =>
        EsFemenino(sexo) ? "portadora" : "portador";

    /// <summary>"EL DEUDOR" / "LA DEUDORA".</summary>
    public static string Deudor(SexoPersona sexo) =>
        EsFemenino(sexo) ? "LA DEUDORA" : "EL DEUDOR";

    /// <summary>"mayor de edad, soltero" concuerda; "casada" no se toca si ya vino escrito.</summary>
    public static string EstadoCivil(string estadoCivil, SexoPersona sexo)
    {
        var texto = estadoCivil.Trim();
        if (texto.Length == 0)
            return string.Empty;
        if (!EsFemenino(sexo))
            return texto;
        return texto.EndsWith('o') ? texto[..^1] + "a" : texto;
    }
}

/// <summary>
/// Un compareciente del acta con todos los datos que el notario escribe de él.
/// Lo usan el deudor, la representante de la acreedora, el notario y los dos
/// testigos: el acta les pide exactamente lo mismo a todos.
/// </summary>
public record ParteDelActo(
    string Nombre,
    string Cedula,
    SexoPersona Sexo = SexoPersona.NoIndicado,
    string Nacionalidad = "dominicano",
    string EstadoCivil = "",
    string Ocupacion = "",
    string Domicilio = "")
{
    public bool EstaVacia => string.IsNullOrWhiteSpace(Nombre);

    /// <summary>
    /// La persona como la describe el acta, con la concordancia resuelta:
    /// "JOSÉ MARTÍNEZ, dominicano, mayor de edad, soltero, comerciante, portador
    /// de la cédula de identidad y electoral No. 001-1234567-8, domiciliado y
    /// residente en …".
    ///
    /// Los datos que falten simplemente no aparecen, en vez de dejar una coma
    /// suelta o un "(sin dato)": un acta con huecos se completa a mano, pero una
    /// con basura impresa hay que rehacerla.
    /// </summary>
    public string Descripcion()
    {
        var partes = new List<string> { Nombre.ToUpperInvariant() };

        var gentilicio = Genero.Gentilicio(Nacionalidad, Sexo);
        if (!string.IsNullOrWhiteSpace(gentilicio))
            partes.Add(gentilicio);

        partes.Add("mayor de edad");

        var estado = Genero.EstadoCivil(EstadoCivil, Sexo);
        if (!string.IsNullOrWhiteSpace(estado))
            partes.Add(estado);

        if (!string.IsNullOrWhiteSpace(Ocupacion))
            partes.Add(Ocupacion.Trim());

        if (!string.IsNullOrWhiteSpace(Cedula))
            partes.Add($"{Genero.Portador(Sexo)} de la cédula de identidad y electoral No. {Cedula.Trim()}");

        if (!string.IsNullOrWhiteSpace(Domicilio))
            partes.Add($"{Genero.Domiciliado(Sexo)} en {Domicilio.Trim()}");

        return string.Join(", ", partes);
    }
}

/// <summary>
/// Todo lo que el pagaré notarial necesita y el pagaré común no tiene
/// (plantilla del cliente, 2026-08-26).
///
/// Es un DTO plano: la capa Printing no conoce ViewModels ni entidades. Cada
/// campo puede venir vacío — el acta se imprime igual, con el hueco en blanco
/// para llenar a mano, que es como se trabaja con un notario.
/// </summary>
public record DatosNotariales
{
    /// <summary>Encabezado del acta. Los asigna el notario, no el sistema.</summary>
    public string ActoNo { get; init; } = string.Empty;
    public string FolioNo { get; init; } = string.Empty;

    /// <summary>Fecha del acto. Por defecto, el día en que se crea el préstamo.</summary>
    public DateOnly FechaActo { get; init; }

    /// <summary>Ciudad, municipio y provincia donde se instrumenta.</summary>
    public string Municipio { get; init; } = string.Empty;

    public ParteDelActo Notario { get; init; } = new("", "");
    /// <summary>Matrícula del Colegio Dominicano de Notarios.</summary>
    public string NotarioMatricula { get; init; } = string.Empty;

    public ParteDelActo Deudor { get; init; } = new("", "");

    /// <summary>Asiento social de la empresa acreedora.</summary>
    public string EmpresaDireccion { get; init; } = string.Empty;
    /// <summary>Quién firma por la empresa.</summary>
    public ParteDelActo Representante { get; init; } = new("", "");

    /// <summary>Cuántas cuotas en atraso hacen exigible el total ("de dos (02) cuotas").</summary>
    public int CuotasParaExigibilidad { get; init; } = 2;
    /// <summary>Días de gracia antes de que corra la mora.</summary>
    public int DiasDeGracia { get; init; } = 5;
    /// <summary>Mora sobre el monto adeudado, en % ("un veinte por ciento (20%)").</summary>
    public decimal MoraPorcentaje { get; init; } = 20m;

    /// <summary>
    /// La garantía tal como la describe el acta: solar, superficie, designación
    /// catastral, ubicación y mejoras. Es texto largo a propósito — la del
    /// modelo del cliente mide casi 400 caracteres.
    /// </summary>
    public string Garantia { get; init; } = string.Empty;
    /// <summary>Registro de Títulos que se autoriza a ejecutar el traspaso.</summary>
    public string RegistroTitulos { get; init; } = string.Empty;

    public IReadOnlyList<ParteDelActo> Testigos { get; init; } = [];

    /// <summary>Los testigos que realmente tienen nombre.</summary>
    public IReadOnlyList<ParteDelActo> TestigosConNombre =>
        [.. Testigos.Where(t => !t.EstaVacia)];

    /// <summary>
    /// Qué le falta al acta para estar completa. La UI lo muestra como aviso, no
    /// como error: el acta se imprime igual y el notario llena a mano lo que
    /// falte. Bloquear la impresión por un campo vacío sería peor.
    /// </summary>
    public IReadOnlyList<string> QueFalta()
    {
        var faltan = new List<string>();
        if (Notario.EstaVacia) faltan.Add("el notario");
        if (Representante.EstaVacia) faltan.Add("quién firma por la empresa");
        if (string.IsNullOrWhiteSpace(EmpresaDireccion)) faltan.Add("la dirección de la empresa");
        if (string.IsNullOrWhiteSpace(Municipio)) faltan.Add("el municipio del acto");
        if (string.IsNullOrWhiteSpace(Garantia)) faltan.Add("la garantía");
        if (TestigosConNombre.Count < 2) faltan.Add("los dos testigos");
        if (Deudor.EstaVacia || string.IsNullOrWhiteSpace(Deudor.Domicilio))
            faltan.Add("el domicilio del deudor");
        return faltan;
    }
}

/// <summary>
/// El pagaré notarial listo para imprimir: la deuda (que comparte con el pagaré
/// común) más los datos del acto.
/// </summary>
public record PagareNotarialImpreso(
    PagareImpreso Deuda,
    DatosNotariales Acto,
    /// <summary>Modalidad, para decir "cuotas mensuales" o "cuotas quincenales".</summary>
    Modalidad Modalidad,
    DateOnly FechaPrimerPago,
    DateOnly FechaUltimoPago,
    decimal TasaMensual,
    decimal MontoCuota,
    int CantidadCuotas);
