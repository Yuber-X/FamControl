using System.Text;
using FluentAssertions;
using FAControl.Models.Pos;
using FAControl.Services.Pos;

namespace FAControl.Services.Tests;

/// <summary>
/// Barrido de invariantes sobre los cálculos del POS-500, hermano del
/// <see cref="AuditoriaCalculosTests"/> de la amortización.
///
/// No prueba una venta puntual: recorre cientos de combinaciones de canasta,
/// tasa de ITBIS y modo de redondeo, y verifica lo que NUNCA puede romperse
/// sin importar los números. Una factura que no cumple alguna de estas es
/// plata mal contada, y el ticket ya salió impreso.
/// </summary>
public class AuditoriaCalculosPosTests
{
    private static readonly decimal[] Precios =
        [0.01m, 0.99m, 1m, 12.50m, 99.99m, 250m, 1_499.95m, 25_000m];

    private static readonly int[] Cantidades = [1, 2, 3, 7, 12, 100];

    private static readonly decimal[] Tasas = [0m, 16m, 18m, 27.5m];

    private static VentaLinea Linea(decimal precio, int cantidad) =>
        new(ProductoId: 1, NombreProducto: "Producto", cantidad, precio);

    /// <summary>Cuántos decimales tiene de verdad el número.</summary>
    private static int Decimales(decimal v) => (decimal.GetBits(v)[3] >> 16) & 0xFF;

    // =========================================================
    // Los totales de una venta
    // =========================================================

    /// <summary>
    /// Las reglas que valen para cualquier canasta y cualquier configuración:
    ///
    ///  1. El subtotal es EXACTAMENTE la suma de las líneas. Si no, el cliente
    ///     paga por algo que no está en el papel.
    ///  2. Ningún importe es negativo.
    ///  3. Todo importe tiene como mucho 2 decimales: es plata, no un promedio.
    ///  4. El ITBIS es el de la tasa aplicada al subtotal, redondeado UNA vez.
    ///     Acumularlo por línea da un número distinto al que el cliente suma
    ///     con la calculadora del celular.
    ///  5. El total no se aleja del exacto (subtotal + ITBIS) más de lo que el
    ///     modo de redondeo permite: al centavo, nada; al peso, menos de un
    ///     peso. "Al peso más cercano" SÍ puede quedar por debajo del subtotal
    ///     —RD$4.30 cobra RD$4— y es lo que el dueño eligió.
    /// </summary>
    [Theory]
    [InlineData(ModoRedondeo.Centavo)]
    [InlineData(ModoRedondeo.Peso)]
    [InlineData(ModoRedondeo.Arriba)]
    public void CualquierVenta_CumpleLasReglasDeLaPlata(ModoRedondeo modo)
    {
        var fallas = new StringBuilder();

        foreach (var precio in Precios)
        foreach (var cantidad in Cantidades)
        foreach (var tasa in Tasas)
        {
            // Canasta de tres líneas: una sola línea esconde los errores de
            // acumulación, que son justamente los que se buscan.
            var lineas = new List<VentaLinea>
            {
                Linea(precio, cantidad),
                Linea(0.99m, 3),
                Linea(precio / 3m == decimal.Zero ? 1m : Math.Round(precio / 3m, 2), 1)
            };

            var t = VentaService.CalcularTotales(lineas, tasa, modo);
            var caso = $"precio={precio} cant={cantidad} tasa={tasa} modo={modo}";

            var sumaLineas = lineas.Sum(l => l.Cantidad * l.PrecioUnitario);
            if (t.Subtotal != sumaLineas)
                fallas.AppendLine($"{caso}: subtotal {t.Subtotal} ≠ suma de líneas {sumaLineas}");

            if (t.Subtotal < 0m || t.Itbis < 0m || t.Total < 0m)
                fallas.AppendLine($"{caso}: importe negativo ({t.Subtotal}/{t.Itbis}/{t.Total})");

            if (Decimales(t.Subtotal) > 2 || Decimales(t.Itbis) > 2 || Decimales(t.Total) > 2)
                fallas.AppendLine($"{caso}: más de 2 decimales ({t.Subtotal}/{t.Itbis}/{t.Total})");

            var itbisEsperado = Math.Round(t.Subtotal * tasa / 100m, 2, MidpointRounding.AwayFromZero);
            if (t.Itbis != itbisEsperado)
                fallas.AppendLine($"{caso}: ITBIS {t.Itbis} ≠ {itbisEsperado}");

            var exacto = t.Subtotal + t.Itbis;
            var margen = modo == ModoRedondeo.Centavo ? 0m : 1m;
            if (Math.Abs(t.Total - exacto) >= margen && t.Total != exacto)
                fallas.AppendLine($"{caso}: total {t.Total} se aleja de {exacto} más de {margen}");
        }

        fallas.Length.Should().Be(0, $"ninguna venta puede romper estas reglas:\n{fallas}");
    }

