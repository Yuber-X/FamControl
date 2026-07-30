using FluentAssertions;
using FAControl.Models;
using FAControl.Services;

namespace FAControl.Services.Tests;

public class AmortizacionServiceTests
{
    private readonly AmortizacionService _sut = new();
    private static readonly DateOnly PrimerPago = new(2026, 8, 8);

    private static ParametrosAmortizacion Params(
        decimal capital, decimal tasaMensual, int plazo,
        Modalidad modalidad = Modalidad.Mensual,
        MetodoAmortizacion metodo = MetodoAmortizacion.CuotaFija) =>
        new(capital, tasaMensual, plazo, modalidad, metodo, PrimerPago);

    // =========================================================
    // Conversión de tasa mensual a tasa por período
    // =========================================================

    [Theory]
    [InlineData(10, Modalidad.Mensual, 0.10)]
    [InlineData(10, Modalidad.Quincenal, 0.05)]
    [InlineData(10, Modalidad.Semanal, 0.025)]
    [InlineData(30, Modalidad.Diaria, 0.01)]
    [InlineData(0, Modalidad.Mensual, 0)]
    public void TasaPorPeriodo_convierte_con_factores_comerciales(
        decimal tasaMensual, Modalidad modalidad, decimal esperada)
    {
        AmortizacionService.TasaPorPeriodo(tasaMensual, modalidad).Should().Be(esperada);
    }

    // =========================================================
    // Interés simple dominicano (cuota_fija)
    // =========================================================

    [Fact]
    public void CuotaFija_10000_al_10_mensual_12_cuotas()
    {
        var tabla = _sut.Calcular(Params(10_000m, 10m, 12));

        tabla.Should().HaveCount(12);
        // Interés fijo sobre capital original: 10,000 × 10% = 1,000 por cuota
        tabla.Take(11).Should().OnlyContain(c => c.Interes == 1_000m && c.Capital == 833.33m);
        // Última cuota absorbe el ajuste de redondeo del capital
        tabla[^1].Capital.Should().Be(833.37m);
        tabla[^1].Interes.Should().Be(1_000m);

        tabla.Sum(c => c.Capital).Should().Be(10_000m);
        tabla.Sum(c => c.Interes).Should().Be(12_000m);
        tabla[^1].SaldoDespues.Should().Be(0m);
    }

    [Fact]
    public void CuotaFija_5000_30_cuotas_diarias_al_1pct_diario()
    {
        // Caso obligatorio del CLAUDE.md: 30% mensual → 1% diario
        var tabla = _sut.Calcular(Params(5_000m, 30m, 30, Modalidad.Diaria));

        tabla.Should().HaveCount(30);
        tabla.Should().OnlyContain(c => c.Interes == 50m); // 5,000 × 1%
        tabla.Sum(c => c.Capital).Should().Be(5_000m, "la suma de capitales debe ser el capital original");
        tabla.Sum(c => c.Interes).Should().Be(1_500m);
    }

    [Fact]
    public void CuotaFija_tasa_cero_reparte_solo_capital()
    {
        var tabla = _sut.Calcular(Params(9_000m, 0m, 6));

        tabla.Should().OnlyContain(c => c.Interes == 0m);
        tabla.Sum(c => c.Capital).Should().Be(9_000m);
        tabla.Should().OnlyContain(c => c.MontoTotal == 1_500m);
    }

    [Fact]
    public void CuotaFija_redondeo_ajusta_en_ultima_cuota()
    {
        // 1,000 / 3 = 333.33... → dos cuotas de 333.33 y la última de 333.34
        var tabla = _sut.Calcular(Params(1_000m, 0m, 3));

        tabla[0].Capital.Should().Be(333.33m);
        tabla[1].Capital.Should().Be(333.33m);
        tabla[2].Capital.Should().Be(333.34m);
        tabla.Sum(c => c.Capital).Should().Be(1_000m);
    }

    [Fact]
    public void CuotaFija_saldo_despues_decrece_hasta_cero()
    {
        var tabla = _sut.Calcular(Params(10_000m, 10m, 4));

        tabla[0].SaldoDespues.Should().Be(7_500m);
        tabla[1].SaldoDespues.Should().Be(5_000m);
        tabla[2].SaldoDespues.Should().Be(2_500m);
        tabla[3].SaldoDespues.Should().Be(0m);
    }

