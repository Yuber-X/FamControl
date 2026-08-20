using FluentAssertions;
using FAControl.Models;
using FAControl.Services;

namespace FAControl.Services.Tests;

/// <summary>
/// Método "interés fijo primero, capital después" (cliente 2026-08-06):
/// "que ellos le prestan con una taza fija de 6 meses, y del 7 en adelante
/// cambia segun el capital que dejaron".
///
/// La prueba principal reproduce, número por número, la tabla de amortización
/// que el cliente mandó por WhatsApp. Si esa tabla no da exacta, el método está
/// mal — es la única referencia de cómo trabajan de verdad.
/// </summary>
public class CapitalDiferidoTests
{
    private readonly AmortizacionService _sut = new();
    private static readonly DateOnly PrimerPago = new(2026, 8, 8);

    private static ParametrosAmortizacion Params(decimal capital, decimal tasaMensual,
        int plazo, int? inicioCapital = null) =>
        new(capital, tasaMensual, plazo, Modalidad.Mensual,
            MetodoAmortizacion.CapitalDiferido, PrimerPago, inicioCapital);

    /// <summary>
    /// La tabla del cliente: RD$150,000 al 5% mensual, 18 meses (6 de interés
    /// fijo y 12 amortizando capital).
    /// </summary>
    [Fact]
    public void ReproduceLaTablaQueMandoElCliente()
    {
        var tabla = _sut.Calcular(Params(150_000m, 5m, 18, inicioCapital: 7));

        tabla.Should().HaveCount(18);

        // --- Cuotas 1 a 6: solo interés, el saldo no se mueve ---
        foreach (var c in tabla.Take(6))
        {
            c.Interes.Should().Be(7_500m);
            c.Capital.Should().Be(0m);
            c.MontoTotal.Should().Be(7_500m);
            c.SaldoDespues.Should().Be(150_000m);
        }

        // --- Cuota 7: arranca el capital. Interés todavía sobre 150,000 ---
        tabla[6].Interes.Should().Be(7_500m);
        tabla[6].Capital.Should().Be(12_500m);
        tabla[6].MontoTotal.Should().Be(20_000m);
        tabla[6].SaldoDespues.Should().Be(137_500m);

        // --- Cuota 8: el interés ya baja, porque el saldo bajó ---
        tabla[7].Interes.Should().Be(6_875m);
        tabla[7].Capital.Should().Be(12_500m);
        tabla[7].MontoTotal.Should().Be(19_375m);
        tabla[7].SaldoDespues.Should().Be(125_000m);

        // --- Cuota 15 (fila del medio de la segunda hoja) ---
        tabla[14].Interes.Should().Be(2_500m);
        tabla[14].MontoTotal.Should().Be(15_000m);
        tabla[14].SaldoDespues.Should().Be(37_500m);

        // --- Cuota 18: la última liquida y el saldo cierra en cero ---
        tabla[17].Interes.Should().Be(625m);
        tabla[17].Capital.Should().Be(12_500m);
        tabla[17].MontoTotal.Should().Be(13_125m);
        tabla[17].SaldoDespues.Should().Be(0m);
    }

    [Fact]
    public void LosTotalesDeLaTablaDelClienteCuadran()
    {
        var tabla = _sut.Calcular(Params(150_000m, 5m, 18, inicioCapital: 7));

        tabla.Sum(c => c.Capital).Should().Be(150_000m, "el capital se devuelve entero");
        // 6 meses × 7,500 de gracia + la serie decreciente de la amortización
        tabla.Sum(c => c.Interes).Should().Be(93_750m);
        tabla.Sum(c => c.MontoTotal).Should().Be(243_750m);
    }

    /// <summary>
    /// Lo que distingue este método del francés: la cuota NO es fija, va bajando
    /// desde que arranca el capital. Si alguien la "arregla" para que sea fija,
    /// esto lo agarra.
    /// </summary>
    [Fact]
    public void LaCuotaBajaMesAMesDesdeQueArrancaElCapital()
    {
        var tabla = _sut.Calcular(Params(150_000m, 5m, 18, inicioCapital: 7));

        var amortizando = tabla.Skip(6).ToList();
        for (var k = 1; k < amortizando.Count; k++)
            amortizando[k].MontoTotal.Should().BeLessThan(amortizando[k - 1].MontoTotal);
    }

