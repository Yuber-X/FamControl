using System.Text;
using FluentAssertions;
using FAControl.Models;
using FAControl.Services;

namespace FAControl.Services.Tests;

/// <summary>
/// Barrido de invariantes sobre el REPARTO de un pago entre las cuotas.
///
/// Por qué hace falta. El reparto ya produjo dos errores de plata que ninguna
/// prueba de ejemplo agarró: la regla "primero interés" se comía una cuota de
/// interés de cada abono a capital (2026-08-27), y el saldo del recibo restaba
/// el cobro dos veces. Los dos aparecieron con números concretos que a nadie se
/// le había ocurrido probar.
///
/// Igual que la auditoría de amortización, esto no prueba un caso: recorre
/// cientos de combinaciones de tabla y monto, y verifica las reglas que NUNCA
/// pueden romperse. Si una falla, es plata mal contada.
/// </summary>
public class AuditoriaRepartoDePagosTests
{
    private static readonly AmortizacionService Amortizacion = new();
    private static readonly DateOnly PrimerPago = new(2026, 8, 8);
    private static readonly DateOnly Hoy = new(2026, 8, 8);

    private static readonly decimal[] Montos = [1_000m, 12_000m, 33_333.33m, 250_000m];
    private static readonly decimal[] Tasas = [0m, 1m, 5m, 10m, 35m];
    private static readonly int[] Plazos = [1, 3, 6, 12, 24];

    private static readonly MetodoAmortizacion[] Metodos =
        [MetodoAmortizacion.CuotaFija, MetodoAmortizacion.Frances,
         MetodoAmortizacion.SoloInteres, MetodoAmortizacion.CapitalDiferido];

    public static TheoryData<MetodoAmortizacion> TodosLosMetodos()
    {
        var datos = new TheoryData<MetodoAmortizacion>();
        foreach (var m in Metodos)
            datos.Add(m);
        return datos;
    }

    /// <summary>Una tabla de cuotas impagas, como la que sale de la base.</summary>
    private static List<Cuota> Tabla(decimal monto, decimal tasa, int plazo,
        MetodoAmortizacion metodo)
    {
        var calculadas = Amortizacion.Calcular(new ParametrosAmortizacion(
            monto, tasa, plazo, Modalidad.Mensual, metodo, PrimerPago));
        return [.. calculadas.Select(c => new Cuota
        {
            Id = c.NumeroCuota,
            NumeroCuota = c.NumeroCuota,
            FechaVencimiento = c.FechaVencimiento,
            Capital = c.Capital,
            Interes = c.Interes,
            MontoTotal = c.MontoTotal,
            SaldoDespues = c.SaldoDespues
        })];
    }

    // ==================================================================

