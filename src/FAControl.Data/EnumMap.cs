using FAControl.Models;

namespace FAControl.Data;

/// <summary>
/// Mapeo entre los enums de C# y los valores ENUM de MySQL.
/// Único lugar donde se traducen — los repositorios no escriben literales sueltos.
/// </summary>
internal static class EnumMap
{
    public static string ADb(Modalidad m) => m switch
    {
        Modalidad.Diaria => "diaria",
        Modalidad.Semanal => "semanal",
        Modalidad.Quincenal => "quincenal",
        Modalidad.Mensual => "mensual",
        _ => throw new ArgumentOutOfRangeException(nameof(m))
    };

    public static Modalidad ModalidadDeDb(string valor) => valor switch
    {
        "diaria" => Modalidad.Diaria,
        "semanal" => Modalidad.Semanal,
        "quincenal" => Modalidad.Quincenal,
        "mensual" => Modalidad.Mensual,
        _ => throw new ArgumentOutOfRangeException(nameof(valor), valor, "Modalidad desconocida en BD.")
    };

    public static string ADb(MetodoAmortizacion m) => m switch
    {
        MetodoAmortizacion.Frances => "frances",
        MetodoAmortizacion.CuotaFija => "cuota_fija",
        _ => throw new ArgumentOutOfRangeException(nameof(m))
    };

    public static MetodoAmortizacion MetodoDeDb(string valor) => valor switch
    {
        "frances" => MetodoAmortizacion.Frances,
        "cuota_fija" => MetodoAmortizacion.CuotaFija,
        _ => throw new ArgumentOutOfRangeException(nameof(valor), valor, "Método de amortización desconocido en BD.")
    };

    public static string ADb(EstadoPrestamo e) => e switch
    {
        EstadoPrestamo.Activo => "activo",
        EstadoPrestamo.Pagado => "pagado",
        EstadoPrestamo.Cancelado => "cancelado",
        _ => throw new ArgumentOutOfRangeException(nameof(e))
    };

    public static EstadoPrestamo EstadoPrestamoDeDb(string valor) => valor switch
    {
        "activo" => EstadoPrestamo.Activo,
        "pagado" => EstadoPrestamo.Pagado,
        "cancelado" => EstadoPrestamo.Cancelado,
        _ => throw new ArgumentOutOfRangeException(nameof(valor), valor, "Estado de préstamo desconocido en BD.")
    };

    public static string ADb(EstadoCuota e) => e switch
    {
        EstadoCuota.Pendiente => "pendiente",
        EstadoCuota.Pagada => "pagada",
        EstadoCuota.Vencida => "vencida",
        EstadoCuota.EnMora => "en_mora",
        EstadoCuota.Cancelada => "cancelada",
        _ => throw new ArgumentOutOfRangeException(nameof(e))
    };

    public static EstadoCuota EstadoCuotaDeDb(string valor) => valor switch
    {
        "pendiente" => EstadoCuota.Pendiente,
        "pagada" => EstadoCuota.Pagada,
        "vencida" => EstadoCuota.Vencida,
        "en_mora" => EstadoCuota.EnMora,
        "cancelada" => EstadoCuota.Cancelada,
        _ => throw new ArgumentOutOfRangeException(nameof(valor), valor, "Estado de cuota desconocido en BD.")
    };

    public static string ADb(MetodoPago m) => m switch
    {
        MetodoPago.Efectivo => "efectivo",
        MetodoPago.Transferencia => "transferencia",
        MetodoPago.Cheque => "cheque",
        MetodoPago.Otro => "otro",
        _ => throw new ArgumentOutOfRangeException(nameof(m))
    };

    public static MetodoPago MetodoPagoDeDb(string valor) => valor switch
    {
        "efectivo" => MetodoPago.Efectivo,
        "transferencia" => MetodoPago.Transferencia,
        "cheque" => MetodoPago.Cheque,
        "otro" => MetodoPago.Otro,
        _ => throw new ArgumentOutOfRangeException(nameof(valor), valor, "Método de pago desconocido en BD.")
    };
}
