using FAControl.Models;

namespace FAControl.ViewModels;

/// <summary>Textos en español para los enums del dominio (UI y recibos).</summary>
public static class Textos
{
    public static string De(Modalidad m) => m switch
    {
        Modalidad.Diaria => "Diaria",
        Modalidad.Semanal => "Semanal",
        Modalidad.Quincenal => "Quincenal",
        Modalidad.Mensual => "Mensual",
        _ => m.ToString()
    };

    public static string De(MetodoAmortizacion m) => m switch
    {
        MetodoAmortizacion.CuotaFija => "Interés fijo (dominicano)",
        MetodoAmortizacion.Frances => "Sistema francés (sobre saldo)",
        _ => m.ToString()
    };

    public static string De(EstadoPrestamo e) => e switch
    {
        EstadoPrestamo.Activo => "Activo",
        EstadoPrestamo.Pagado => "Pagado",
        EstadoPrestamo.Cancelado => "Cancelado",
        _ => e.ToString()
    };

    public static string De(SemaforoCuota s) => s switch
    {
        SemaforoCuota.AlDia => "Al día",
        SemaforoCuota.PorVencer => "Por vencer",
        SemaforoCuota.Vencida => "Vencida",
        SemaforoCuota.EnMora => "En mora",
        SemaforoCuota.Pagada => "Pagada",
        SemaforoCuota.Cancelada => "Cancelada",
        _ => s.ToString()
    };

    public static string De(MetodoPago m) => m switch
    {
        MetodoPago.Efectivo => "Efectivo",
        MetodoPago.Transferencia => "Transferencia",
        MetodoPago.Cheque => "Cheque",
        MetodoPago.Otro => "Otro",
        _ => m.ToString()
    };
}

/// <summary>Opción de ComboBox con valor tipado + texto en español.</summary>
public record Opcion<T>(T Valor, string Texto)
{
    public override string ToString() => Texto;
}