    // =========================================================
    // Sistema francés
    // =========================================================

    [Fact]
    public void Frances_10000_al_5_mensual_12_cuotas_cuota_exacta()
    {
        // Caso obligatorio del CLAUDE.md
        var tabla = _sut.Calcular(Params(10_000m, 5m, 12, metodo: MetodoAmortizacion.Frances));

        tabla[0].MontoTotal.Should().Be(1_128.25m);
        tabla[0].Interes.Should().Be(500m);       // 10,000 × 5%
        tabla[0].Capital.Should().Be(628.25m);
        tabla.Sum(c => c.Capital).Should().Be(10_000m);
        tabla[^1].SaldoDespues.Should().Be(0m);
    }

    [Fact]
    public void Frances_caso_del_mockup_75000_al_5_mensual_12_cuotas()
    {
        // Validación contra el mockup de "Nuevo préstamo" (Screenshot 5)
        var tabla = _sut.Calcular(Params(75_000m, 5m, 12, metodo: MetodoAmortizacion.Frances));

        tabla[0].MontoTotal.Should().Be(8_461.91m);
        tabla[0].Interes.Should().Be(3_750m);
        tabla[0].Capital.Should().Be(4_711.91m);
        tabla.Sum(c => c.Capital).Should().Be(75_000m);
    }

    [Fact]
    public void Frances_interes_decrece_y_capital_crece()
    {
        var tabla = _sut.Calcular(Params(10_000m, 5m, 12, metodo: MetodoAmortizacion.Frances));

        for (var k = 1; k < tabla.Count; k++)
        {
            tabla[k].Interes.Should().BeLessThan(tabla[k - 1].Interes);
            tabla[k].Capital.Should().BeGreaterThan(tabla[k - 1].Capital);
        }
    }

    [Fact]
    public void Frances_tasa_cero_equivale_a_reparto_lineal()
    {
        var tabla = _sut.Calcular(Params(6_000m, 0m, 4, metodo: MetodoAmortizacion.Frances));

        tabla.Should().OnlyContain(c => c.Interes == 0m);
        tabla.Sum(c => c.Capital).Should().Be(6_000m);
    }

    [Fact]
    public void Frances_ultima_cuota_liquida_saldo_exacto()
    {
        // Plazo primo y tasa incómoda para forzar residuos de redondeo
        var tabla = _sut.Calcular(Params(7_777.77m, 3.33m, 7, metodo: MetodoAmortizacion.Frances));

        tabla.Sum(c => c.Capital).Should().Be(7_777.77m);
        tabla[^1].SaldoDespues.Should().Be(0m);
        tabla[^1].MontoTotal.Should().Be(tabla[^1].Capital + tabla[^1].Interes);
    }

    // =========================================================
    // Fechas de vencimiento por modalidad
    // =========================================================

    [Fact]
    public void Fechas_mensuales_avanzan_por_mes_calendario()
    {
        var tabla = _sut.Calcular(Params(1_000m, 5m, 3));

        tabla[0].FechaVencimiento.Should().Be(new DateOnly(2026, 8, 8));
        tabla[1].FechaVencimiento.Should().Be(new DateOnly(2026, 9, 8));
        tabla[2].FechaVencimiento.Should().Be(new DateOnly(2026, 10, 8));
    }

    [Theory]
    [InlineData(Modalidad.Diaria, 1)]
    [InlineData(Modalidad.Semanal, 7)]
    [InlineData(Modalidad.Quincenal, 15)]
    public void Fechas_por_modalidad_avanzan_los_dias_correctos(Modalidad modalidad, int dias)
    {
        var tabla = _sut.Calcular(Params(1_000m, 5m, 2, modalidad));

        tabla[0].FechaVencimiento.Should().Be(PrimerPago);
        tabla[1].FechaVencimiento.Should().Be(PrimerPago.AddDays(dias));
    }

