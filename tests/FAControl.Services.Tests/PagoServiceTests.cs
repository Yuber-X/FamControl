using FluentAssertions;
using FAControl.Models;
using FAControl.Services;
using Xunit;

namespace FAControl.Services.Tests;

/// <summary>
/// Pruebas de la lógica PURA de distribución de cobros (sin BD).
/// Escenario base: préstamo de 12,000 a 12 cuotas mensuales, interés simple
/// dominicano al 5% mensual → cada cuota: capital 1,000 + interés 600 = 1,600.
/// </summary>
public class PagoServiceTests
{
    /// <summary>
    /// `capitalPagado` refleja lo que un cobro real habria persistido (043): el
    /// reparto ya no se deduce con "primero interes", se guarda. Para un cobro
    /// normal da lo mismo que la deduccion vieja —de 800 sobre una cuota de
    /// 600 de interes, 200 van a capital—; la diferencia aparece solo en los
    /// abonos a capital, que no pagan interes.
    /// </summary>
    private static Cuota CrearCuota(int numero, decimal capital = 1_000m, decimal interes = 600m,
        decimal montoPagado = 0m, DateOnly? vencimiento = null,
        decimal? capitalPagado = null) => new()
    {
        Id = numero,
        PrestamoId = 1,
        NumeroCuota = numero,
        FechaVencimiento = vencimiento ?? new DateOnly(2026, 7, 1).AddMonths(numero - 1),
        Capital = capital,
        Interes = interes,
        MontoTotal = capital + interes,
        MontoPagado = montoPagado,
        CapitalPagado = capitalPagado ?? Math.Max(0m, montoPagado - interes),
        Estado = EstadoCuota.Pendiente
    };

    // ============================================================
    // Interés y capital pendientes (abonos aplican primero a interés)
    // ============================================================

    [Fact]
    public void InteresPendiente_SinAbonos_EsElInteresCompleto()
    {
        PagoService.InteresPendiente(CrearCuota(1)).Should().Be(600m);
    }

    [Fact]
    public void InteresPendiente_ConAbonoParcialMenorQueElInteres_EsLaDiferencia()
    {
        PagoService.InteresPendiente(CrearCuota(1, montoPagado: 250m)).Should().Be(350m);
    }

    [Fact]
    public void CapitalPendiente_ConAbonoQueCubrioElInteres_DescuentaSoloElExcedente()
    {
        // 800 pagados: 600 fueron interés, 200 capital → capital pendiente 800
        PagoService.CapitalPendiente(CrearCuota(1, montoPagado: 800m)).Should().Be(800m);
    }

    // ============================================================
    // Pago exacto
    // ============================================================

    [Fact]
    public void DistribuirPago_MontoExactoDeUnaCuota_LaDejaPagadaConDesgloseCorrecto()
    {
        var cuotas = new[] { CrearCuota(1), CrearCuota(2) };

        var aplicaciones = PagoService.DistribuirPago(1_600m, cuotas);

        aplicaciones.Should().HaveCount(1);
        aplicaciones[0].Cuota.NumeroCuota.Should().Be(1);
        aplicaciones[0].MontoAplicado.Should().Be(1_600m);
        aplicaciones[0].InteresAplicado.Should().Be(600m);
        aplicaciones[0].CapitalAplicado.Should().Be(1_000m);
        aplicaciones[0].QuedaPagada.Should().BeTrue();
    }

    // ============================================================
    // Abono parcial (primero interés, luego capital)
    // ============================================================

    [Fact]
    public void DistribuirPago_AbonoMenorQueElInteres_TodoVaAInteres()
    {
        var aplicaciones = PagoService.DistribuirPago(400m, new[] { CrearCuota(1) });

        aplicaciones[0].InteresAplicado.Should().Be(400m);
        aplicaciones[0].CapitalAplicado.Should().Be(0m);
        aplicaciones[0].QuedaPagada.Should().BeFalse();
    }

    [Fact]
    public void DistribuirPago_AbonoMayorQueElInteres_CubreInteresYElRestoACapital()
    {
        var aplicaciones = PagoService.DistribuirPago(1_000m, new[] { CrearCuota(1) });

        aplicaciones[0].InteresAplicado.Should().Be(600m);
        aplicaciones[0].CapitalAplicado.Should().Be(400m);
        aplicaciones[0].QuedaPagada.Should().BeFalse();
    }

