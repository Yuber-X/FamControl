using FluentAssertions;
using FAControl.Services;
using Xunit;

namespace FAControl.Services.Tests;

/// <summary>Pruebas de la normalización de cédula (lógica pura, sin BD).</summary>
public class ClienteServiceTests
{
    [Theory]
    [InlineData("00112345678", "001-1234567-8")]
    [InlineData("001-1234567-8", "001-1234567-8")]
    [InlineData("001 1234567 8", "001-1234567-8")]
    [InlineData(" 00112345678 ", "001-1234567-8")]
    public void NormalizarCedula_OnceDigitos_FormateaComoCedulaDominicana(string entrada, string esperado)
    {
        ClienteService.NormalizarCedula(entrada).Should().Be(esperado);
    }

    [Theory]
    [InlineData("PA1234567")]      // pasaporte
    [InlineData("X-99887766")]     // documento extranjero
    public void NormalizarCedula_OtroDocumento_SeAceptaTalCual(string entrada)
    {
        ClienteService.NormalizarCedula(entrada).Should().Be(entrada);
    }

    [Fact]
    public void NormalizarCedula_Vacia_Lanza()
    {
        var accion = () => ClienteService.NormalizarCedula("   ");
        accion.Should().Throw<ArgumentException>().WithMessage("*obligatoria*");
    }

    [Fact]
    public void NormalizarCedula_DocumentoMuyLargo_Lanza()
    {
        var accion = () => ClienteService.NormalizarCedula("ABCDEFGHIJKLMN"); // 14 caracteres
        accion.Should().Throw<ArgumentException>().WithMessage("*13 caracteres*");
    }
}
