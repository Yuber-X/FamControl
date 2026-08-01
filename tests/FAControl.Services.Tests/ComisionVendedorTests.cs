using FluentAssertions;
using FAControl.Models.Pos;

namespace FAControl.Services.Tests;

/// <summary>
/// Comisión del vendedor del punto de venta (037).
///
/// Pedido de Yuber (2026-08-01): "esto no puede salir en la factura, pero sí en
/// el cuadre del día, en la exportación de excel y en 'vender' que se refleje
/// junto al subtotal; debe tener un checkbox para activar la comisión".
///
/// Acá se fija la CUENTA. Que no aparezca en la factura es una propiedad del
/// ticket —no lo lee de ningún lado, no hay nada que probar—; lo que sí puede
/// romperse en silencio es el cálculo.
/// </summary>
public class ComisionVendedorTests
{
    [Fact]
    public void ApagadaNoCobraNada_PeroConservaElPorcentaje()
    {
        var cfg = new ConfiguracionNegocio { ComisionActiva = false, ComisionPorcentaje = 5m };

        cfg.ComisionEfectiva.Should().Be(0m);
        cfg.ComisionSobre(10_000m).Should().Be(0m);
        cfg.ComisionPorcentaje.Should().Be(5m, "el porcentaje queda guardado para cuando se reactive");
    }

    [Theory]
    [InlineData(5, 10000, 500)]
    [InlineData(2.5, 10000, 250)]
    [InlineData(10, 3333.33, 333.33)]
    [InlineData(0, 10000, 0)]
    public void ElPorcentajeQueSeEscribe_EsElQueSeAplica(
        decimal porcentaje, decimal monto, decimal esperado)
    {
        var cfg = new ConfiguracionNegocio { ComisionActiva = true, ComisionPorcentaje = porcentaje };

        cfg.ComisionSobre(monto).Should().Be(esperado);
    }

    /// <summary>
    /// Se redondea AL FINAL, con el mismo criterio que el resto de la app
    /// (a favor del negocio). Redondear por línea acumularía diferencias que no
    /// cuadrarían con el total del cuadre.
    /// </summary>
    [Fact]
    public void SeRedondeaAlFinal_ConElMismoCriterioQueElResto()
    {
        var cfg = new ConfiguracionNegocio { ComisionActiva = true, ComisionPorcentaje = 3m };

        // 99.99 × 3% = 2.9997 → 3.00
        cfg.ComisionSobre(99.99m).Should().Be(3.00m);
        // 16.75 × 3% = 0.5025 → 0.50
        cfg.ComisionSobre(16.75m).Should().Be(0.50m);
    }

    /// <summary>
    /// Un negocio recién instalado NO calcula comisión: el dueño tiene que
    /// activarla a propósito. Encenderla sola le pondría un gasto que nunca
    /// pidió.
    /// </summary>
    [Fact]
    public void PorDefectoVieneApagada()
    {
        var cfg = new ConfiguracionNegocio();

        cfg.ComisionActiva.Should().BeFalse();
        cfg.ComisionSobre(50_000m).Should().Be(0m);
    }
}