    [Fact]
    public void ElSaldoNoSeMueveDuranteLaGraciaYLuegoBajaParejo()
    {
        var tabla = _sut.Calcular(Params(150_000m, 5m, 18, inicioCapital: 7));

        tabla.Take(6).Should().OnlyContain(c => c.SaldoDespues == 150_000m);
        // 12 cuotas de 12,500: el abono a capital es constante, no la cuota
        tabla.Skip(6).Should().OnlyContain(c => c.Capital == 12_500m);
    }

    // =========================================================
    // Modo automático (el sistema elige dónde arranca el capital)
    // =========================================================

    /// <summary>
    /// Un tercio del plazo como gracia. El 18 → 7 sale del ejemplo del cliente;
    /// el resto es la misma regla aplicada.
    /// </summary>
    [Theory]
    [InlineData(18, 7)]
    [InlineData(12, 5)]
    [InlineData(6, 3)]
    [InlineData(3, 2)]
    [InlineData(2, 2)]
    [InlineData(1, 1)]
    public void ModoAutomatico_SugiereUnTercioDeGracia(int plazo, int esperada)
    {
        AmortizacionService.CuotaInicioCapitalSugerida(plazo).Should().Be(esperada);
    }

    [Fact]
    public void SinCuotaDeInicio_UsaLaSugerida()
    {
        var automatico = _sut.Calcular(Params(150_000m, 5m, 18));
        var manual = _sut.Calcular(Params(150_000m, 5m, 18, inicioCapital: 7));

        automatico.Select(c => c.MontoTotal).Should().Equal(manual.Select(c => c.MontoTotal));
    }

    // =========================================================
    // Modo manual (el usuario elige la cuota exacta)
    // =========================================================

    [Fact]
    public void ArrancandoEnLaPrimera_EsAmortizacionParejaSinGracia()
    {
        var tabla = _sut.Calcular(Params(120_000m, 5m, 12, inicioCapital: 1));

        tabla.Should().OnlyContain(c => c.Capital == 10_000m, "no hay meses de gracia");
        tabla[0].Interes.Should().Be(6_000m);
        tabla[11].SaldoDespues.Should().Be(0m);
        tabla.Sum(c => c.Capital).Should().Be(120_000m);
    }

    /// <summary>
    /// El caso extremo del modo manual: el capital entero cae en la última
    /// cuota. Ahí este método coincide con el préstamo ABIERTO.
    /// </summary>
    [Fact]
    public void ArrancandoEnLaUltima_EquivaleAlPrestamoAbierto()
    {
        var diferido = _sut.Calcular(Params(150_000m, 5m, 18, inicioCapital: 18));
        var abierto = _sut.Calcular(new ParametrosAmortizacion(
            150_000m, 5m, 18, Modalidad.Mensual, MetodoAmortizacion.SoloInteres, PrimerPago));

        diferido.Select(c => c.MontoTotal).Should().Equal(abierto.Select(c => c.MontoTotal));
        diferido.Select(c => c.Capital).Should().Equal(abierto.Select(c => c.Capital));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(19)]
    public void CuotaDeInicioImposible_SeRechazaConUnMensajeQueSeEntiende(int inicio)
    {
        var accion = () => _sut.Calcular(Params(150_000m, 5m, 18, inicioCapital: inicio));

        accion.Should().Throw<ArgumentException>()
            .Which.Message.Should().NotBeNullOrWhiteSpace();
    }

    // =========================================================
    // Redondeo y bordes
    // =========================================================

    /// <summary>
    /// Capital que no se divide justo: 100,000 entre 7 cuotas da 14,285.714…
    /// La última tiene que absorber la diferencia y dejar el saldo EXACTO en
    /// cero, o el cliente termina debiendo centavos que nadie sabe cobrar.
    /// </summary>
    [Fact]
    public void CapitalQueNoDivideJusto_LaUltimaCuotaCierraElSaldo()
    {
        var tabla = _sut.Calcular(Params(100_000m, 3m, 10, inicioCapital: 4));

        tabla.Sum(c => c.Capital).Should().Be(100_000m);
        tabla[^1].SaldoDespues.Should().Be(0m);
        tabla.Should().OnlyContain(c => c.SaldoDespues >= 0m);
    }

    [Fact]
    public void SinInteres_SoloDevuelveElCapital()
    {
        var tabla = _sut.Calcular(Params(60_000m, 0m, 12, inicioCapital: 5));

        tabla.Should().OnlyContain(c => c.Interes == 0m);
        tabla.Sum(c => c.MontoTotal).Should().Be(60_000m);
        tabla[^1].SaldoDespues.Should().Be(0m);
    }

