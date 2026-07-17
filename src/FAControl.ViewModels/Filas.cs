using FAControl.Models;

namespace FAControl.ViewModels;

/// <summary>Fila de la tabla de préstamos (lista principal).</summary>
public record PrestamoFila(PrestamoResumen Resumen)
{
    public long Id => Resumen.Id;
    public string Codigo => Resumen.Codigo;
    public string ClienteNombre => Resumen.ClienteNombre;
    public decimal MontoCapital => Resumen.MontoCapital;
    public decimal SaldoPendiente => Resumen.SaldoPendiente;
    public string TasaTexto => $"{Resumen.TasaInteres:0.##}% mensual";
    public string ModalidadTexto => Textos.De(Resumen.Modalidad);
    public string ProgresoTexto => $"{Resumen.CuotasPagadas}/{Resumen.PlazoCuotas}";
    public string ProximoVencimientoTexto => Resumen.ProximoVencimiento?.ToString("dd/MM/yyyy") ?? "—";
    public EstadoPrestamo Estado => Resumen.Estado;
    public string EstadoTexto => Textos.De(Resumen.Estado);
}

/// <summary>Fila de la tabla de cuotas (detalle de préstamo y cobros) con su semáforo.</summary>
public record CuotaFila(Cuota Cuota, SemaforoCuota Semaforo)
{
    public int Numero => Cuota.NumeroCuota;
    public string FechaTexto => Cuota.FechaVencimiento.ToString("dd/MM/yyyy");
    public decimal Capital => Cuota.Capital;
    public decimal Interes => Cuota.Interes;
    public decimal MontoTotal => Cuota.MontoTotal;
    public decimal MontoPagado => Cuota.MontoPagado;
    public decimal SaldoPendiente => Cuota.SaldoPendiente;
    public decimal SaldoDespues => Cuota.SaldoDespues;
    public string SemaforoTexto => Textos.De(Semaforo);
    public bool EstaVencida => Semaforo is SemaforoCuota.Vencida or SemaforoCuota.EnMora;
}

/// <summary>Fila del preview de distribución de un cobro (pantalla Cobros).</summary>
public record AplicacionFila(AplicacionPago Aplicacion)
{
    public int NumeroCuota => Aplicacion.Cuota.NumeroCuota;
    public decimal Interes => Aplicacion.InteresAplicado;
    public decimal Capital => Aplicacion.CapitalAplicado;
    public decimal Monto => Aplicacion.MontoAplicado;
    public string ResultadoTexto => Aplicacion.QuedaPagada
        ? (Aplicacion.InteresExonerado > 0m ? "Pagada (interés exonerado)" : "Queda pagada")
        : "Abono parcial";
}

/// <summary>Fila del historial de pagos recientes.</summary>
public record PagoFila(PagoResumen Resumen)
{
    public string NumeroRecibo => Resumen.NumeroRecibo;
    public string FechaTexto => Common.FechaNegocio.AUtcLocal(Resumen.FechaPagoUtc).ToString("dd/MM/yyyy hh:mm tt");
    public string ClienteNombre => Resumen.ClienteNombre;
    public string PrestamoCodigo => Resumen.PrestamoCodigo;
    public string CuotaTexto => $"Cuota {Resumen.NumeroCuota}";
    public decimal MontoPagado => Resumen.MontoPagado;
    public string MetodoTexto => Textos.De(Resumen.MetodoPago);
}