    [Fact]
    public void Fechas_mensuales_fin_de_mes_no_se_desbordan()
    {
        // 31 de enero + 1 mes → 28/29 de febrero, nunca 3 de marzo
        var p = Params(1_000m, 5m, 3) with { FechaPrimerPago = new DateOnly(2026, 1, 31) };
        var tabla = _sut.Calcular(p);

        tabla[1].FechaVencimiento.Should().Be(new DateOnly(2026, 2, 28));
        tabla[2].FechaVencimiento.Should().Be(new DateOnly(2026, 3, 31));
    }

    // =========================================================
    // Resumen y validaciones
    // =========================================================

    [Fact]
    public void Resumir_calcula_los_totales_de_las_tarjetas()
    {
        var tabla = _sut.Calcular(Params(75_000m, 5m, 12, metodo: MetodoAmortizacion.Frances));
        var resumen = _sut.Resumir(tabla);

        resumen.CuotaFija.Should().Be(8_461.91m);
        resumen.Capital.Should().Be(75_000m);
        resumen.TotalAPagar.Should().Be(resumen.Capital + resumen.InteresTotal);
    }

    [Theory]
    [InlineData(0, 5, 12)]      // capital cero
    [InlineData(-100, 5, 12)]   // capital negativo
    [InlineData(1000, -1, 12)]  // tasa negativa
    [InlineData(1000, 5, 0)]    // plazo cero
    public void Parametros_invalidos_lanzan_excepcion(decimal capital, decimal tasa, int plazo)
    {
        var act = () => _sut.Calcular(Params(capital, tasa, plazo));
        act.Should().Throw<ArgumentException>();
    }

    // =========================================================
    // Pago único (cliente 2026-07-17)
    // =========================================================

    [Fact]
    public void PagoUnico_produce_una_sola_cuota()
    {
        // 100k al 20%: el cliente devuelve 120k en un solo pago
        var tabla = _sut.Calcular(Params(100_000m, 20m, 1, Modalidad.PagoUnico));

        tabla.Should().HaveCount(1);
        tabla[0].Capital.Should().Be(100_000m);
        tabla[0].Interes.Should().Be(20_000m);
        tabla[0].MontoTotal.Should().Be(120_000m);
        tabla[0].SaldoDespues.Should().Be(0m);
    }

    [Fact]
    public void PagoUnico_ignora_el_plazo_pedido()
    {
        // Aunque se pidan 12 cuotas, pago único es SIEMPRE una sola
        var tabla = _sut.Calcular(Params(50_000m, 10m, 12, Modalidad.PagoUnico));
        tabla.Should().HaveCount(1);
    }

    [Fact]
    public void PagoUnico_vence_en_la_fecha_acordada()
    {
        var tabla = _sut.Calcular(Params(50_000m, 10m, 1, Modalidad.PagoUnico));
        tabla[0].FechaVencimiento.Should().Be(PrimerPago);
    }

    // =========================================================
    // Modo "para bobos": monto + monto final -> tasa
    // =========================================================

    [Fact]
    public void TasaMensualParaTotal_pago_unico_es_exacta()
    {
        // Presta 100k, quiere recibir 130k en un pago -> 30% mensual
        var tasa = _sut.TasaMensualParaTotal(100_000m, 130_000m, 1,
            Modalidad.PagoUnico, MetodoAmortizacion.CuotaFija);

        tasa.Should().BeApproximately(30m, 0.01m);
    }

    [Fact]
    public void TasaMensualParaTotal_es_el_inverso_de_Calcular()
    {
        // Si calculo la tasa para un total y luego amortizo con esa tasa,
        // el total debe volver a dar (a menos de unos centavos por redondeo).
        var tasa = _sut.TasaMensualParaTotal(80_000m, 100_000m, 6,
            Modalidad.Mensual, MetodoAmortizacion.CuotaFija);

        var total = _sut.Calcular(Params(80_000m, tasa, 6)).Sum(c => c.MontoTotal);
        total.Should().BeApproximately(100_000m, 1m);
    }

    [Fact]
    public void TasaMensualParaTotal_sin_interes_es_cero()
    {
        var tasa = _sut.TasaMensualParaTotal(50_000m, 50_000m, 4,
            Modalidad.Mensual, MetodoAmortizacion.CuotaFija);
        tasa.Should().Be(0m);
    }