    /// <summary>Pago único: una sola cuota, sin gracia posible. No debe romperse.</summary>
    [Fact]
    public void PagoUnico_NoSeRompeConEsteMetodo()
    {
        var tabla = _sut.Calcular(new ParametrosAmortizacion(
            50_000m, 5m, 18, Modalidad.PagoUnico, MetodoAmortizacion.CapitalDiferido,
            PrimerPago, CuotaInicioCapital: 7));

        tabla.Should().ContainSingle();
        tabla[0].Capital.Should().Be(50_000m);
        tabla[0].Interes.Should().Be(2_500m);
        tabla[0].SaldoDespues.Should().Be(0m);
    }

    /// <summary>
    /// El resumen alimenta las tarjetas del formulario. La "cuota fija" que
    /// muestra es la PRIMERA, que en este método es la de solo interés: es
    /// justamente lo que el cliente quiere ver arriba ("paga 7,500 los primeros
    /// 6 meses").
    /// </summary>
    [Fact]
    public void ElResumenMuestraLaPrimeraCuotaYLosTotalesCorrectos()
    {
        var tabla = _sut.Calcular(Params(150_000m, 5m, 18, inicioCapital: 7));

        var resumen = _sut.Resumir(tabla);

        resumen.CuotaFija.Should().Be(7_500m);
        resumen.Capital.Should().Be(150_000m);
        resumen.InteresTotal.Should().Be(93_750m);
        resumen.TotalAPagar.Should().Be(243_750m);
    }

    [Fact]
    public void ModalidadQuincenal_UsaLaTasaDelPeriodo()
    {
        // 5% mensual → 2.5% quincenal
        var tabla = _sut.Calcular(new ParametrosAmortizacion(
            100_000m, 5m, 12, Modalidad.Quincenal, MetodoAmortizacion.CapitalDiferido,
            PrimerPago, CuotaInicioCapital: 5));

        tabla[0].Interes.Should().Be(2_500m);
        tabla[0].Capital.Should().Be(0m);
        tabla.Sum(c => c.Capital).Should().Be(100_000m);
        tabla[^1].SaldoDespues.Should().Be(0m);
    }

    // =========================================================
    // Conservar la cuota pactada al CORREGIR (2026-08-20)
    // =========================================================

    /// <summary>
    /// La cuota de inicio es un dato del préstamo, no una sugerencia: recalcular
    /// con otra cambia la tabla entera.
    ///
    /// Es lo que pasaba al corregir un préstamo diferido: la ventana no mandaba
    /// `CuotaInicioCapital` y el servicio caía en la sugerida. Un préstamo
    /// pactado con capital desde la 4 se recalculaba desde la 5, y el cliente
    /// terminaba con otras cuotas que las que firmó.
    /// </summary>
    [Fact]
    public void CambiarLaCuotaDeInicio_CambiaLaTablaEntera()
    {
        var pactado = _sut.Calcular(Params(120_000m, 4m, 12, inicioCapital: 4));
        var sugerido = _sut.Calcular(Params(120_000m, 4m, 12));   // la sugerida: 5

        AmortizacionService.CuotaInicioCapitalSugerida(12).Should().Be(5);
        pactado.Select(c => c.MontoTotal).Should().NotEqual(sugerido.Select(c => c.MontoTotal),
            "si dieran lo mismo esta prueba no protegería nada");

        pactado[3].Capital.Should().BeGreaterThan(0m, "lo pactado cobra capital desde la 4");
        sugerido[3].Capital.Should().Be(0m, "la sugerida recién cobra en la 5");
    }

    /// <summary>Capital desde la primera cuota: no hay gracia y igual cierra en cero.</summary>
    [Fact]
    public void CapitalDesdeLaPrimeraCuota_NoTieneGracia()
    {
        var tabla = _sut.Calcular(Params(60_000m, 4m, 12, inicioCapital: 1));

        tabla.Should().OnlyContain(c => c.Capital > 0m);
        tabla.Sum(c => c.Capital).Should().Be(60_000m);
        tabla[^1].SaldoDespues.Should().Be(0m);
    }

    /// <summary>
    /// El otro extremo: la última cuota es la única que trae capital. Es válido
    /// —queda un globo al final— y tiene que cerrar igual de exacto.
    /// </summary>
    [Fact]
    public void LaUltimaCuotaPuedeSerLaUnicaConCapital()
    {
        var tabla = _sut.Calcular(Params(60_000m, 4m, 12, inicioCapital: 12));

        tabla.Take(11).Should().OnlyContain(c => c.Capital == 0m);
        tabla[^1].Capital.Should().Be(60_000m);
        tabla[^1].SaldoDespues.Should().Be(0m);
    }
}
