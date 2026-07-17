using FAControl.Models;

namespace FAControl.Services;

/// <summary>
/// Cálculo de tablas de amortización. Ver docs/AMORTIZATION.md para la matemática completa.
///
/// Convención de tasa (decisión 2026-07-10, corregible con el cliente):
/// la tasa se ingresa SIEMPRE como porcentaje MENSUAL y se convierte a tasa
/// por período según la modalidad con factores comerciales simples:
/// mensual ÷1, quincenal ÷2, semanal ÷4, diaria ÷30.
///
/// Este servicio nunca persiste — retorna valores y el llamador decide guardar.
/// </summary>
public class AmortizacionService
{
    /// <summary>Convierte la tasa mensual (%) a la tasa del período de pago, como fracción decimal.</summary>
    public static decimal TasaPorPeriodo(decimal tasaMensualPorciento, Modalidad modalidad)
    {
        var divisor = modalidad switch
        {
            Modalidad.Mensual => 1m,
            Modalidad.Quincenal => 2m,
            Modalidad.Semanal => 4m,
            Modalidad.Diaria => 30m,
            _ => throw new ArgumentOutOfRangeException(nameof(modalidad))
        };
        return tasaMensualPorciento / 100m / divisor;
    }

    /// <summary>Calcula la tabla de amortización completa según el método indicado.</summary>
    public IReadOnlyList<CuotaCalculada> Calcular(ParametrosAmortizacion p)
    {
        Validar(p);
        return p.Metodo switch
        {
            MetodoAmortizacion.CuotaFija => CalcularCuotaFija(p),
            MetodoAmortizacion.Frances => CalcularFrances(p),
            _ => throw new ArgumentOutOfRangeException(nameof(p.Metodo))
        };
    }

    /// <summary>Totales de la tabla para las tarjetas del resumen (Cuota fija, Total a pagar, Interés total, Capital).</summary>
    public ResumenAmortizacion Resumir(IReadOnlyList<CuotaCalculada> tabla)
    {
        if (tabla.Count == 0)
            throw new ArgumentException("La tabla de amortización está vacía.", nameof(tabla));

        var totalAPagar = tabla.Sum(c => c.MontoTotal);
        var interesTotal = tabla.Sum(c => c.Interes);
        var capital = tabla.Sum(c => c.Capital);
        // La cuota "fija" representativa es la primera (la última puede variar por ajuste de redondeo)
        return new ResumenAmortizacion(tabla[0].MontoTotal, totalAPagar, interesTotal, capital);
    }

    private static void Validar(ParametrosAmortizacion p)
    {
        if (p.MontoCapital <= 0m)
            throw new ArgumentException("El monto del capital debe ser mayor que cero.");
        if (p.TasaInteresMensual < 0m)
            throw new ArgumentException("La tasa de interés no puede ser negativa.");
        if (p.PlazoCuotas <= 0)
            throw new ArgumentException("El plazo debe ser de al menos 1 cuota.");
    }

    /// <summary>
    /// Interés simple dominicano ("cuota fija"): el interés de cada cuota se calcula
    /// sobre el capital ORIGINAL, no sobre el saldo. Es la práctica habitual de los
    /// prestamistas independientes en RD.
    ///
    ///   interésPorCuota = P × i          (i = tasa por período)
    ///   capitalPorCuota = P / n
    ///   cuota           = capitalPorCuota + interésPorCuota
    ///
    /// Redondeo: cada componente se redondea a 2 decimales (AwayFromZero) y la
    /// ÚLTIMA cuota absorbe la diferencia para que las sumas cuadren exactas.
    /// </summary>
    private static List<CuotaCalculada> CalcularCuotaFija(ParametrosAmortizacion p)
    {
        var i = TasaPorPeriodo(p.TasaInteresMensual, p.Modalidad);
        var n = p.PlazoCuotas;

        // Totales exactos, redondeados una sola vez (regla: redondear al final)
        var interesTotal = Math.Round(p.MontoCapital * i * n, 2, MidpointRounding.AwayFromZero);

        var capitalPorCuota = Math.Round(p.MontoCapital / n, 2, MidpointRounding.AwayFromZero);
        var interesPorCuota = Math.Round(p.MontoCapital * i, 2, MidpointRounding.AwayFromZero);

        var tabla = new List<CuotaCalculada>(n);
        var capitalAcumulado = 0m;
        var interesAcumulado = 0m;

        for (var k = 1; k <= n; k++)
        {
            decimal capital, interes;
            if (k < n)
            {
                capital = capitalPorCuota;
                interes = interesPorCuota;
                capitalAcumulado += capital;
                interesAcumulado += interes;
            }
            else
            {
                // La última cuota absorbe la diferencia de redondeo
                capital = p.MontoCapital - capitalAcumulado;
                interes = interesTotal - interesAcumulado;
            }

            var saldoDespues = k < n ? p.MontoCapital - capitalAcumulado : 0m;
            tabla.Add(new CuotaCalculada(
                k,
                FechaDeCuota(p.FechaPrimerPago, p.Modalidad, k),
                capital,
                interes,
                capital + interes,
                saldoDespues));
        }

        return tabla;
    }