    /// <summary>
    /// Las reglas que valen para cualquier tabla y cualquier monto:
    ///
    ///  1. Lo repartido suma EXACTAMENTE lo que se pagó. Ni un centavo de más
    ///     (el cliente pagaría de menos) ni de menos (se perdería plata).
    ///  2. Ningún importe negativo. Un interés o un capital negativo es un
    ///     recibo que dice que el negocio le debe al cliente.
    ///  3. Dentro de cada cuota, interés + capital = lo aplicado a esa cuota.
    ///  4. Nunca se le aplica a una cuota más de lo que debe.
    ///  5. El interés aplicado nunca supera el interés pendiente de esa cuota:
    ///     ahí estaba el error del 2026-08-27.
    ///  6. Las cuotas se cobran en orden, de la más vieja a la más nueva.
    ///  7. "Queda pagada" solo si de verdad se cubrió todo su pendiente. Es la
    ///     palabra que se imprime en el recibo ("Pagado" / "Abonado parcial").
    /// </summary>
    [Theory]
    [MemberData(nameof(TodosLosMetodos))]
    public void CualquierReparto_CumpleLasReglasDeLaPlata(MetodoAmortizacion metodo)
    {
        var fallas = new StringBuilder();

        foreach (var monto in Montos)
        foreach (var tasa in Tasas)
        foreach (var plazo in Plazos)
        {
            var cuotas = Tabla(monto, tasa, plazo, metodo);
            var deuda = cuotas.Sum(c => c.SaldoPendiente);

            // Desde un centavo hasta la deuda entera, pasando por montos que
            // caen justo en el medio de una cuota.
            foreach (var pago in MontosDePrueba(cuotas, deuda))
            {
                var caso = $"{metodo} {monto:N0}@{tasa}%x{plazo} pagando {pago:N2}";
                var aplicaciones = PagoService.DistribuirPago(pago, cuotas);

                if (aplicaciones.Sum(a => a.MontoAplicado) != pago)
                    fallas.AppendLine($"{caso}: lo repartido ({aplicaciones.Sum(a => a.MontoAplicado):N2}) no suma el pago");

                foreach (var a in aplicaciones)
                {
                    if (a.MontoAplicado < 0m || a.InteresAplicado < 0m || a.CapitalAplicado < 0m)
                        fallas.AppendLine($"{caso}: importe negativo en la cuota {a.Cuota.NumeroCuota}");
                    if (a.InteresAplicado + a.CapitalAplicado != a.MontoAplicado)
                        fallas.AppendLine($"{caso}: interés+capital no da lo aplicado en la cuota {a.Cuota.NumeroCuota}");
                    if (a.MontoAplicado > a.Cuota.SaldoPendiente)
                        fallas.AppendLine($"{caso}: se aplicó de más a la cuota {a.Cuota.NumeroCuota}");
                    if (a.InteresAplicado > PagoService.InteresPendiente(a.Cuota))
                        fallas.AppendLine($"{caso}: interés de más en la cuota {a.Cuota.NumeroCuota}");
                    if (a.QuedaPagada != (a.MontoAplicado == a.Cuota.SaldoPendiente))
                        fallas.AppendLine($"{caso}: 'queda pagada' miente en la cuota {a.Cuota.NumeroCuota}");
                }

                var numeros = aplicaciones.Select(a => a.Cuota.NumeroCuota).ToList();
                if (!numeros.SequenceEqual(numeros.OrderBy(n => n)))
                    fallas.AppendLine($"{caso}: las cuotas no se cobraron en orden");
            }
        }

        fallas.ToString().Should().BeEmpty();
    }

    /// <summary>
    /// Un centavo, la mitad de la primera cuota, la primera exacta, una y media,
    /// y la deuda entera. Son los bordes donde el reparto se rompe.
    /// </summary>
    private static IEnumerable<decimal> MontosDePrueba(List<Cuota> cuotas, decimal deuda)
    {
        if (deuda <= 0m)
            yield break;   // tabla sin nada que cobrar (0% + solo interés, p. ej.)

        yield return 0.01m;

        // La PRIMERA con algo que cobrar: con 0% y solo-interés las primeras
        // valen 0.00 y el reparto rechaza montos no positivos, con razón.
        var primera = cuotas.First(c => c.SaldoPendiente > 0m).SaldoPendiente;
        if (primera > 0.02m)
            yield return Math.Round(primera / 2m, 2, MidpointRounding.AwayFromZero);
        yield return primera;

        var siguiente = cuotas.Where(c => c.SaldoPendiente > 0m).Skip(1).FirstOrDefault();
        if (siguiente is not null)
        {
            var unaYMedia = primera + Math.Round(siguiente.SaldoPendiente / 2m, 2,
                MidpointRounding.AwayFromZero);
            if (unaYMedia <= deuda && unaYMedia > primera)
                yield return unaYMedia;
        }
        yield return deuda;
    }

    // ==================================================================

