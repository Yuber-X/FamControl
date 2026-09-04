using System.Text.RegularExpressions;

namespace FAControl.Models;

/// <summary>
/// Secuencia de comprobantes fiscales autorizada por la DGII (012_ncf.sql).
/// Prefijo = serie + tipo (ej. "B02" tradicional, "E32" e-CF). La parte
/// numérica se rellena con ceros a la izquierda hasta <see cref="Largo"/>.
/// Un NCF consumido NUNCA se reusa, aunque el préstamo se cancele (regla DGII).
/// </summary>
public class NcfSecuencia
{
    // De qué estancia es esta secuencia (030) NO se guarda aquí a propósito: este
    // modelo describe la autorización de la DGII (prefijo, rango, vencimiento),
    // y quién la usa es un asunto de ruteo. El modo viaja como parámetro en cada
    // operación del repositorio. Además, FAControl.Models no referencia a
    // FAControl.Common —es la capa sin dependencias— así que ModoApp no llega aquí.
    public int Id { get; set; }
    public string Prefijo { get; set; } = "B02";
    /// <summary>Dígitos de la secuencia: 8 para NCF tradicional, 10 para e-CF.</summary>
    public int Largo { get; set; } = 8;
    /// <summary>Próximo número a asignar.</summary>
    public long Proxima { get; set; } = 1;
    /// <summary>Último número autorizado (inclusive). NULL = sin tope conocido.</summary>
    public long? FinRango { get; set; }
    /// <summary>Vencimiento de la autorización. NULL = sin vencimiento conocido.</summary>
    public DateOnly? Vencimiento { get; set; }
    public bool Activo { get; set; } = true;

    /// <summary>NCF ya formateado para un número dado, ej. B02 + 00000012.</summary>
    public string Formatear(long numero) =>
        $"{Prefijo}{numero.ToString().PadLeft(Largo, '0')}";

    /// <summary>Cuántos números quedan disponibles (null = sin tope conocido).</summary>
    public long? Restantes => FinRango is { } fin ? Math.Max(0, fin - Proxima + 1) : null;

    public bool EstaVencida(DateOnly hoy) => Vencimiento is { } v && hoy > v;

    public bool EstaAgotada => FinRango is { } fin && Proxima > fin;

    /// <summary>
    /// Forma de un comprobante de la DGII: una letra de serie, dos digitos de
    /// tipo y la secuencia (8 en el NCF tradicional, 10 en el e-CF). Se aceptan
    /// de 6 a 12 digitos para no pelear con rangos raros de autorizacion.
    /// Los formatos anteriores a 2018 (serie A de 19 digitos) quedan fuera a
    /// proposito: no se emiten mas y admitirlos volveria ambiguo el prefijo.
    /// </summary>
    private static readonly Regex FormaNcf = new(@"^([A-Z]\d{2})(\d{6,12})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Parte un NCF digitado a mano en prefijo + numero + largo, para poder
    /// adoptarlo como secuencia predeterminada (pedido del cliente 2026-09-03).
    /// Null si el texto no tiene forma de comprobante: en ese caso no se toca
    /// nada, porque adivinar la numeracion de un libro de ventas es peor que
    /// no hacer nada.
    /// </summary>
    public static (string Prefijo, long Numero, int Largo)? Descomponer(string? ncf)
    {
        if (string.IsNullOrWhiteSpace(ncf))
            return null;
        var m = FormaNcf.Match(ncf.Trim().ToUpperInvariant());
        if (!m.Success)
            return null;
        var digitos = m.Groups[2].Value;
        return long.TryParse(digitos, out var numero)
            ? (m.Groups[1].Value, numero, digitos.Length)
            : null;
    }
}
