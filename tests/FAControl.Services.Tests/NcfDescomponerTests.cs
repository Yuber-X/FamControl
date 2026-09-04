using FAControl.Models;
using FluentAssertions;
using Xunit;

namespace FAControl.Services.Tests;

/// <summary>
/// Lectura de un NCF digitado a mano (pedido del cliente 2026-09-03): partirlo
/// en serie + número para poder adoptarlo como secuencia predeterminada.
///
/// Es lógica pura y por eso vive acá; que la adopción efectivamente mueva la
/// secuencia se prueba contra MySQL en <c>NcfPredeterminadoTests</c>.
/// </summary>
public class NcfDescomponerTests
{
    [Theory]
    [InlineData("B0200000045", "B02", 45L, 8)]
    [InlineData("B0100000001", "B01", 1L, 8)]
    [InlineData("E320000000012", "E32", 12L, 10)]
    [InlineData("B0212345678", "B02", 12345678L, 8)]
    public void Descompone_UnNcfValido(string ncf, string prefijo, long numero, int largo)
    {
        var partes = NcfSecuencia.Descomponer(ncf);

        partes.Should().NotBeNull();
        partes!.Value.Prefijo.Should().Be(prefijo);
        partes.Value.Numero.Should().Be(numero);
        partes.Value.Largo.Should().Be(largo);
    }

    [Theory]
    [InlineData("b0200000045")]        // el cajero escribe en minúsculas
    [InlineData("  B0200000045  ")]    // pegado desde el Facturador, con espacios
    public void NormalizaMayusculasYEspacios(string ncf)
    {
        var partes = NcfSecuencia.Descomponer(ncf);

        partes.Should().NotBeNull();
        partes!.Value.Prefijo.Should().Be("B02");
        partes.Value.Numero.Should().Be(45);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("B02")]                    // solo la serie
    [InlineData("00000045")]               // solo el número
    [InlineData("B0-200000045")]           // con separadores
    [InlineData("BB200000045")]            // dos letras
    [InlineData("B0200045")]               // menos de 6 dígitos de secuencia
    [InlineData("B020000000000045")]       // más de 12
    [InlineData("A010010011500000001")]    // formato anterior a 2018 (serie A)
    public void NoDescompone_LoQueNoTieneFormaDeComprobante(string? ncf)
    {
        // Devolver null es a propósito: adivinar la numeración de un libro de
        // ventas a partir de un texto raro es peor que no tocar nada.
        NcfSecuencia.Descomponer(ncf).Should().BeNull();
    }

    [Fact]
    public void LoDescompuesto_VuelveAFormatearseIgual()
    {
        const string original = "E320000000012";
        var partes = NcfSecuencia.Descomponer(original)!.Value;

        var secuencia = new NcfSecuencia { Prefijo = partes.Prefijo, Largo = partes.Largo };

        secuencia.Formatear(partes.Numero).Should().Be(original);
    }
}