    /// <summary>
    /// Pagar la deuda entera de una vez deja TODAS las cuotas saldadas y no
    /// sobra ni falta un centavo. Es el cierre de un préstamo.
    /// </summary>
    [Theory]
    [MemberData(nameof(TodosLosMetodos))]
    public void PagarLaDeudaEntera_SaldaTodasLasCuotas(MetodoAmortizacion metodo)
    {
        var fallas = new StringBuilder();

        foreach (var monto in Montos)
        foreach (var tasa in Tasas)
        foreach (var plazo in Plazos)
        {
            var cuotas = Tabla(monto, tasa, plazo, metodo);
            var deuda = cuotas.Sum(c => c.SaldoPendiente);
            if (deuda <= 0m)
                continue;

            var aplicaciones = PagoService.DistribuirPago(deuda, cuotas);
            var caso = $"{metodo} {monto:N0}@{tasa}%x{plazo}";

            if (aplicaciones.Count != cuotas.Count(c => c.SaldoPendiente > 0m))
                fallas.AppendLine($"{caso}: quedaron cuotas sin tocar");
            if (aplicaciones.Any(a => !a.QuedaPagada))
                fallas.AppendLine($"{caso}: alguna cuota quedó sin saldar");
            if (aplicaciones.Sum(a => a.InteresAplicado) != cuotas.Sum(c => c.Interes))
                fallas.AppendLine($"{caso}: el interés cobrado no es el de la tabla");
            if (aplicaciones.Sum(a => a.CapitalAplicado) != cuotas.Sum(c => c.Capital))
                fallas.AppendLine($"{caso}: el capital cobrado no es el de la tabla");
        }

        fallas.ToString().Should().BeEmpty();
    }

    /// <summary>
    /// Pagar en dos veces deja lo mismo que pagar de una. Si no, el orden de
    /// los cobros cambiaría cuánto termina pagando el cliente.
    /// </summary>
    [Theory]
    [MemberData(nameof(TodosLosMetodos))]
    public void PagarEnDosVeces_TerminaIgualQueDeUna(MetodoAmortizacion metodo)
    {
        var fallas = new StringBuilder();

        foreach (var monto in Montos)
        foreach (var tasa in Tasas)
        foreach (var plazo in new[] { 3, 6, 12 })
        {
            var caso = $"{metodo} {monto:N0}@{tasa}%x{plazo}";
            var cuotas = Tabla(monto, tasa, plazo, metodo);
            var deuda = cuotas.Sum(c => c.SaldoPendiente);
            if (deuda <= 0.02m)
                continue;

            var mitad = Math.Round(deuda / 2m, 2, MidpointRounding.AwayFromZero);

            // Primer cobro
            foreach (var a in PagoService.DistribuirPago(mitad, cuotas))
            {
                a.Cuota.MontoPagado += a.MontoAplicado;
                a.Cuota.CapitalPagado += a.CapitalAplicado;
            }

            // Segundo cobro por lo que quede
            var resto = cuotas.Sum(c => c.SaldoPendiente);
            if (resto > 0m)
                foreach (var a in PagoService.DistribuirPago(resto, cuotas))
                {
                    a.Cuota.MontoPagado += a.MontoAplicado;
                    a.Cuota.CapitalPagado += a.CapitalAplicado;
                }

            if (cuotas.Sum(c => c.SaldoPendiente) != 0m)
                fallas.AppendLine($"{caso}: quedó saldo después de pagar todo en dos veces");
            if (cuotas.Sum(c => c.MontoPagado) != deuda)
                fallas.AppendLine($"{caso}: lo cobrado en dos veces no da la deuda");
            if (cuotas.Sum(c => c.CapitalPagado) != cuotas.Sum(c => c.Capital))
                fallas.AppendLine($"{caso}: el capital cobrado no cierra");
        }

        fallas.ToString().Should().BeEmpty();
    }

    // ==================================================================

