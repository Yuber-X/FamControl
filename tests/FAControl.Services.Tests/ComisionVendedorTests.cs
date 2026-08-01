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
/// Acá se fija la CUENTA, que es lo que puede romperse en silencio.
///
/// Al día siguiente pidió lo contrario para la factura: "agregar un nuevo
/// checkbox para mostrar la comisión del vendedor a la factura si está
/// activa" (038). Dejó de ser una regla y pasó a ser una opción, apagada por
/// defecto. Eso también se prueba acá: es la condición que lee el ticket.
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
        cfg.ComisionEnFactura.Should().BeFalse("mostrarla al cliente se activa a propósito");
        cfg.MuestraComisionEnFactura.Should().BeFalse();
    }

    // ---------- Mostrarla en la factura (038) ----------

    /// <summary>
    /// Calcular la comisión y enseñársela al cliente son DOS decisiones. Con la
    /// comisión encendida y la casilla apagada —el caso de todo negocio que ya
    /// venía usando 037— el ticket sigue saliendo igual que antes.
    /// </summary>
    [Fact]
    public void CalcularlaNoImplicaMostrarla()
    {
        var cfg = new ConfiguracionNegocio { ComisionActiva = true, ComisionPorcentaje = 5m };

        cfg.ComisionSobre(10_000m).Should().Be(500m, "el cuadre la sigue necesitando");
        cfg.MuestraComisionEnFactura.Should().BeFalse();
    }

    [Fact]
    public void ConLasDosEncendidas_SaleEnLaFactura()
    {
        var cfg = new ConfiguracionNegocio
        {
            ComisionActiva = true, ComisionPorcentaje = 5m, ComisionEnFactura = true
        };

        cfg.MuestraComisionEnFactura.Should().BeTrue();
    }

    /// <summary>
    /// Si se apaga la comisión, la casilla de la factura no alcanza para
    /// imprimir nada: sería una línea de RD$ 0.00 en cada ticket. El valor se
    /// conserva por si la comisión vuelve a encenderse.
    /// </summary>
    [Fact]
    public void SinComisionActiva_LaCasillaDeLaFacturaNoAlcanza()
    {
        var cfg = new ConfiguracionNegocio
        {
            ComisionActiva = false, ComisionPorcentaje = 5m, ComisionEnFactura = true
        };

        cfg.MuestraComisionEnFactura.Should().BeFalse();
        cfg.ComisionEnFactura.Should().BeTrue("queda guardado para cuando se reactive");
    }
}
