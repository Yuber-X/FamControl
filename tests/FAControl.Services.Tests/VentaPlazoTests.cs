using FAControl.Models;
using FluentAssertions;
using Xunit;

namespace FAControl.Services.Tests;

/// <summary>
/// Lógica pura del financiamiento del dealer (016): reparto del saldo en
/// plazos, ajuste del redondeo y validaciones. El cobro transaccional se
/// prueba en integración con la BD real.
/// </summary>
public class VentaPlazoTests
{
    private static PlanPlazos Plan(decimal inicial, int cantidad, int cadaDias = 30) =>
        new(inicial, cantidad, new DateOnly(2026, 9, 1), cadaDias);

    [Fact]
    public void Reparte_ElSaldoEnPlazosIguales()
    {
        var plazos = VentaPlazoService.CalcularPlazos(600_000m, Plan(inicial: 200_000m, cantidad: 4));

        plazos.Should().HaveCount(4);
        plazos.Should().OnlyContain(p => p.Monto == 100_000m);   // (600k − 200k) / 4
        plazos.Sum(p => p.Monto).Should().Be(400_000m);
    }

    [Fact]
    public void ElRedondeo_SeAjustaEnElUltimoPlazo()
    {
        // 100,000 / 3 = 33,333.333... → dos de 33,333.33 y el último absorbe el resto
        var plazos = VentaPlazoService.CalcularPlazos(100_000m, Plan(inicial: 0m, cantidad: 3));

        plazos[0].Monto.Should().Be(33_333.33m);
        plazos[1].Monto.Should().Be(33_333.33m);
        plazos[2].Monto.Should().Be(33_333.34m);
        // Lo que importa: la suma da EXACTAMENTE el saldo, sin centavos perdidos
        plazos.Sum(p => p.Monto).Should().Be(100_000m);
    }

    [Fact]
    public void LosVencimientos_AvanzanSegunElIntervalo()
    {
        var plazos = VentaPlazoService.CalcularPlazos(90_000m, Plan(inicial: 0m, cantidad: 3));

        plazos[0].FechaVencimiento.Should().Be(new DateOnly(2026, 9, 1));
        plazos[1].FechaVencimiento.Should().Be(new DateOnly(2026, 10, 1));   // +30 días
        plazos[2].FechaVencimiento.Should().Be(new DateOnly(2026, 10, 31));  // +60 días
        plazos.Should().OnlyContain(p => p.Estado == EstadoPlazo.Pendiente);
    }

    [Fact]
    public void Quincenal_UsaElIntervaloIndicado()
    {
        var plazos = VentaPlazoService.CalcularPlazos(60_000m, Plan(inicial: 0m, cantidad: 2, cadaDias: 15));

        plazos[1].FechaVencimiento.Should().Be(new DateOnly(2026, 9, 16));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(241)]
    public void Rechaza_CantidadDePlazosInvalida(int cantidad)
    {
        var accion = () => VentaPlazoService.CalcularPlazos(100_000m, Plan(0m, cantidad));
        accion.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Rechaza_InicialMayorQueElPrecio()
    {
        var accion = () => VentaPlazoService.CalcularPlazos(100_000m, Plan(inicial: 120_000m, cantidad: 6));
        accion.Should().Throw<ArgumentException>().WithMessage("*inicial*");
    }

    [Fact]
    public void Rechaza_InicialQueCubreTodo_PorqueSeriaVentaAlContado()
    {
        var accion = () => VentaPlazoService.CalcularPlazos(100_000m, Plan(inicial: 100_000m, cantidad: 6));
        accion.Should().Throw<ArgumentException>().WithMessage("*contado*");
    }

    // ---------- Semáforo y estado del plan ----------

    [Fact]
    public void PlazoAtrasado_SoloSiVencioYQuedaSaldo()
    {
        var hoy = new DateOnly(2026, 10, 15);
        var vencidoConSaldo = new VentaPlazo
        { FechaVencimiento = new DateOnly(2026, 10, 1), Monto = 1_000m, MontoPagado = 400m };
        var vencidoSaldado = new VentaPlazo
        {
            FechaVencimiento = new DateOnly(2026, 10, 1), Monto = 1_000m, MontoPagado = 1_000m,
            Estado = EstadoPlazo.Pagado
        };
        var porVencer = new VentaPlazo
        { FechaVencimiento = new DateOnly(2026, 11, 1), Monto = 1_000m };

        vencidoConSaldo.EstaAtrasado(hoy).Should().BeTrue();
        vencidoConSaldo.SaldoPendiente.Should().Be(600m);
        vencidoSaldado.EstaAtrasado(hoy).Should().BeFalse();
        porVencer.EstaAtrasado(hoy).Should().BeFalse();
    }

    [Fact]
    public void EstadoFinanciamiento_DerivaPendienteYRecibido()
    {
        var estado = new EstadoFinanciamiento(
            VentaId: 1, Codigo: "VC-0001", Tipo: TipoVenta.Plazos,
            Precio: 500_000m, Inicial: 100_000m, TotalAPlazos: 400_000m,
            Pagado: 150_000m, CantidadPlazos: 4, PlazosPagados: 1, PlazosAtrasados: 0,
            FechaLimite: null, Plazos: []);

        estado.Pendiente.Should().Be(250_000m);        // 400k − 150k
        estado.RecibidoTotal.Should().Be(250_000m);    // inicial 100k + abonos 150k
        estado.EstaSaldada.Should().BeFalse();
    }

    [Fact]
    public void SeparacionVencida_SoloTrasLaFechaLimiteYConSaldo()
    {
        var separacion = new EstadoFinanciamiento(
            VentaId: 2, Codigo: "VC-0002", Tipo: TipoVenta.Separacion,
            Precio: 700_000m, Inicial: 50_000m, TotalAPlazos: 650_000m,
            Pagado: 0m, CantidadPlazos: 0, PlazosPagados: 0, PlazosAtrasados: 0,
            FechaLimite: new DateOnly(2026, 8, 15), Plazos: []);

        separacion.SeparacionVencida(new DateOnly(2026, 8, 15)).Should().BeFalse();  // el día aún vale
        separacion.SeparacionVencida(new DateOnly(2026, 8, 16)).Should().BeTrue();
    }
}