    /// <summary>
    /// La liquidación anticipada: lo que se reparte tiene que ser EXACTAMENTE
    /// lo que se le dijo al cliente que iba a pagar. Si no, el número del
    /// mostrador y el del recibo no coinciden.
    /// </summary>
    [Theory]
    [MemberData(nameof(TodosLosMetodos))]
    public void LaLiquidacion_RepartEExactamenteLoQueSeCotizo(MetodoAmortizacion metodo)
    {
        var fallas = new StringBuilder();

        foreach (var monto in Montos)
        foreach (var tasa in Tasas)
        foreach (var plazo in Plazos)
        {
            var cuotas = Tabla(monto, tasa, plazo, metodo);
            var caso = $"{metodo} {monto:N0}@{tasa}%x{plazo}";

            if (cuotas.Sum(c => c.SaldoPendiente) <= 0m)
                continue;

            var cotizado = PagoService.CalcularLiquidacion(cuotas, Hoy);
            var aplicaciones = PagoService.DistribuirLiquidacion(cuotas, Hoy);

            if (aplicaciones.Sum(a => a.MontoAplicado) != cotizado)
                fallas.AppendLine($"{caso}: la liquidación reparte {aplicaciones.Sum(a => a.MontoAplicado):N2} y se cotizó {cotizado:N2}");
            if (aplicaciones.Any(a => a.MontoAplicado < 0m || a.InteresExonerado < 0m))
                fallas.AppendLine($"{caso}: importe negativo en la liquidación");
            if (aplicaciones.Any(a => !a.QuedaPagada))
                fallas.AppendLine($"{caso}: la liquidación dejó una cuota sin saldar");

            // Nunca puede costar MÁS que pagar todas las cuotas una por una:
            // liquidar anticipadamente exonera interés, no lo agrega.
            if (cotizado > cuotas.Sum(c => c.SaldoPendiente))
                fallas.AppendLine($"{caso}: liquidar sale más caro que pagar todo");
            // Y nunca menos que el capital: el capital prestado se devuelve entero.
            if (cotizado < cuotas.Sum(PagoService.CapitalPendiente))
                fallas.AppendLine($"{caso}: liquidar no devuelve todo el capital");
        }

        fallas.ToString().Should().BeEmpty();
    }

    /// <summary>
    /// El abono a capital: el monto base paga las cuotas y el abono baja capital
    /// exonerando su interés. Lo repartido tiene que sumar base + abono.
    /// </summary>
    [Theory]
    [MemberData(nameof(TodosLosMetodos))]
    public void ElAbonoACapital_RepartExactamenteBaseMasAbono(MetodoAmortizacion metodo)
    {
        var fallas = new StringBuilder();

        foreach (var monto in Montos)
        foreach (var tasa in Tasas)
        foreach (var plazo in new[] { 6, 12, 24 })
        {
            var cuotas = Tabla(monto, tasa, plazo, metodo);
            var caso = $"{metodo} {monto:N0}@{tasa}%x{plazo}";

            var baseCobro = cuotas.FirstOrDefault(c => c.SaldoPendiente > 0m)?.SaldoPendiente ?? 0m;
            if (baseCobro <= 0m)
                continue;
            // El abono solo puede caer sobre el capital que el cobro base NO
            // se llevó. Con solo-interés todo el capital vive en la ÚLTIMA
            // cuota, así que si el cobro base es esa cuota no queda nada que
            // abonar: ese caso se saltea, no es un error del reparto.
            var capitalDisponible = cuotas.Sum(PagoService.CapitalPendiente)
                - PagoService.DistribuirPago(baseCobro, cuotas).Sum(a => a.CapitalAplicado);
            if (capitalDisponible <= 0.02m)
                continue;
            var abono = Math.Round(capitalDisponible / 3m, 2, MidpointRounding.AwayFromZero);

            var aplicaciones = PagoService.DistribuirConAbono(baseCobro, abono, cuotas, Hoy);

            if (aplicaciones.Sum(a => a.MontoAplicado) != baseCobro + abono)
                fallas.AppendLine($"{caso}: reparte {aplicaciones.Sum(a => a.MontoAplicado):N2} y se pagó {baseCobro + abono:N2}");
            if (aplicaciones.Any(a => a.MontoAplicado < 0m || a.CapitalAplicado < 0m || a.InteresAplicado < 0m))
                fallas.AppendLine($"{caso}: importe negativo con abono");
            foreach (var a in aplicaciones)
                if (a.CapitalAplicado > PagoService.CapitalPendiente(a.Cuota))
                    fallas.AppendLine($"{caso}: se abonó más capital del que debe la cuota {a.Cuota.NumeroCuota}");
        }

        fallas.ToString().Should().BeEmpty();
    }
}
