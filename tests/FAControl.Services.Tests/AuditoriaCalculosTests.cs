using System.Text;
using FluentAssertions;
using FAControl.Models;
using FAControl.Services;

namespace FAControl.Services.Tests;

/// <summary>
/// Barrido de invariantes sobre TODOS los métodos de amortización.
///
/// No prueba un caso puntual: recorre cientos de combinaciones de monto, tasa,
/// plazo y modalidad, y verifica las reglas que NUNCA pueden romperse, sin
/// importar el método ni los números. Un préstamo que no cumple alguna de estas
/// es plata mal contada.
/// </summary>
public class AuditoriaCalculosTests
{
    private readonly AmortizacionService _sut = new();
    private static readonly DateOnly PrimerPago = new(2026, 8, 8);

    private static readonly decimal[] Montos =
        [1_000m, 3_500m, 12_000m, 33_333.33m, 75_000m, 150_000m, 1_250_000m];

    private static readonly decimal[] Tasas =
        [0m, 0.5m, 1m, 3m, 5m, 10m, 20m, 35m];

    private static readonly int[] Plazos = [1, 2, 3, 6, 7, 12, 13, 18, 24, 36, 52, 100];

    private static readonly Modalidad[] Modalidades =
        [Modalidad.Mensual, Modalidad.Quincenal, Modalidad.Semanal, Modalidad.Diaria];

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

    /// <summary>
    /// Las reglas que valen para cualquier método y cualquier número:
    ///
    ///  1. El capital devuelto es EXACTAMENTE el prestado (ni un centavo de más
    ///     ni de menos). Si sobra, el cliente paga de más; si falta, queda
    ///     debiendo algo que nadie sabe cobrar.
    ///  2. El saldo termina en cero.
    ///  3. El saldo nunca sube: una cuota no puede dejar debiendo más que antes.
    ///  4. Ningún componente es negativo.
    ///  5. La cuota es capital + interés, siempre.
    ///  6. Todo importe tiene como mucho 2 decimales (es plata, no un promedio).
    /// </summary>
    [Theory]
    [MemberData(nameof(TodosLosMetodos))]
    public void CualquierPrestamo_CumpleLasReglasDeLaPlata(MetodoAmortizacion metodo)
    {
        var fallas = new StringBuilder();
        var casos = 0;

        foreach (var monto in Montos)
        foreach (var tasa in Tasas)
        foreach (var plazo in Plazos)
        foreach (var modalidad in Modalidades)
        {
            casos++;
            var p = new ParametrosAmortizacion(monto, tasa, plazo, modalidad, metodo, PrimerPago);
            var tabla = _sut.Calcular(p);
            var caso = $"{metodo} {monto:N2} @ {tasa}% × {plazo} {modalidad}";

            if (tabla.Sum(c => c.Capital) != monto)
                fallas.AppendLine($"{caso}: capital suma {tabla.Sum(c => c.Capital):N2}, debería ser {monto:N2}");

            if (tabla[^1].SaldoDespues != 0m)
                fallas.AppendLine($"{caso}: el saldo final quedó en {tabla[^1].SaldoDespues:N2}");

            for (var k = 1; k < tabla.Count; k++)
                if (tabla[k].SaldoDespues > tabla[k - 1].SaldoDespues)
                {
                    fallas.AppendLine($"{caso}: el saldo SUBIÓ en la cuota {k + 1} " +
                                      $"({tabla[k - 1].SaldoDespues:N2} → {tabla[k].SaldoDespues:N2})");
                    break;
                }

            foreach (var c in tabla)
            {
                if (c.Capital < 0m || c.Interes < 0m || c.SaldoDespues < 0m)
                {
                    fallas.AppendLine($"{caso}: cuota {c.NumeroCuota} tiene un valor negativo " +
                                      $"(capital {c.Capital:N2}, interés {c.Interes:N2}, saldo {c.SaldoDespues:N2})");
                    break;
                }
                if (c.MontoTotal != c.Capital + c.Interes)
                {
                    fallas.AppendLine($"{caso}: cuota {c.NumeroCuota} no cuadra " +
                                      $"({c.Capital:N2} + {c.Interes:N2} ≠ {c.MontoTotal:N2})");
                    break;
                }
                if (MasDeDosDecimales(c.Capital) || MasDeDosDecimales(c.Interes) ||
                    MasDeDosDecimales(c.MontoTotal) || MasDeDosDecimales(c.SaldoDespues))
                {
                    fallas.AppendLine($"{caso}: cuota {c.NumeroCuota} tiene más de 2 decimales");
                    break;
                }
            }
        }

        casos.Should().BeGreaterThan(2_000, "el barrido tiene que ser amplio de verdad");
        fallas.ToString().Should().BeEmpty();
    }

