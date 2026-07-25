namespace FAControl.Models;

/// <summary>
/// Secuencia de comprobantes fiscales autorizada por la DGII (012_ncf.sql).
/// Prefijo = serie + tipo (ej. "B02" tradicional, "E32" e-CF). La parte
/// numérica se rellena con ceros a la izquierda hasta <see cref="Largo"/>.
/// Un NCF consumido NUNCA se reusa, aunque el préstamo se cancele (regla DGII).
/// </summary>
public class NcfSecuencia
{
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
}
