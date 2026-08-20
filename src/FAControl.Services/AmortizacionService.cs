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
    /// <summary>
    /// Convierte la tasa mensual (%) a la tasa del período de pago, como fracción
    /// decimal. Pago único aplica la tasa UNA vez (divisor 1, como mensual).
    /// </summary>
    public static decimal TasaPorPeriodo(decimal tasaMensualPorciento, Modalidad modalidad)
    {
        var divisor = modalidad switch
        {
            Modalidad.Mensual => 1m,
            Modalidad.PagoUnico => 1m,
            Modalidad.Quincenal => 2m,
            Modalidad.Semanal => 4m,
            Modalidad.Diaria => 30m,
            _ => throw new ArgumentOutOfRangeException(nameof(modalidad))
        };
        return tasaMensualPorciento / 100m / divisor;
    }

    /// <summary>Pago único = SIEMPRE una sola cuota, sin importar el plazo pedido.</summary>
    private static int PlazoEfectivo(ParametrosAmortizacion p) =>
        p.Modalidad == Modalidad.PagoUnico ? 1 : p.PlazoCuotas;

    /// <summary>Calcula la tabla de amortización completa según el método indicado.</summary>
    public IReadOnlyList<CuotaCalculada> Calcular(ParametrosAmortizacion p)
    {
        Validar(p);
        // Con una sola cuota los dos métodos coinciden (capital entero + interés
        // de un período), así que da igual cuál se use.
        if (p.Modalidad == Modalidad.PagoUnico)
            return CalcularCuotaFija(p);

        return p.Metodo switch
        {
            MetodoAmortizacion.CuotaFija => CalcularCuotaFija(p),
            MetodoAmortizacion.Frances => CalcularFrances(p),
            MetodoAmortizacion.SoloInteres => CalcularSoloInteres(p),
            MetodoAmortizacion.CapitalDiferido => CalcularCapitalDiferido(p),
            _ => throw new ArgumentOutOfRangeException(nameof(p.Metodo))
        };
    }

    /// <summary>
    /// Cuota en la que EMPIEZA a cobrarse capital cuando el usuario no la elige
    /// (modo automático del método diferido, pedido del cliente 2026-08-06).
    ///
    /// Se usa un TERCIO del plazo como gracia, que es lo que da la cuenta del
    /// ejemplo que mandó el cliente: 18 cuotas → 6 de interés fijo → arranca en
    /// la 7. Es una SUGERENCIA: el formulario la muestra y el usuario puede
    /// escribir cualquier otra (modo manual).
    /// </summary>
    public static int CuotaInicioCapitalSugerida(int plazoCuotas)
    {
        if (plazoCuotas <= 1)
            return 1;
        var gracia = plazoCuotas / 3;                  // 18 → 6
        return Math.Clamp(gracia + 1, 2, plazoCuotas); // 18 → 7
    }

    /// <summary>
    /// Modo "para bobos" (cliente 2026-07-17): dado el capital y el TOTAL que el
    /// cliente devolverá, calcula la tasa mensual implícita. Es el inverso de
    /// Calcular: se busca la tasa cuyo total a pagar es el monto final pedido.
    ///
    /// El total a pagar crece de forma monótona con la tasa, así que se resuelve
    /// por bisección: funciona igual para cuota fija que para francés, y para
    /// pago único (una cuota). Devuelve el % mensual redondeado a 4 decimales.
    /// </summary>
    public decimal TasaMensualParaTotal(decimal capital, decimal montoFinal, int plazo,
        Modalidad modalidad, MetodoAmortizacion metodo)
    {
        if (capital <= 0m)
            throw new ArgumentException("El monto a prestar debe ser mayor que cero.");
        if (montoFinal < capital)
            throw new ArgumentException("El monto final no puede ser menor que el monto prestado.");
        if (montoFinal == capital)
            return 0m;   // sin interés

        decimal Total(decimal tasa) =>
            Calcular(new ParametrosAmortizacion(capital, tasa, plazo, modalidad, metodo,
                new DateOnly(2000, 1, 1))).Sum(c => c.MontoTotal);

        decimal baja = 0m, alta = 1m;
        // Ensancha el techo hasta que el total supere el objetivo (tasas altas
        // de prestamista informal pueden ser enormes)
        while (Total(alta) < montoFinal && alta < 1_000m)
            alta *= 2m;

        // ~60 iteraciones bastan para converger a menos de un centavo
        for (var i = 0; i < 60; i++)
        {
            var medio = (baja + alta) / 2m;
            if (Total(medio) < montoFinal)
                baja = medio;
            else
                alta = medio;
        }
        return Math.Round((baja + alta) / 2m, 4, MidpointRounding.AwayFromZero);
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

        // Modo manual del método diferido: el usuario escribe la cuota donde
        // arranca el capital, así que puede escribir cualquier cosa.
        if (p.Metodo == MetodoAmortizacion.CapitalDiferido &&
            p.Modalidad != Modalidad.PagoUnico &&
            p.CuotaInicioCapital is { } inicio)
        {
            if (inicio < 1)
                throw new ArgumentException(
                    "La cuota donde empieza a cobrarse el capital no puede ser menor que 1.");
            if (inicio > p.PlazoCuotas)
                throw new ArgumentException(
                    $"El capital tiene que empezar a cobrarse en alguna de las {p.PlazoCuotas} cuotas. " +
                    $"Si empieza en la {inicio}, no se cobraría nunca.");
        }
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
    /// Redondeo: el INTERÉS es el mismo en todas las cuotas, porque en este
    /// método se calcula siempre sobre el capital original — es la definición
    /// del interés simple, y así es como el prestamista lo dice ("son 600 de
    /// interés todos los meses"). Solo el CAPITAL absorbe el redondeo en la
    /// última cuota, que es donde P/n casi nunca da justo.
    ///
    /// ANTES (corregido el 07/08/2026): el interés total se redondeaba aparte y
    /// la última cuota absorbía TAMBIÉN esa diferencia. Cuando el interés por
    /// cuota era de centavos y redondeaba para arriba, lo acumulado superaba al
    /// total y la última cuota salía con INTERÉS NEGATIVO. Pasaba con préstamos
    /// diarios chicos, que son pan de cada día: RD$500 al 1% a 60 días daba
    /// −0.03 en la última.
    /// </summary>
    private static List<CuotaCalculada> CalcularCuotaFija(ParametrosAmortizacion p)
    {
        var i = TasaPorPeriodo(p.TasaInteresMensual, p.Modalidad);
        var n = PlazoEfectivo(p);

        var capitalPorCuota = Math.Round(p.MontoCapital / n, 2, MidpointRounding.AwayFromZero);
        var interesPorCuota = Math.Round(p.MontoCapital * i, 2, MidpointRounding.AwayFromZero);

        var tabla = new List<CuotaCalculada>(n);
        var capitalAcumulado = 0m;

        for (var k = 1; k <= n; k++)
        {
            // El interés no cambia nunca: es P × i, la misma cuenta todos los períodos.
            var interes = interesPorCuota;
            decimal capital;
            if (k < n)
            {
                capital = capitalPorCuota;
                capitalAcumulado += capital;
            }
            else
            {
                // La última cuota absorbe la diferencia de redondeo del capital
                capital = p.MontoCapital - capitalAcumulado;
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
        var n = PlazoEfectivo(p);

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
                // El abono NUNCA puede pasar del saldo que queda.
                // Corregido el 07/08/2026: la cuota se redondea para arriba, así
                // que con tasas altas y plazos largos el préstamo se saldaba
                // ANTES de la última cuota y el código seguía restando capital.
                // El saldo se iba a negativo (1,000 al 10% × 100 cuotas llegaba
                // a −135.69) y la última cuota terminaba con capital negativo,
                // como si el prestamista tuviera que devolver plata.
                capital = Math.Min(cuotaRedondeada - interes, saldo);
                cuota = capital + interes;
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

    /// <summary>
    /// Préstamo ABIERTO (cliente 2026-07-29): el cliente paga SOLO el interés
    /// cada período y el capital queda abierto.
    ///
    ///   interés_k = P × i     para todas las cuotas (el capital no baja)
    ///   capital_k = 0         salvo la última, que lleva el capital entero
    ///
    /// Así es como el prestamista lleva de verdad estos préstamos: "interés
    /// 20,000 mensual, abono a capital abierto". El plazo N es el horizonte
    /// acordado; si el cliente sigue pagando interés, el préstamo se renueva y
    /// la última cuota se corre.
    ///
    /// El interés NO se redondea acumulando error: es el mismo número todas las
    /// veces, calculado una sola vez.
    /// </summary>
    private static List<CuotaCalculada> CalcularSoloInteres(ParametrosAmortizacion p)
    {
        var i = TasaPorPeriodo(p.TasaInteresMensual, p.Modalidad);
        var n = PlazoEfectivo(p);
        var interes = Math.Round(p.MontoCapital * i, 2, MidpointRounding.AwayFromZero);

        var tabla = new List<CuotaCalculada>(n);
        for (var k = 1; k <= n; k++)
        {
            var esUltima = k == n;
            var capital = esUltima ? p.MontoCapital : 0m;
            tabla.Add(new CuotaCalculada(
                k,
                FechaDeCuota(p.FechaPrimerPago, p.Modalidad, k),
                capital,
                interes,
                capital + interes,
                // El saldo no se mueve hasta que se salda: es la esencia del abierto
                esUltima ? 0m : p.MontoCapital));
        }

        return tabla;
    }

    /// <summary>
    /// Interés fijo primero, capital después (cliente 2026-08-06). Es el método
    /// con el que trabajaban "unos clientes de los viejos" y que no estaba en el
    /// sistema, así que no podían ni imprimirles la cotización.
    ///
    ///   cuotas 1 .. (inicio-1)   interés = P × i,  capital = 0   (el saldo no baja)
    ///   cuotas inicio .. n       capital = P / (n − inicio + 1)  (constante)
    ///                            interés = saldo × i             (sobre saldo, ya baja)
    ///
    /// El abono a capital es CONSTANTE, no la cuota: por eso el pago total va
    /// bajando mes a mes. Es amortización alemana con período de gracia.
    ///
    /// Redondeo: el capital por cuota se redondea una sola vez y la ÚLTIMA cuota
    /// se lleva lo que quede, para que el saldo cierre EXACTO en cero. Un saldo
    /// final de 0.01 obligaría al cliente a volver a pagar por un centavo.
    /// </summary>
    private static List<CuotaCalculada> CalcularCapitalDiferido(ParametrosAmortizacion p)
    {
        var i = TasaPorPeriodo(p.TasaInteresMensual, p.Modalidad);
        var n = PlazoEfectivo(p);
        var inicio = p.CuotaInicioCapital ?? CuotaInicioCapitalSugerida(n);

        var cuotasConCapital = n - inicio + 1;
        var capitalPorCuota = Math.Round(p.MontoCapital / cuotasConCapital, 2, MidpointRounding.AwayFromZero);

        var tabla = new List<CuotaCalculada>(n);
        var saldo = p.MontoCapital;

        for (var k = 1; k <= n; k++)
        {
            // El interés SIEMPRE se calcula sobre el saldo al empezar la cuota.
            // Durante la gracia el saldo es el capital entero, así que da el
            // mismo número todos los meses; después empieza a bajar solo.
            var interes = Math.Round(saldo * i, 2, MidpointRounding.AwayFromZero);

            decimal capital;
            if (k < inicio)
                capital = 0m;
            else if (k < n)
                capital = capitalPorCuota;
            else
                capital = saldo;   // la última liquida el saldo exacto

            saldo -= capital;
            tabla.Add(new CuotaCalculada(
                k,
                FechaDeCuota(p.FechaPrimerPago, p.Modalidad, k),
                capital,
                interes,
                capital + interes,
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
            // Pago único: la única cuota vence en la fecha acordada
            Modalidad.PagoUnico => primerPago,
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
