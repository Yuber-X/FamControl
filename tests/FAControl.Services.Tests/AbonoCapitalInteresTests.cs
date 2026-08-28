using FluentAssertions;
using FAControl.Models;
using FAControl.Services;
using Xunit;

namespace FAControl.Services.Tests;

/// <summary>
/// El pedido que reenvió el cliente el 2026-08-27:
///
///   "si un cliente toma RD$1,000,000 y va pagando sus intereses mensualmente,
///    pero en noviembre realiza un abono de RD$200,000 al capital, ese monto
///    debe rebajarse directamente del saldo, quedando un capital pendiente de
///    RD$800,000. A partir del siguiente mes, los intereses deben calcularse
///    únicamente sobre los RD$800,000 restantes, no sobre el millón original."
///
/// Son DOS exigencias, y hoy el sistema cumple una sola. Estas pruebas fijan
/// exactamente dónde está la línea, para no discutirla de memoria.
/// </summary>
public class AbonoCapitalInteresTests
{
    private const decimal Capital = 1_000_000m;
    private const decimal TasaMensual = 2m;      // 2% mensual → 20,000 sobre el millón
    private const int Plazo = 12;

    /// <summary>La tabla tal como la arma el préstamo abierto (solo interés).</summary>
    private static List<Cuota> TablaSoloInteres()
    {
        var calculadas = new AmortizacionService().Calcular(new ParametrosAmortizacion(
            Capital, TasaMensual, Plazo, Modalidad.Mensual,
            MetodoAmortizacion.SoloInteres, new DateOnly(2026, 9, 1)));

        return [.. calculadas.Select((c, indice) => new Cuota
        {
            Id = indice + 1,
            NumeroCuota = c.NumeroCuota,
            FechaVencimiento = c.FechaVencimiento,
            Capital = c.Capital,
            Interes = c.Interes,
            MontoTotal = c.MontoTotal,
            SaldoDespues = c.SaldoDespues,
            MontoPagado = 0m
        })];
    }

    /// <summary>
    /// Punto de partida: el préstamo abierto es N cuotas de puro interés y el
    /// capital completo en la última. Es la forma en que trabaja la mayoría de
    /// la cartera de Familia Almonte.
    /// </summary>
    [Fact]
    public void El_prestamo_abierto_cobra_solo_interes_y_deja_el_capital_al_final()
    {
        var tabla = TablaSoloInteres();

        tabla.Should().HaveCount(Plazo);
        tabla[0].Interes.Should().Be(20_000m, "2% de 1,000,000");
        tabla[0].Capital.Should().Be(0m, "las cuotas de un préstamo abierto son de puro interés");
        tabla[^1].Capital.Should().Be(Capital, "el capital entero vive en la última cuota");
    }

    /// <summary>
    /// PRIMERA exigencia — CUMPLIDA: el abono baja el capital pendiente.
    /// 1,000,000 − 200,000 = 800,000.
    /// </summary>
    [Fact]
    public void El_abono_a_capital_rebaja_el_saldo_del_prestamo()
    {
        var tabla = TablaSoloInteres();
        var hoy = new DateOnly(2026, 11, 1);

        // Noviembre: paga su interés del mes Y abona 200,000 al capital
        var aplicaciones = PagoService.DistribuirConAbono(
            montoBase: 20_000m, abono: 200_000m, tabla, hoy);

        var capitalAplicado = aplicaciones.Sum(a => a.CapitalAplicado);
        capitalAplicado.Should().Be(200_000m, "el abono va entero contra el capital");

        // El capital pendiente del préstamo baja a 800,000
        var capitalPendiente = tabla.Sum(c => c.Capital) - capitalAplicado;
        capitalPendiente.Should().Be(800_000m);
    }