    /// <summary>Los totales del resumen tienen que ser la suma de la tabla, no otra cuenta.</summary>
    [Theory]
    [MemberData(nameof(TodosLosMetodos))]
    public void ElResumen_SiempreCuadraConLaTabla(MetodoAmortizacion metodo)
    {
        var fallas = new StringBuilder();

        foreach (var monto in Montos)
        foreach (var tasa in Tasas)
        foreach (var plazo in Plazos)
        {
            var tabla = _sut.Calcular(new ParametrosAmortizacion(
                monto, tasa, plazo, Modalidad.Mensual, metodo, PrimerPago));
            var r = _sut.Resumir(tabla);
            var caso = $"{metodo} {monto:N2} @ {tasa}% × {plazo}";

            if (r.Capital + r.InteresTotal != r.TotalAPagar)
                fallas.AppendLine($"{caso}: capital + interés ≠ total a pagar");
            if (r.TotalAPagar != tabla.Sum(c => c.MontoTotal))
                fallas.AppendLine($"{caso}: el total del resumen no es la suma de las cuotas");
            if (r.Capital != monto)
                fallas.AppendLine($"{caso}: el resumen dice capital {r.Capital:N2} y se prestaron {monto:N2}");
        }

        fallas.ToString().Should().BeEmpty();
    }

    /// <summary>
    /// Sin interés, el cliente devuelve EXACTAMENTE lo que le prestaron. Es la
    /// prueba más simple y la que deja en evidencia cualquier centavo fantasma.
    /// </summary>
    [Theory]
    [MemberData(nameof(TodosLosMetodos))]
    public void TasaCero_DevuelveExactamenteLoPrestado(MetodoAmortizacion metodo)
    {
        var fallas = new StringBuilder();

        foreach (var monto in Montos)
        foreach (var plazo in Plazos)
        {
            var tabla = _sut.Calcular(new ParametrosAmortizacion(
                monto, 0m, plazo, Modalidad.Mensual, metodo, PrimerPago));

            if (tabla.Sum(c => c.MontoTotal) != monto)
                fallas.AppendLine($"{metodo} {monto:N2} × {plazo}: paga " +
                                  $"{tabla.Sum(c => c.MontoTotal):N2} sin interés");
            if (tabla.Sum(c => c.Interes) != 0m)
                fallas.AppendLine($"{metodo} {monto:N2} × {plazo}: cobra interés con tasa 0");
        }

        fallas.ToString().Should().BeEmpty();
    }

    /// <summary>
    /// El francés promete cuota CONSTANTE: es su razón de ser. Varían solo la
    /// última (liquida el saldo exacto) y las que caen DESPUÉS de que la deuda
    /// ya quedó saldada — que con tasas altas y plazos muy largos puede pasar
    /// antes de tiempo, porque la cuota se redondea para arriba. Ahí la cuota
    /// baja porque ya no hay nada que cobrar, no porque el cálculo falle.
    /// </summary>
    [Fact]
    public void Frances_MantieneLaCuotaConstanteMientrasQuedeSaldo()
    {
        var fallas = new StringBuilder();

        foreach (var monto in Montos)
        foreach (var tasa in Tasas.Where(t => t > 0m))
        foreach (var plazo in Plazos.Where(n => n >= 3))
        {
            var tabla = _sut.Calcular(new ParametrosAmortizacion(
                monto, tasa, plazo, Modalidad.Mensual, MetodoAmortizacion.Frances, PrimerPago));

            var primera = tabla[0].MontoTotal;
            // Solo se juzgan las cuotas anteriores a la última que todavía dejan
            // saldo pendiente: son las que tienen que salir todas iguales.
            var distintas = tabla.Take(tabla.Count - 1)
                                 .Where(c => c.SaldoDespues > 0m && c.MontoTotal != primera)
                                 .ToList();
            if (distintas.Count > 0)
                fallas.AppendLine($"Francés {monto:N2} @ {tasa}% × {plazo}: " +
                                  $"{distintas.Count} cuotas distintas de la primera ({primera:N2})");
        }

        fallas.ToString().Should().BeEmpty();
    }

    /// <summary>
    /// Cuota fija (interés simple dominicano): el interés es el MISMO en todas
    /// las cuotas, porque siempre se calcula sobre el capital original. Es lo
    /// que el prestamista le dice al cliente ("son 600 de interés todos los
    /// meses"), así que una cuota con otro interés es un error visible.
    ///
    /// Hasta el 07/08/2026 la última absorbía el redondeo del interés total y en
    /// préstamos diarios chicos salía NEGATIVA.
    /// </summary>
    [Fact]
    public void CuotaFija_CobraElMismoInteresEnTodasLasCuotas()
    {
        var fallas = new StringBuilder();

        foreach (var monto in Montos)
        foreach (var tasa in Tasas)
        foreach (var plazo in Plazos)
        foreach (var modalidad in Modalidades)
        {
            var tabla = _sut.Calcular(new ParametrosAmortizacion(
                monto, tasa, plazo, modalidad, MetodoAmortizacion.CuotaFija, PrimerPago));

            var distintas = tabla.Where(c => c.Interes != tabla[0].Interes).ToList();
            if (distintas.Count > 0)
                fallas.AppendLine($"Cuota fija {monto:N2} @ {tasa}% × {plazo} {modalidad}: " +
                                  $"{distintas.Count} cuotas con interés distinto de {tabla[0].Interes:N2} " +
                                  $"(la peor: {distintas.Min(c => c.Interes):N2})");
        }

        fallas.ToString().Should().BeEmpty();
    }