    [Fact]
    public void DistribuirPago_SegundoAbonoTrasUnoAnterior_RespetaElInteresYaCubierto()
    {
        // Abono previo de 400 (todo interés) → quedan 200 de interés + 1,000 de capital
        var cuota = CrearCuota(1, montoPagado: 400m);

        var aplicaciones = PagoService.DistribuirPago(1_200m, new[] { cuota });

        aplicaciones[0].InteresAplicado.Should().Be(200m);
        aplicaciones[0].CapitalAplicado.Should().Be(1_000m);
        aplicaciones[0].QuedaPagada.Should().BeTrue();
    }

    // ============================================================
    // Adelanto (cascada a cuotas futuras)
    // ============================================================

    [Fact]
    public void DistribuirPago_AdelantoDeDosCuotasYMedia_CaeEnCascada()
    {
        var cuotas = new[] { CrearCuota(1), CrearCuota(2), CrearCuota(3), CrearCuota(4) };

        var aplicaciones = PagoService.DistribuirPago(4_000m, cuotas); // 1,600 + 1,600 + 800

        aplicaciones.Should().HaveCount(3);
        aplicaciones[0].QuedaPagada.Should().BeTrue();
        aplicaciones[1].QuedaPagada.Should().BeTrue();
        aplicaciones[2].MontoAplicado.Should().Be(800m);
        aplicaciones[2].InteresAplicado.Should().Be(600m);  // primero interés de la 3ra
        aplicaciones[2].CapitalAplicado.Should().Be(200m);
        aplicaciones[2].QuedaPagada.Should().BeFalse();
        aplicaciones.Sum(a => a.MontoAplicado).Should().Be(4_000m);
    }

    // ============================================================
    // Validaciones
    // ============================================================