    /// <summary>
    /// Sistema francés: cuota constante, interés sobre saldo insoluto.
    ///
    ///   cuota = P × [i(1+i)^n] / [(1+i)^n − 1]     (si i = 0 → cuota = P/n)
    ///   interés_k = saldo × i
    ///   capital_k = cuota − interés_k
    ///
    /// Redondeo: cuota e interés de cada período a 2 decimales (AwayFromZero);
    /// la ÚLTIMA cuota liquida el saldo exacto restante.
    /// </summary>
    private static List<CuotaCalculada> CalcularFrances(ParametrosAmortizacion p)
    {
        var i = TasaPorPeriodo(p.TasaInteresMensual, p.Modalidad);
        var n = p.PlazoCuotas;

        decimal cuotaRedondeada;
        if (i == 0m)
        {
            cuotaRedondeada = Math.Round(p.MontoCapital / n, 2, MidpointRounding.AwayFromZero);
        }
        else
        {
            var factor = PotenciaDecimal(1m + i, n); // (1+i)^n
            var cuotaExacta = p.MontoCapital * i * factor / (factor - 1m);
            cuotaRedondeada = Math.Round(cuotaExacta, 2, MidpointRounding.AwayFromZero);
        }

        var tabla = new List<CuotaCalculada>(n);
        var saldo = p.MontoCapital;

        for (var k = 1; k <= n; k++)
        {
            var interes = Math.Round(saldo * i, 2, MidpointRounding.AwayFromZero);
            decimal capital, cuota;

            if (k < n)
            {
                capital = cuotaRedondeada - interes;
                cuota = cuotaRedondeada;
            }
            else
            {
                // Última cuota: liquida exactamente el saldo restante
                capital = saldo;
                cuota = capital + interes;
            }

            saldo -= capital;
            tabla.Add(new CuotaCalculada(
                k,
                FechaDeCuota(p.FechaPrimerPago, p.Modalidad, k),
                capital,
                interes,
                cuota,
                saldo));
        }

        return tabla;
    }

    /// <summary>Fecha de vencimiento de la cuota k (k inicia en 1; la cuota 1 vence en la fecha del primer pago).</summary>
    public static DateOnly FechaDeCuota(DateOnly primerPago, Modalidad modalidad, int numeroCuota)
    {
        var desplazamientos = numeroCuota - 1;
        return modalidad switch
        {
            Modalidad.Diaria => primerPago.AddDays(desplazamientos),
            Modalidad.Semanal => primerPago.AddDays(desplazamientos * 7),
            Modalidad.Quincenal => primerPago.AddDays(desplazamientos * 15),
            Modalidad.Mensual => primerPago.AddMonths(desplazamientos),
            _ => throw new ArgumentOutOfRangeException(nameof(modalidad))
        };
    }

    /// <summary>Potencia entera de un decimal sin pasar por double (evita pérdida de precisión).</summary>
    private static decimal PotenciaDecimal(decimal baseValor, int exponente)
    {
        var resultado = 1m;
        for (var j = 0; j < exponente; j++)
            resultado *= baseValor;
        return resultado;
    }
}
