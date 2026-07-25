using FAControl.Models;
using FluentAssertions;
using Xunit;

namespace FAControl.Services.Tests;

/// <summary>
/// Lógica pura de la secuencia de comprobantes (012): formato del NCF,
/// restantes, vencimiento y agotamiento. La reserva atómica (FOR UPDATE)
/// se prueba en integración con la BD real.
/// </summary>
public class NcfSecuenciaTests
{
    [Theory]
    [InlineData("B02", 8, 1, "B0200000001")]
    [InlineData("B02", 8, 12345678, "B0212345678")]
    [InlineData("E32", 10, 5, "E320000000005")]
    public void Formatear_RellenaConCeros(string prefijo, int largo, long numero, string esperado)
    {
        var secuencia = new NcfSecuencia { Prefijo = prefijo, Largo = largo };
        secuencia.Formatear(numero).Should().Be(esperado);
    }

    [Fact]
    public void Restantes_ConRango_CuentaInclusive()
    {
        var secuencia = new NcfSecuencia { Proxima = 95, FinRango = 100 };
        secuencia.Restantes.Should().Be(6);   // 95..100
        secuencia.EstaAgotada.Should().BeFalse();
    }

    [Fact]
    public void Restantes_SinRango_EsNull()
    {
        new NcfSecuencia { Proxima = 1, FinRango = null }.Restantes.Should().BeNull();
    }

    [Fact]
    public void Agotada_CuandoProximaPasaElFin()
    {
        var secuencia = new NcfSecuencia { Proxima = 101, FinRango = 100 };
        secuencia.EstaAgotada.Should().BeTrue();
        secuencia.Restantes.Should().Be(0);
    }

    [Fact]
    public void Vencida_SoloDespuesDeLaFecha()
    {
        var secuencia = new NcfSecuencia { Vencimiento = new DateOnly(2026, 12, 31) };
        secuencia.EstaVencida(new DateOnly(2026, 12, 31)).Should().BeFalse();  // el día del vencimiento aún vale
        secuencia.EstaVencida(new DateOnly(2027, 1, 1)).Should().BeTrue();
        new NcfSecuencia { Vencimiento = null }.EstaVencida(new DateOnly(2030, 1, 1)).Should().BeFalse();
    }
}
