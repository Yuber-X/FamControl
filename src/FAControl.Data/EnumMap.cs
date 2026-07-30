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
        Modalidad.PagoUnico => "pago_unico",
        _ => throw new ArgumentOutOfRangeException(nameof(m))
    };

    public static Modalidad ModalidadDeDb(string valor) => valor switch
    {
        "diaria" => Modalidad.Diaria,
        "semanal" => Modalidad.Semanal,
        "quincenal" => Modalidad.Quincenal,
        "mensual" => Modalidad.Mensual,
        "pago_unico" => Modalidad.PagoUnico,
        _ => throw new ArgumentOutOfRangeException(nameof(valor), valor, "Modalidad desconocida en BD.")
    };

    public static string ADb(MetodoAmortizacion m) => m switch
    {
        MetodoAmortizacion.Frances => "frances",
        MetodoAmortizacion.CuotaFija => "cuota_fija",
        MetodoAmortizacion.SoloInteres => "solo_interes",
        _ => throw new ArgumentOutOfRangeException(nameof(m))
    };

    public static MetodoAmortizacion MetodoDeDb(string valor) => valor switch
    {
        "frances" => MetodoAmortizacion.Frances,
        "cuota_fija" => MetodoAmortizacion.CuotaFija,
        "solo_interes" => MetodoAmortizacion.SoloInteres,
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

    public static string ADb(TipoVehiculo t) => t switch
    {
        TipoVehiculo.Sedan => "sedan",
        TipoVehiculo.Suv => "suv",
        TipoVehiculo.Jeepeta => "jeepeta",
        TipoVehiculo.Camioneta => "camioneta",
        TipoVehiculo.Camion => "camion",
        TipoVehiculo.Motor => "motor",
        TipoVehiculo.Otro => "otro",
        _ => throw new ArgumentOutOfRangeException(nameof(t))
    };

    public static TipoVehiculo TipoVehiculoDeDb(string valor) => valor switch
    {
        "sedan" => TipoVehiculo.Sedan,
        "suv" => TipoVehiculo.Suv,
        "jeepeta" => TipoVehiculo.Jeepeta,
        "camioneta" => TipoVehiculo.Camioneta,
        "camion" => TipoVehiculo.Camion,
        "motor" => TipoVehiculo.Motor,
        "otro" => TipoVehiculo.Otro,
        _ => throw new ArgumentOutOfRangeException(nameof(valor), valor, "Tipo de vehículo desconocido en BD.")
    };

    public static string ADb(EstadoVehiculo e) => e switch
    {
        EstadoVehiculo.Disponible => "disponible",
        EstadoVehiculo.Reservado => "reservado",
        EstadoVehiculo.Vendido => "vendido",
        EstadoVehiculo.Alquilado => "alquilado",
        EstadoVehiculo.Baja => "baja",
        _ => throw new ArgumentOutOfRangeException(nameof(e))
    };

    public static EstadoVehiculo EstadoVehiculoDeDb(string valor) => valor switch
    {
        "disponible" => EstadoVehiculo.Disponible,
        "reservado" => EstadoVehiculo.Reservado,
        "vendido" => EstadoVehiculo.Vendido,
        "alquilado" => EstadoVehiculo.Alquilado,
        "baja" => EstadoVehiculo.Baja,
        _ => throw new ArgumentOutOfRangeException(nameof(valor), valor, "Estado de vehículo desconocido en BD.")
    };

    public static string ADb(EstadoAlquiler e) => e switch
    {
        EstadoAlquiler.Activo => "activo",
        EstadoAlquiler.Finalizado => "finalizado",
        EstadoAlquiler.Cancelado => "cancelado",
        _ => throw new ArgumentOutOfRangeException(nameof(e))
    };

    public static EstadoAlquiler EstadoAlquilerDeDb(string valor) => valor switch
    {
        "activo" => EstadoAlquiler.Activo,
        "finalizado" => EstadoAlquiler.Finalizado,
        "cancelado" => EstadoAlquiler.Cancelado,
        _ => throw new ArgumentOutOfRangeException(nameof(valor), valor, "Estado de alquiler desconocido en BD.")
    };

    // Financiamiento del dealer (016)

    public static string ADb(TipoVenta t) => t switch
    {
        TipoVenta.Contado => "contado",
        TipoVenta.Plazos => "plazos",
        TipoVenta.Separacion => "separacion",
        _ => throw new ArgumentOutOfRangeException(nameof(t))
    };

    public static TipoVenta TipoVentaDeDb(string valor) => valor switch
    {
        "contado" => TipoVenta.Contado,
        "plazos" => TipoVenta.Plazos,
        "separacion" => TipoVenta.Separacion,
        _ => throw new ArgumentOutOfRangeException(nameof(valor), valor, "Tipo de venta desconocido en BD.")
    };

    public static string ADb(EstadoPlazo e) => e switch
    {
        EstadoPlazo.Pendiente => "pendiente",
        EstadoPlazo.Pagado => "pagado",
        EstadoPlazo.Cancelado => "cancelado",
        _ => throw new ArgumentOutOfRangeException(nameof(e))
    };

    public static EstadoPlazo EstadoPlazoDeDb(string valor) => valor switch
    {
        "pendiente" => EstadoPlazo.Pendiente,
        "pagado" => EstadoPlazo.Pagado,
        "cancelado" => EstadoPlazo.Cancelado,
        _ => throw new ArgumentOutOfRangeException(nameof(valor), valor, "Estado de plazo desconocido en BD.")
    };

    // ---------- Expediente digital del contrato (018) ----------

    public static string ADb(TipoDocumento t) => t switch
    {
        TipoDocumento.Otro => "otro",
        TipoDocumento.FacturaEscaneada => "factura_escaneada",
        TipoDocumento.Contrato => "contrato",
        TipoDocumento.Identificacion => "identificacion",
        _ => throw new ArgumentOutOfRangeException(nameof(t))
    };

    public static TipoDocumento TipoDocumentoDeDb(string valor) => valor switch
    {
        "otro" => TipoDocumento.Otro,
        "factura_escaneada" => TipoDocumento.FacturaEscaneada,
        "contrato" => TipoDocumento.Contrato,
        "identificacion" => TipoDocumento.Identificacion,
        _ => throw new ArgumentOutOfRangeException(nameof(valor), valor, "Tipo de documento desconocido en BD.")
    };
}
