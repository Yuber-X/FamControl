// Portado de POS-500 el 2026-07-30 al integrar el punto de venta a la suite.
// Cambios respecto del original: usa ConexionPos500 (base pos500_db, aparte de
// facontrol_db) y el SesionActual / la auditoria compartidos de FAControl.
using FAControl.Common;
using FAControl.Models.Pos;

namespace FAControl.Data.Pos;

/// <summary>Mapeo C# ↔ ENUMs de MySQL (nunca cadenas mágicas sueltas).</summary>
internal static class EnumMap
{
    public static string ADb(MetodoPagoFactura metodo) => metodo switch
    {
        MetodoPagoFactura.Efectivo => "efectivo",
        MetodoPagoFactura.Tarjeta => "tarjeta",
        MetodoPagoFactura.Transferencia => "transferencia",
        MetodoPagoFactura.Mixto => "mixto",
        _ => throw new ArgumentOutOfRangeException(nameof(metodo))
    };

    public static MetodoPagoFactura MetodoPagoDeDb(string valor) => valor switch
    {
        "efectivo" => MetodoPagoFactura.Efectivo,
        "tarjeta" => MetodoPagoFactura.Tarjeta,
        "transferencia" => MetodoPagoFactura.Transferencia,
        "mixto" => MetodoPagoFactura.Mixto,
        _ => throw new ArgumentOutOfRangeException(nameof(valor), valor, "metodo_pago desconocido")
    };

    public static string ADb(EstadoFactura estado) => estado switch
    {
        EstadoFactura.Emitida => "emitida",
        EstadoFactura.Anulada => "anulada",
        _ => throw new ArgumentOutOfRangeException(nameof(estado))
    };

    public static EstadoFactura EstadoFacturaDeDb(string valor) => valor switch
    {
        "emitida" => EstadoFactura.Emitida,
        "anulada" => EstadoFactura.Anulada,
        _ => throw new ArgumentOutOfRangeException(nameof(valor), valor, "estado desconocido")
    };
}