    [Fact]
    public void TasaMensualParaTotal_rechaza_monto_final_menor_al_prestado()
    {
        var accion = () => _sut.TasaMensualParaTotal(50_000m, 40_000m, 4,
            Modalidad.Mensual, MetodoAmortizacion.CuotaFija);
        accion.Should().Throw<ArgumentException>();
    }

    // =========================================================
    // Prestamo ABIERTO (solo interes) — cliente 2026-07-29
    // =========================================================

    /// <summary>
    /// Richard Ovalles, del listado real: 1,000,000 al 2% mensual, "cuotas
    /// abierto, abono a capital 0, total a pagar 20,000". Doce meses de puro
    /// interes y el capital entero al final.
    /// </summary>
    [Fact]
    public void SoloInteres_reproduce_un_prestamo_abierto_de_la_cartera_real()
    {
        var tabla = _sut.Calcular(Params(1_000_000m, 2m, 12,
            metodo: MetodoAmortizacion.SoloInteres));

        tabla.Should().HaveCount(12);
        // Todas las cuotas menos la ultima: solo interes, el capital no se mueve
        tabla.Take(11).Should().OnlyContain(c => c.Capital == 0m);
        tabla.Take(11).Should().OnlyContain(c => c.Interes == 20_000m);
        tabla.Take(11).Should().OnlyContain(c => c.MontoTotal == 20_000m);
        tabla.Take(11).Should().OnlyContain(c => c.SaldoDespues == 1_000_000m);

        // La ultima liquida el capital
        tabla[^1].Capital.Should().Be(1_000_000m);
        tabla[^1].Interes.Should().Be(20_000m);
        tabla[^1].MontoTotal.Should().Be(1_020_000m);
        tabla[^1].SaldoDespues.Should().Be(0m);

        // El capital vuelve entero y el interes es el pactado, mes a mes
        tabla.Sum(c => c.Capital).Should().Be(1_000_000m);
        tabla.Sum(c => c.Interes).Should().Be(240_000m);
    }

    /// <summary>Dionicio: 1,100,000 al 1.5% → 16,500 mensuales, tal cual el papel.</summary>
    [Theory]
    [InlineData(1_100_000, 1.5, 16_500)]     // Dionicio
    [InlineData(500_000, 2, 10_000)]         // Miguel Angel
    [InlineData(400_000, 3, 12_000)]         // Grabiel
    [InlineData(300_000, 2.5, 7_500)]        // Wendy Yocasta
    [InlineData(200_000, 2, 4_000)]          // Mariana Sanchez
    public void SoloInteres_da_el_interes_mensual_que_dice_el_listado(
        decimal capital, decimal tasa, decimal interesEsperado)
    {
        var tabla = _sut.Calcular(Params(capital, tasa, 6,
            metodo: MetodoAmortizacion.SoloInteres));

        tabla[0].MontoTotal.Should().Be(interesEsperado);
        tabla.Take(5).Should().OnlyContain(c => c.MontoTotal == interesEsperado);
    }

    /// <summary>Sin interes, el abierto es simplemente devolver el capital al final.</summary>
    [Fact]
    public void SoloInteres_con_tasa_cero_solo_devuelve_el_capital_al_final()
    {
        var tabla = _sut.Calcular(Params(50_000m, 0m, 4,
            metodo: MetodoAmortizacion.SoloInteres));

        tabla.Take(3).Should().OnlyContain(c => c.MontoTotal == 0m);
        tabla[^1].MontoTotal.Should().Be(50_000m);
        tabla.Sum(c => c.Interes).Should().Be(0m);
    }

    /// <summary>El resumen muestra como "cuota" el pago de interes, que es lo que cobra.</summary>
    [Fact]
    public void SoloInteres_el_resumen_muestra_el_pago_de_interes()
    {
        var tabla = _sut.Calcular(Params(400_000m, 2m, 6,
            metodo: MetodoAmortizacion.SoloInteres));

        var resumen = _sut.Resumir(tabla);

        resumen.CuotaFija.Should().Be(8_000m);
        resumen.Capital.Should().Be(400_000m);
        resumen.InteresTotal.Should().Be(48_000m);
        resumen.TotalAPagar.Should().Be(448_000m);
    }
}
