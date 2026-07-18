using System.Globalization;
using FAControl.Models;

namespace FAControl.ViewModels;

/// <summary>Textos en español para los enums del dominio (UI y recibos).</summary>
public static class Textos
{
    /// <summary>Cultura del negocio. Fija: no depende de la config de Windows del cliente.</summary>
    public static readonly CultureInfo CulturaRd = CultureInfo.GetCultureInfo("es-DO");

    // Los separadores van ESCAPADOS ('/' y ':'). Sin escapar son comodines que
    // .NET reemplaza por los de la cultura de Windows: la misma app mostraría
    // 17/07/2026 en un binding XAML (que usa en-US) y 17-07-2026 en un
    // ToString de C# (que usa la cultura de la máquina). Escapados, la fecha
    // se ve IGUAL en cualquier PC.
    public const string FormatoFecha = @"dd'/'MM'/'yyyy";
    public const string FormatoFechaHora = @"dd'/'MM'/'yyyy hh':'mm tt";
    public const string FormatoFechaHoraSegundos = @"dd'/'MM'/'yyyy hh':'mm':'ss tt";

    public static string De(Modalidad m) => m switch
    {
        Modalidad.Diaria => "Diaria",
        Modalidad.Semanal => "Semanal",
        Modalidad.Quincenal => "Quincenal",
        Modalidad.Mensual => "Mensual",
        Modalidad.PagoUnico => "Pago único",
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

    public static string De(TipoVehiculo t) => t switch
    {
        TipoVehiculo.Sedan => "Sedán",
        TipoVehiculo.Suv => "SUV",
        TipoVehiculo.Jeepeta => "Jeepeta",
        TipoVehiculo.Camioneta => "Camioneta",
        TipoVehiculo.Camion => "Camión",
        TipoVehiculo.Motor => "Motor",
        TipoVehiculo.Otro => "Otro",
        _ => t.ToString()
    };

    public static string De(EstadoVehiculo e) => e switch
    {
        EstadoVehiculo.Disponible => "Disponible",
        EstadoVehiculo.Reservado => "Reservado",
        EstadoVehiculo.Vendido => "Vendido",
        EstadoVehiculo.Alquilado => "Alquilado",
        EstadoVehiculo.Baja => "Baja",
        _ => e.ToString()
    };

    public static string De(EstadoAlquiler e) => e switch
    {
        EstadoAlquiler.Activo => "Activo",
        EstadoAlquiler.Finalizado => "Finalizado",
        EstadoAlquiler.Cancelado => "Cancelado",
        _ => e.ToString()
    };
}

/// <summary>Opción de ComboBox con valor tipado + texto en español.</summary>
public record Opcion<T>(T Valor, string Texto)
{
    public override string ToString() => Texto;
}