    [Fact]
    public void DistribuirPago_MontoQueExcedeLaDeuda_Lanza()
    {
        var accion = () => PagoService.DistribuirPago(5_000m, new[] { CrearCuota(1) });
        accion.Should().Throw<ArgumentException>().WithMessage("*excede la deuda*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void DistribuirPago_MontoNoPositivo_Lanza(decimal monto)
    {
        var accion = () => PagoService.DistribuirPago(monto, new[] { CrearCuota(1) });
        accion.Should().Throw<ArgumentException>().WithMessage("*mayor que cero*");
    }

    [Fact]
    public void DistribuirPago_SinCuotasImpagas_Lanza()
    {
        var accion = () => PagoService.DistribuirPago(100m, Array.Empty<Cuota>());
        accion.Should().Throw<ArgumentException>().WithMessage("*cuotas pendientes*");
    }

    // ============================================================
    // Liquidación anticipada
    // ============================================================

    [Fact]
    public void CalcularLiquidacion_CuotaVencidaCompletaYFuturasSoloCapital()
    {
        var hoy = new DateOnly(2026, 7, 10);
        var cuotas = new[]
        {
            CrearCuota(1, vencimiento: new DateOnly(2026, 7, 1)),   // vencida → 1,600
            CrearCuota(2, vencimiento: new DateOnly(2026, 8, 1)),   // futura  → 1,000
            CrearCuota(3, vencimiento: new DateOnly(2026, 9, 1))    // futura  → 1,000
        };

        PagoService.CalcularLiquidacion(cuotas, hoy).Should().Be(3_600m);
    }

    [Fact]
    public void CalcularLiquidacion_CuotaQueVenceHoy_SeCobraCompleta()
    {
        var hoy = new DateOnly(2026, 7, 10);
        var cuotas = new[] { CrearCuota(1, vencimiento: hoy) };

        PagoService.CalcularLiquidacion(cuotas, hoy).Should().Be(1_600m);
    }

    [Fact]
    public void DistribuirLiquidacion_ExoneraElInteresDeCuotasFuturas()
    {
        var hoy = new DateOnly(2026, 7, 10);
        var cuotas = new[]
        {
            CrearCuota(1, vencimiento: new DateOnly(2026, 7, 1)),
            CrearCuota(2, vencimiento: new DateOnly(2026, 8, 1))
        };

        var aplicaciones = PagoService.DistribuirLiquidacion(cuotas, hoy);

        aplicaciones.Should().HaveCount(2);
        aplicaciones.Should().OnlyContain(a => a.QuedaPagada);

        aplicaciones[0].MontoAplicado.Should().Be(1_600m);
        aplicaciones[0].InteresExonerado.Should().Be(0m);

        aplicaciones[1].MontoAplicado.Should().Be(1_000m);
        aplicaciones[1].InteresAplicado.Should().Be(0m);
        aplicaciones[1].CapitalAplicado.Should().Be(1_000m);
        aplicaciones[1].InteresExonerado.Should().Be(600m);
    }

    [Fact]
    public void DistribuirLiquidacion_CuotaFuturaConAbonoPrevio_PagaSoloElCapitalRestante()
    {
        var hoy = new DateOnly(2026, 7, 10);
        // Abono previo de 800: cubrió 600 de interés + 200 de capital
        var cuota = CrearCuota(1, montoPagado: 800m, vencimiento: new DateOnly(2026, 8, 1));

        var aplicaciones = PagoService.DistribuirLiquidacion(new[] { cuota }, hoy);

        aplicaciones[0].MontoAplicado.Should().Be(800m);      // capital pendiente
        aplicaciones[0].InteresExonerado.Should().Be(0m);     // el interés ya estaba cubierto
        aplicaciones[0].QuedaPagada.Should().BeTrue();
    }

    [Fact]
    public void DistribuirLiquidacion_SumaIgualACalcularLiquidacion()
    {
        var hoy = new DateOnly(2026, 7, 10);
        var cuotas = new[]
        {
            CrearCuota(1, montoPagado: 400m, vencimiento: new DateOnly(2026, 6, 20)),
            CrearCuota(2, vencimiento: new DateOnly(2026, 7, 20)),
            CrearCuota(3, vencimiento: new DateOnly(2026, 8, 20))
        };

        var aplicaciones = PagoService.DistribuirLiquidacion(cuotas, hoy);

        aplicaciones.Sum(a => a.MontoAplicado)
            .Should().Be(PagoService.CalcularLiquidacion(cuotas, hoy));
    }

    // ============================================================
    // Abono a capital (cliente 2026-07-17)
    // ============================================================

    [Fact]
    public void DistribuirConAbono_paga_la_cuota_y_abona_capital_futuro()
    {
        // 5 cuotas de 1,600 (cap 1,000 + int 600). Base 1,600 paga la cuota 1;
        // abono 2,000 va al capital de las cuotas 2 y 3, exonerando su interés.
        var cuotas = Enumerable.Range(1, 5).Select(n => CrearCuota(n)).ToList();

        var apps = PagoService.DistribuirConAbono(1_600m, 2_000m, cuotas, new DateOnly(2026, 7, 15));

        // Cuota 1: pagada completa (1,600)
        var c1 = apps.Single(a => a.Cuota.NumeroCuota == 1);
        c1.MontoAplicado.Should().Be(1_600m);
        c1.QuedaPagada.Should().BeTrue();

        // Cuotas 2 y 3: solo capital (1,000 c/u), interés exonerado
        var c2 = apps.Single(a => a.Cuota.NumeroCuota == 2);
        c2.CapitalAplicado.Should().Be(1_000m);
        c2.InteresAplicado.Should().Be(0m);
        c2.InteresExonerado.Should().Be(600m);
        c2.QuedaPagada.Should().BeTrue();

        // El abono total aplicado a capital es 2,000
        apps.Where(a => a.Cuota.NumeroCuota > 1).Sum(a => a.CapitalAplicado).Should().Be(2_000m);
    }

    [Fact]
    public void DistribuirConAbono_sin_abono_es_igual_a_DistribuirPago()
    {
        var cuotas = Enumerable.Range(1, 3).Select(n => CrearCuota(n)).ToList();

        var conAbono = PagoService.DistribuirConAbono(1_600m, 0m, cuotas, new DateOnly(2026, 7, 15));
        var normal = PagoService.DistribuirPago(1_600m, cuotas);

        conAbono.Should().HaveCount(normal.Count);
        conAbono[0].MontoAplicado.Should().Be(normal[0].MontoAplicado);
    }

    [Fact]
    public void DistribuirConAbono_rechaza_abono_mayor_al_capital_pendiente()
    {
        var cuotas = Enumerable.Range(1, 2).Select(n => CrearCuota(n)).ToList();
        // capital total pendiente = 2,000; abono 5,000 lo excede
        var accion = () => PagoService.DistribuirConAbono(1_600m, 5_000m, cuotas, new DateOnly(2026, 7, 15));
        accion.Should().Throw<ArgumentException>().WithMessage("*excede el capital*");
    }

}