    /// <summary>
    /// Redondeo al centavo (el que usa el cliente): el papel tiene que cuadrar
    /// a la vista — subtotal + ITBIS = total, sin "más o menos".
    /// </summary>
    [Fact]
    public void RedondeoAlCentavo_ElTicketCuadraExacto()
    {
        foreach (var precio in Precios)
        foreach (var cantidad in Cantidades)
        foreach (var tasa in Tasas)
        {
            var t = VentaService.CalcularTotales(
                [Linea(precio, cantidad)], tasa, ModoRedondeo.Centavo);

            t.Total.Should().Be(t.Subtotal + t.Itbis,
                $"precio={precio} cant={cantidad} tasa={tasa}");
        }
    }

    /// <summary>
    /// Redondear a pesos mueve el total menos de un peso, y "arriba" nunca
    /// cobra de menos. Es lo único que el redondeo tiene permitido hacer.
    /// </summary>
    [Theory]
    [InlineData(ModoRedondeo.Peso)]
    [InlineData(ModoRedondeo.Arriba)]
    public void RedondeoAPeso_MueveMenosDeUnPeso(ModoRedondeo modo)
    {
        foreach (var precio in Precios)
        foreach (var tasa in Tasas)
        {
            var t = VentaService.CalcularTotales([Linea(precio, 3)], tasa, modo);
            var exacto = t.Subtotal + t.Itbis;
            var caso = $"precio={precio} tasa={tasa} modo={modo}";

            Math.Abs(t.Total - exacto).Should().BeLessThan(1m, caso);
            t.Total.Should().Be(Math.Round(t.Total, 0), $"{caso}: tiene que quedar en pesos enteros");

            if (modo == ModoRedondeo.Arriba)
                t.Total.Should().BeGreaterThanOrEqualTo(exacto, $"{caso}: 'arriba' no cobra de menos");
        }
    }

    /// <summary>
    /// ITBIS apagado (Configuración → Cálculos e impuestos): la factura no
    /// puede quedar con un impuesto fantasma ni con un total inflado.
    /// </summary>
    [Fact]
    public void ItbisApagado_NoCobraImpuesto()
    {
        var cfg = new ConfiguracionNegocio { ItbisActivo = false, ItbisTasa = 18m };

        var t = VentaService.CalcularTotales(
            [Linea(1_000m, 2)], cfg.ItbisTasaEfectiva, ModoRedondeo.Centavo);

        cfg.ItbisTasaEfectiva.Should().Be(0m);
        t.Itbis.Should().Be(0m);
        t.Total.Should().Be(t.Subtotal);
    }

    /// <summary>
    /// El ITBIS se saca del subtotal de una sola vez. Sacarlo línea por línea y
    /// sumarlo da otro número; esta prueba fija cuál de los dos es el bueno.
    /// </summary>
    [Fact]
    public void ElItbisSaleDelSubtotal_NoDeLaSumaPorLinea()
    {
        // 3 líneas de 0.99: por línea daría 3 × 0.18 = 0.54; sobre el subtotal
        // (2.97) da 0.53. El correcto es el segundo.
        var lineas = new List<VentaLinea> { Linea(0.99m, 1), Linea(0.99m, 1), Linea(0.99m, 1) };

        var t = VentaService.CalcularTotales(lineas, 18m, ModoRedondeo.Centavo);

        t.Subtotal.Should().Be(2.97m);
        t.Itbis.Should().Be(0.53m);
        t.Total.Should().Be(3.50m);
    }

    // =========================================================
    // El cambio
    // =========================================================

    /// <summary>
    /// El cambio es lo que el cajero le devuelve al cliente: si esto falla, la
    /// caja no cuadra al cerrar el día.
    /// </summary>
    [Theory]
    [InlineData(100, 100, 0)]
    [InlineData(500, 337.50, 162.50)]
    [InlineData(1000, 999.99, 0.01)]
    public void ElCambio_EsElEfectivoMenosElTotal(decimal efectivo, decimal total, decimal esperado)
    {
        VentaService.CalcularCambio(efectivo, total).Should().Be(esperado);
    }

    /// <summary>El cambio se calcula contra el total YA redondeado, no contra el exacto.</summary>
    [Fact]
    public void ElCambio_UsaElTotalRedondeado()
    {
        var t = VentaService.CalcularTotales([Linea(99.99m, 1)], 18m, ModoRedondeo.Arriba);

        // 99.99 + 18.00 = 117.99 → hacia arriba, 118
        t.Total.Should().Be(118m);
        VentaService.CalcularCambio(200m, t.Total).Should().Be(82m);
    }

    // =========================================================
    // La comisión del vendedor
    // =========================================================

    /// <summary>
    /// La comisión sale de un porcentaje del monto vendido: nunca puede pasarlo,
    /// nunca es negativa y siempre queda en centavos redondos.
    /// </summary>
    [Fact]
    public void LaComision_NuncaSuperaLoVendido()
    {
        foreach (var porcentaje in new[] { 0m, 0.5m, 3m, 10m, 100m })
        foreach (var monto in new[] { 0m, 0.01m, 137.77m, 25_000m, 1_250_000m })
        {
            var cfg = new ConfiguracionNegocio
            {
                ComisionActiva = true,
                ComisionPorcentaje = porcentaje
            };

            var comision = cfg.ComisionSobre(monto);
            var caso = $"{porcentaje}% de {monto}";

            comision.Should().BeGreaterThanOrEqualTo(0m, caso);
            comision.Should().BeLessThanOrEqualTo(monto + 0.01m, caso);
            Decimales(comision).Should().BeLessThanOrEqualTo(2, caso);
        }
    }

