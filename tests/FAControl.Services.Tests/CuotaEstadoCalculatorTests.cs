using FluentAssertions;
using FAControl.Models;
using FAControl.Services;

namespace FAControl.Services.Tests;

/// <summary>
/// Semáforo de cobros — cobertura 100% de ramas (requisito del CLAUDE.md del proyecto).
/// </summary>
public class CuotaEstadoCalculatorTests
{
    private static readonly DateOnly Hoy = new(2026, 7, 10);

    private static SemaforoCuota Calcular(
        int diasParaVencer,
        decimal montoTotal = 1_000m,
        decimal montoPagado = 0m,
        EstadoCuota estado = EstadoCuota.Pendiente) =>
        CuotaEstadoCalculator.Calcular(Hoy.AddDays(diasParaVencer), montoTotal, montoPagado, estado, Hoy);

    [Fact]
    public void Cancelada_tiene_prioridad_sobre_todo()
    {
        // Incluso pagada o en mora profunda, cancelada gana
        Calcular(-100, montoPagado: 1_000m, estado: EstadoCuota.Cancelada)
            .Should().Be(SemaforoCuota.Cancelada);
    }

    [Theory]
    [InlineData(1_000, 1_000)]  // pago exacto
    [InlineData(1_000, 1_200)]  // sobrepago
    public void Pagada_cuando_los_abonos_cubren_el_total(decimal total, decimal pagado)
    {
        Calcular(-30, total, pagado).Should().Be(SemaforoCuota.Pagada);
    }

    [Fact]
    public void Abono_parcial_no_cuenta_como_pagada()
    {
        Calcular(-3, montoPagado: 999.99m).Should().Be(SemaforoCuota.Vencida);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(30)]
    [InlineData(365)]
    public void AlDia_cuando_vence_a_mas_de_7_dias(int dias)
    {
        Calcular(dias).Should().Be(SemaforoCuota.AlDia);
    }

    [Theory]
    [InlineData(0)]  // vence hoy
    [InlineData(1)]
    [InlineData(7)]  // borde superior
    public void PorVencer_cuando_vence_entre_hoy_y_7_dias(int dias)
    {
        Calcular(dias).Should().Be(SemaforoCuota.PorVencer);
    }

    [Theory]
    [InlineData(-1)]   // venció ayer
    [InlineData(-15)]  // borde de tolerancia
    public void Vencida_entre_1_y_15_dias_sin_pagar(int dias)
    {
        Calcular(dias).Should().Be(SemaforoCuota.Vencida);
    }

    [Theory]
    [InlineData(-16)]  // primer día de mora
    [InlineData(-90)]
    public void EnMora_pasados_mas_de_15_dias(int dias)
    {
        Calcular(dias).Should().Be(SemaforoCuota.EnMora);
    }

    [Fact]
    public void Sobrecarga_con_entidad_Cuota_produce_el_mismo_resultado()
    {
        var cuota = new Cuota
        {
            FechaVencimiento = Hoy.AddDays(3),
            MontoTotal = 500m,
            MontoPagado = 0m,
            Estado = EstadoCuota.Pendiente
        };

        CuotaEstadoCalculator.Calcular(cuota, Hoy).Should().Be(SemaforoCuota.PorVencer);
    }
}
