using FAControl.Common;
using FluentAssertions;
using Xunit;

namespace FAControl.Services.Tests;

/// <summary>
/// Números y fechas en letras para el pagaré notarial (plantilla del cliente,
/// 2026-08-26). El acta escribe cada cifra dos veces y esa duplicación es lo
/// que le da valor probatorio, así que las letras tienen que ser exactas.
///
/// Los casos con nombre salen literalmente de la plantilla que mandó Verónica.
/// </summary>
public class NumeroALetrasTests
{
    // ================= enteros =================

    [Theory]
    [InlineData(0, "cero")]
    [InlineData(1, "uno")]
    [InlineData(15, "quince")]
    [InlineData(16, "dieciséis")]
    [InlineData(21, "veintiuno")]
    [InlineData(24, "veinticuatro")]
    [InlineData(30, "treinta")]
    [InlineData(31, "treinta y uno")]
    [InlineData(48, "cuarenta y ocho")]
    [InlineData(99, "noventa y nueve")]
    public void Unidades_Y_Decenas(long numero, string esperado) =>
        NumeroALetras.De(numero).Should().Be(esperado);

    [Theory]
    [InlineData(100, "cien")]           // exacto: "cien", nunca "ciento"
    [InlineData(101, "ciento uno")]     // con resto ya es "ciento"
    [InlineData(200, "doscientos")]
    [InlineData(500, "quinientos")]     // irregular
    [InlineData(700, "setecientos")]    // irregular
    [InlineData(900, "novecientos")]    // irregular
    [InlineData(999, "novecientos noventa y nueve")]
    public void Centenas(long numero, string esperado) =>
        NumeroALetras.De(numero).Should().Be(esperado);

    [Theory]
    [InlineData(1_000, "mil")]                          // nunca "un mil"
    [InlineData(1_001, "mil uno")]
    [InlineData(2_000, "dos mil")]
    [InlineData(15_000, "quince mil")]
    [InlineData(250_000, "doscientos cincuenta mil")]
    [InlineData(999_999, "novecientos noventa y nueve mil novecientos noventa y nueve")]
    public void Miles(long numero, string esperado) =>
        NumeroALetras.De(numero).Should().Be(esperado);

    [Theory]
    [InlineData(1_000_000, "un millón")]                // el millón SÍ concuerda
    [InlineData(2_000_000, "dos millones")]
    [InlineData(1_500_000, "un millón quinientos mil")]
    public void Millones(long numero, string esperado) =>
        NumeroALetras.De(numero).Should().Be(esperado);

    [Fact]
    public void Negativos_LlevanMenosDelante() =>
        NumeroALetras.De(-45).Should().Be("menos cuarenta y cinco");

    // ================= dinero =================

    [Fact]
    public void ElMontoDelPagareDeLaPlantilla()
    {
        // "la suma de DOCIENTOS CINCUENTA MIL PESOS DOMINICANO (RD$250,000.00)"
        // — la plantilla trae la errata "DOCIENTOS"; acá va bien escrito.
        NumeroALetras.PesosConCifra(250_000m)
            .Should().Be("DOSCIENTOS CINCUENTA MIL PESOS DOMINICANOS CON 00/100 (RD$250,000.00)");
    }

    [Fact]
    public void UnPeso_VaEnSingular() =>
        NumeroALetras.Pesos(1m, mayusculas: false).Should().Be("un peso dominicano con 00/100");

    [Fact]
    public void LosCentavosVanEnFraccion() =>
        NumeroALetras.Pesos(18_117.50m, mayusculas: false)
            .Should().Be("dieciocho mil ciento diecisiete pesos dominicanos con 50/100");

    [Fact]
    public void RedondeaAlejandoseDelCero()
    {
        // Regla del proyecto: MidpointRounding.AwayFromZero.
        NumeroALetras.Pesos(0.005m, mayusculas: false)
            .Should().Be("cero pesos dominicanos con 01/100");
    }

    [Fact]
    public void ElMontoDeLaCuotaDeLaPlantilla()
    {
        // "por la suma de DIECIOCHO MIL CIENTO DIECISIETE (18,117,00) pesos"
        // — la plantilla escribió la coma decimal mal; la cifra correcta es ésta.
        NumeroALetras.PesosConCifra(18_117m, mayusculas: false)
            .Should().Be("dieciocho mil ciento diecisiete pesos dominicanos con 00/100 (RD$18,117.00)");
    }

    // ================= con cifra al lado =================

    [Fact]
    public void NumeroConSuCifra() =>
        NumeroALetras.ConCifra(24).Should().Be("Veinticuatro (24)");

    [Theory]
    [InlineData(2, "Dos (02)")]     // "de dos (02) cuotas"
    [InlineData(5, "Cinco (05)")]   // "una gracia de cinco (05) días"
    [InlineData(15, "Quince (15)")]
    public void NumeroConCifraDeDosDigitos(long numero, string esperado) =>
        NumeroALetras.ConCifraDosDigitos(numero).Should().Be(esperado);

    [Theory]
    [InlineData(5, "Cinco (05%)")]          // "una tasa de un Cinco (05%)"
    [InlineData(20, "Veinte (20%)")]        // "una mora de un veinte por ciento (20%)"
    [InlineData(10, "Diez (10%)")]
    public void PorcentajeEntero(int valor, string esperado) =>
        NumeroALetras.Porcentaje(valor).Should().Be(esperado);

    [Fact]
    public void PorcentajeConDecimales() =>
        NumeroALetras.Porcentaje(2.5m).Should().Be("Dos punto cinco (2.5%)");

    // ================= fechas =================

    [Fact]
    public void LaFechaDeAperturaDelActa()
    {
        // "a los Tres (3) días del mes de abril del año Dos Mil Veintiséis (2026)"
        NumeroALetras.FechaLarga(new DateOnly(2026, 4, 3))
            .Should().Be("a los Tres (3) días del mes de abril del año Dos Mil Veintiséis (2026)");
    }

    [Fact]
    public void LaFechaDelPrimerPago()
    {
        // "iniciando los pagos en fecha Tres (3) del mes de Mayo del año 2026"
        NumeroALetras.FechaEnTexto(new DateOnly(2026, 5, 3))
            .Should().Be("el Tres (3) de mayo del año Dos Mil Veintiséis (2026)");
    }

    [Fact]
    public void TodosLosMesesTienenNombre()
    {
        for (var mes = 1; mes <= 12; mes++)
            NumeroALetras.FechaLarga(new DateOnly(2026, mes, 1))
                .Should().NotContain("  ", $"el mes {mes} tiene que tener nombre");
    }
}