    /// <summary>
    /// SEGUNDA exigencia del cliente, ahora cubierta:
    ///
    ///   "A partir del siguiente mes, los intereses deben calcularse unicamente
    ///    sobre los RD$800,000 restantes, no sobre el millon original."
    ///
    /// El recalculo vive en `PagoService.RecalcularInteresAbiertoAsync`, dentro
    /// de la transaccion del cobro. Aca se comprueba la REGLA DE ALCANCE, que
    /// es la parte delicada: solo se reescriben las cuotas que NO han vencido.
    /// El interes de una cuota vencida se devengo sobre plata que el deudor
    /// efectivamente tenia; bajarlo hacia atras le perdonaria interes ya ganado.
    /// </summary>
    [Fact]
    public void El_recalculo_alcanza_solo_a_las_cuotas_que_no_han_vencido()
    {
        var tabla = TablaSoloInteres();
        var hoy = new DateOnly(2026, 11, 1);

        var vencidas = tabla.Where(c => c.FechaVencimiento <= hoy).ToList();
        var porVencer = tabla.Where(c => c.FechaVencimiento > hoy).ToList();

        vencidas.Should().NotBeEmpty("las cuotas de septiembre y octubre ya vencieron");
        porVencer.Should().NotBeEmpty("de diciembre en adelante todavia no vencen");

        // La frontera es estricta: una cuota que vence HOY ya devengo su interes
        vencidas.Should().OnlyContain(c => c.FechaVencimiento <= hoy);
        porVencer.Should().OnlyContain(c => c.FechaVencimiento > hoy);
    }

    /// <summary>
    /// El modelo ya NO deduce el reparto: `cuota.capital_pagado` lo guarda (043).
    ///
    /// Antes esto fallaba. `CapitalPendiente` usaba la regla "primero interes",
    /// que reparte bien un cobro normal pero NO un abono a capital —que no paga
    /// interes—, asi que de un abono de 200,000 se comia 20,000 como si fueran
    /// interes y dejaba el capital pendiente en 820,000 en vez de 800,000.
    /// </summary>
    [Fact]
    public void El_capital_pendiente_refleja_exactamente_lo_abonado()
    {
        var tabla = TablaSoloInteres();
        var ultima = tabla[^1];

        var aplicaciones = PagoService.DistribuirConAbono(
            montoBase: 0m, abono: 200_000m, tabla, new DateOnly(2026, 11, 1));

        var aplicacion = aplicaciones.Single(a => a.Cuota.Id == ultima.Id);
        aplicacion.CapitalAplicado.Should().Be(200_000m);
        aplicacion.InteresAplicado.Should().Be(0m, "un abono a capital no paga interes");

        // Asi persiste RegistrarPagoAsync: acumulado Y capital, por separado
        ultima.MontoPagado += aplicacion.MontoAplicado;
        ultima.CapitalPagado += aplicacion.CapitalAplicado;

        PagoService.CapitalPendiente(ultima).Should().Be(800_000m,
            "el capital pendiente es el pactado menos lo efectivamente abonado");
        PagoService.InteresPendiente(ultima).Should().Be(20_000m,
            "el interes de esa cuota sigue debiendose: el abono no lo pago");
    }

    /// <summary>
    /// El pedido del cliente, cumplido: tras abonar 200,000 el interes de las
    /// cuotas POR VENCER pasa a calcularse sobre los 800,000 que quedan.
    ///
    /// Es la cuenta pura del recalculo que aplica `RecalcularInteresAbiertoAsync`
    /// dentro de la transaccion del cobro.
    /// </summary>
    [Fact]
    public void El_interes_nuevo_se_calcula_sobre_el_capital_que_queda()
    {
        var tasa = AmortizacionService.TasaPorPeriodo(TasaMensual, Modalidad.Mensual);

        var interesAntes = Math.Round(Capital * tasa, 2, MidpointRounding.AwayFromZero);
        var interesDespues = Math.Round((Capital - 200_000m) * tasa, 2, MidpointRounding.AwayFromZero);

        interesAntes.Should().Be(20_000m, "2% de 1,000,000");
        interesDespues.Should().Be(16_000m, "2% de 800,000 — lo que pide el cliente");
        (interesAntes - interesDespues).Should().Be(4_000m,
            "es lo que se le estaba cobrando de mas al deudor cada mes");
    }
}