    /// <summary>Con la comisión apagada no hay comisión, por más porcentaje que haya guardado.</summary>
    [Fact]
    public void ComisionApagada_NoPagaNada()
    {
        var cfg = new ConfiguracionNegocio { ComisionActiva = false, ComisionPorcentaje = 10m };

        cfg.ComisionEfectiva.Should().Be(0m);
        cfg.ComisionSobre(50_000m).Should().Be(0m);
        cfg.MuestraComisionEnFactura.Should().BeFalse();
    }

    /// <summary>
    /// Mostrar la comisión en la factura es una decisión aparte de calcularla
    /// (038): marcar la casilla sin la comisión activa no imprime nada.
    /// </summary>
    [Fact]
    public void LaCasillaDeFactura_NoAlcanzaSinComisionActiva()
    {
        new ConfiguracionNegocio { ComisionActiva = false, ComisionEnFactura = true }
            .MuestraComisionEnFactura.Should().BeFalse();

        new ConfiguracionNegocio { ComisionActiva = true, ComisionEnFactura = true }
            .MuestraComisionEnFactura.Should().BeTrue();
    }

    // =========================================================
    // El número de factura
    // =========================================================

    [Theory]
    [InlineData("F-", 1, FormatoFactura.Simple, 2026, "F-0001")]
    [InlineData("F-", 9999, FormatoFactura.Simple, 2026, "F-9999")]
    // Pasado el 9999 el número SIGUE creciendo: no se reinicia ni se corta.
    [InlineData("F-", 10000, FormatoFactura.Simple, 2026, "F-10000")]
    [InlineData("F-", 1, FormatoFactura.ConAnio, 2026, "F-2026-0001")]
    [InlineData("", 42, FormatoFactura.Simple, 2026, "0042")]
    public void ElNumeroDeFactura_SeFormateaSiempreIgual(
        string prefijo, long numero, FormatoFactura formato, int anio, string esperado)
    {
        VentaService.FormatearNumeroFactura(prefijo, numero, formato, anio).Should().Be(esperado);
    }

    // =========================================================
    // El semáforo de caducidad
    // =========================================================

    /// <summary>
    /// Meses COMPLETOS, igual que el TIMESTAMPDIFF(MONTH) de MySQL: faltando un
    /// día para el mes, todavía no es un mes. El color de la pantalla y el del
    /// correo de aviso salen de acá, y tienen que decir lo mismo.
    /// </summary>
    [Theory]
    [InlineData("2026-09-20", "2026-08-20", 1)]
    [InlineData("2026-09-19", "2026-08-20", 0)]   // falta 1 día: aún no es un mes
    [InlineData("2026-08-20", "2026-08-20", 0)]   // caduca hoy
    [InlineData("2026-07-21", "2026-08-20", 0)]   // venció hace 30 días
    [InlineData("2026-07-20", "2026-08-20", -1)]  // venció hace un mes exacto
    [InlineData("2027-08-20", "2026-08-20", 12)]
    public void MesesRestantes_CuentaMesesCompletos(string caduca, string hoy, int esperado)
    {
        CalculadoraCaducidad.MesesCompletosRestantes(DateOnly.Parse(caduca), DateOnly.Parse(hoy))
            .Should().Be(esperado);
    }

    [Theory]
    [InlineData(7, SemaforoCaducidad.Verde)]
    [InlineData(6, SemaforoCaducidad.Amarillo)]
    [InlineData(4, SemaforoCaducidad.Amarillo)]
    [InlineData(3, SemaforoCaducidad.Naranja)]
    [InlineData(2, SemaforoCaducidad.Naranja)]
    [InlineData(1, SemaforoCaducidad.Rojo)]
    [InlineData(0, SemaforoCaducidad.Rojo)]
    [InlineData(-5, SemaforoCaducidad.Rojo)]      // ya caducado: rojo, no otro color
    public void ElSemaforo_NoTieneHuecosNiSolapes(int mesesRestantes, SemaforoCaducidad esperado)
    {
        var hoy = new DateOnly(2026, 8, 20);
        var caduca = hoy.AddMonths(mesesRestantes);

        CalculadoraCaducidad.Calcular(caduca, hoy).Should().Be(esperado);
    }

    /// <summary>
    /// Todo producto tiene un color: no hay fecha para la que el semáforo se
    /// quede sin responder. Barrido de 3 años día por día.
    /// </summary>
    [Fact]
    public void TodaFechaTieneColor()
    {
        var hoy = new DateOnly(2026, 8, 20);
        for (var d = -365; d <= 730; d++)
        {
            var color = CalculadoraCaducidad.Calcular(hoy.AddDays(d), hoy);
            Enum.IsDefined(color).Should().BeTrue($"día {d}");
        }
    }
}