    /// <summary>
    /// El caso exacto que destapó el barrido: un préstamo diario chico, de los
    /// que el cliente hace todos los días. Daba −0.03 de interés en la última.
    /// </summary>
    [Theory]
    [InlineData(500, 1, 60)]
    [InlineData(700, 0.5, 45)]
    [InlineData(1000, 0.5, 60)]
    [InlineData(1000, 0.5, 100)]
    public void PrestamoDiarioChico_NoCobraInteresNegativo(int monto, double tasa, int plazo)
    {
        var tabla = _sut.Calcular(new ParametrosAmortizacion(
            monto, (decimal)tasa, plazo, Modalidad.Diaria,
            MetodoAmortizacion.CuotaFija, PrimerPago));

        tabla.Should().OnlyContain(c => c.Interes >= 0m && c.Capital >= 0m);
        tabla[^1].Interes.Should().Be(tabla[0].Interes);
        tabla.Sum(c => c.Capital).Should().Be(monto);
    }

    /// <summary>
    /// Modo "para bobos": el usuario dice cuánto le devuelven y el sistema saca
    /// la tasa. Volviendo a calcular con esa tasa hay que llegar al mismo total.
    /// </summary>
    [Fact]
    public void ModoMontoFinal_LaTasaQueCalculaReproduceElTotalPedido()
    {
        var fallas = new StringBuilder();

        foreach (var monto in new[] { 10_000m, 50_000m, 150_000m })
        foreach (var factor in new[] { 1.10m, 1.25m, 1.5m, 2m, 3m })
        foreach (var plazo in new[] { 1, 6, 12, 24 })
        foreach (var metodo in new[] { MetodoAmortizacion.CuotaFija, MetodoAmortizacion.Frances })
        {
            var objetivo = Math.Round(monto * factor, 2, MidpointRounding.AwayFromZero);
            var tasa = _sut.TasaMensualParaTotal(monto, objetivo, plazo, Modalidad.Mensual, metodo);

            var total = _sut.Calcular(new ParametrosAmortizacion(
                monto, tasa, plazo, Modalidad.Mensual, metodo, PrimerPago)).Sum(c => c.MontoTotal);

            // Tolerancia: la tasa se guarda con 4 decimales, así que el total
            // reconstruido no puede ser exacto al centavo en montos grandes.
            var desvio = Math.Abs(total - objetivo);
            var tolerancia = Math.Max(1m, monto * 0.0002m);
            if (desvio > tolerancia)
                fallas.AppendLine($"{metodo} {monto:N2} × {plazo} → objetivo {objetivo:N2}, " +
                                  $"tasa {tasa}% da {total:N2} (desvío {desvio:N2})");
        }

        fallas.ToString().Should().BeEmpty();
    }

    /// <summary>
    /// Pago único: UNA cuota, pase lo que pase el plazo que se haya escrito, y
    /// el interés se aplica una sola vez.
    /// </summary>
    [Theory]
    [MemberData(nameof(TodosLosMetodos))]
    public void PagoUnico_EsSiempreUnaSolaCuota(MetodoAmortizacion metodo)
    {
        var fallas = new StringBuilder();

        foreach (var monto in Montos)
        foreach (var tasa in Tasas)
        foreach (var plazo in Plazos)
        {
            var tabla = _sut.Calcular(new ParametrosAmortizacion(
                monto, tasa, plazo, Modalidad.PagoUnico, metodo, PrimerPago));

            if (tabla.Count != 1)
                fallas.AppendLine($"{metodo} pago único con plazo {plazo}: dio {tabla.Count} cuotas");
            else if (tabla[0].Capital != monto ||
                     tabla[0].Interes != Math.Round(monto * tasa / 100m, 2, MidpointRounding.AwayFromZero))
                fallas.AppendLine($"{metodo} pago único {monto:N2} @ {tasa}%: " +
                                  $"capital {tabla[0].Capital:N2}, interés {tabla[0].Interes:N2}");
        }

        fallas.ToString().Should().BeEmpty();
    }

    private static bool MasDeDosDecimales(decimal v) =>
        v != Math.Round(v, 2, MidpointRounding.AwayFromZero);
}
