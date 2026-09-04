using FAControl.Models;
using FluentAssertions;
using Xunit;

namespace FAControl.Services.Tests;

/// <summary>
/// Concordancia de género y descripción de las partes del acta notarial (044).
///
/// Esto no es cosmética: la plantilla que mandó el cliente está declinada en
/// género de punta a punta ("dominicano/a", "domiciliado/a", "el señor / la
/// señora", "EL DEUDOR / LA DEUDORA"), y sin resolverlo el documento sale mal
/// escrito la mitad de las veces, delante del notario del cliente.
/// </summary>
public class ActaNotarialTests
{
    // ================= género =================

    [Theory]
    [InlineData(SexoPersona.Masculino, "el señor")]
    [InlineData(SexoPersona.Femenino, "la señora")]
    [InlineData(SexoPersona.NoIndicado, "el señor")]
    public void Tratamiento(SexoPersona sexo, string esperado) =>
        Genero.Tratamiento(sexo).Should().Be(esperado);

    [Theory]
    [InlineData("dominicano", SexoPersona.Masculino, "dominicano")]
    [InlineData("dominicano", SexoPersona.Femenino, "dominicana")]
    [InlineData("dominicano", SexoPersona.NoIndicado, "dominicano")]
    // Un gentilicio que no termina en -o no se toca: "estadounidense" no tiene
    // femenino distinto y "estadounidensa" no existe.
    [InlineData("estadounidense", SexoPersona.Femenino, "estadounidense")]
    [InlineData("haitiano", SexoPersona.Femenino, "haitiana")]
    public void Gentilicio(string nacionalidad, SexoPersona sexo, string esperado) =>
        Genero.Gentilicio(nacionalidad, sexo).Should().Be(esperado);

    [Fact]
    public void SinNacionalidad_SeAsumeDominicano() =>
        Genero.Gentilicio("", SexoPersona.Masculino).Should().Be("dominicano");

    [Theory]
    [InlineData("soltero", SexoPersona.Femenino, "soltera")]
    [InlineData("casado", SexoPersona.Femenino, "casada")]
    [InlineData("soltero", SexoPersona.Masculino, "soltero")]
    // Ya escrito en femenino, no se toca dos veces.
    [InlineData("casada", SexoPersona.Femenino, "casada")]
    [InlineData("", SexoPersona.Femenino, "")]
    public void EstadoCivilConcuerda(string estado, SexoPersona sexo, string esperado) =>
        Genero.EstadoCivil(estado, sexo).Should().Be(esperado);

    [Theory]
    [InlineData(SexoPersona.Masculino, "EL DEUDOR")]
    [InlineData(SexoPersona.Femenino, "LA DEUDORA")]
    [InlineData(SexoPersona.NoIndicado, "EL DEUDOR")]
    public void ComoSeLlamaAlDeudor(SexoPersona sexo, string esperado) =>
        Genero.Deudor(sexo).Should().Be(esperado);

    // ================= descripción de una parte =================

    [Fact]
    public void ElDeudorDeLaPlantilla()
    {
        // "JOSÉ MARTÍNEZ, dominicano, mayor de edad, soltero, comerciante,
        //  portador de la cédula de identidad No. 001-1234567-8, domiciliado y
        //  residente en la calle Hermanos Estrellas número 4…"
        var deudor = new ParteDelActo(
            Nombre: "José Martínez",
            Cedula: "001-1234567-8",
            Sexo: SexoPersona.Masculino,
            Nacionalidad: "dominicano",
            EstadoCivil: "soltero",
            Ocupacion: "comerciante",
            Domicilio: "la calle Hermanos Estrellas número 4, La Vega");

        deudor.Descripcion().Should().Be(
            "JOSÉ MARTÍNEZ, dominicano, mayor de edad, soltero, comerciante, " +
            "portador de la cédula de identidad y electoral No. 001-1234567-8, " +
            "domiciliado y residente en la calle Hermanos Estrellas número 4, La Vega");
    }

    [Fact]
    public void LaRepresentanteDeLaPlantilla_VaEnFemenino()
    {
        var representante = new ParteDelActo(
            Nombre: "Marleny del Carmen Abreu de Familia",
            Cedula: "402-2796799-5",
            Sexo: SexoPersona.Femenino,
            Nacionalidad: "dominicano",
            EstadoCivil: "casado",
            Ocupacion: "comerciante",
            Domicilio: "la calle Jeremías No. 2, La Vega");

        var texto = representante.Descripcion();

        texto.Should().Contain("dominicana");
        texto.Should().Contain("casada");
        texto.Should().Contain("portadora de la cédula");
        texto.Should().Contain("domiciliada y residente");
        texto.Should().NotContain("dominicano,");
    }

    [Fact]
    public void LosDatosQueFaltanNoDejanBasuraImpresa()
    {
        // Un acta con huecos se completa a mano; una con "(sin dato)" impreso
        // hay que rehacerla.
        var parte = new ParteDelActo(Nombre: "Ana Pérez", Cedula: "", Sexo: SexoPersona.Femenino);

        var texto = parte.Descripcion();

        texto.Should().Be("ANA PÉREZ, dominicana, mayor de edad");
        texto.Should().NotContain("cédula");
        texto.Should().NotContain("domiciliada");
        texto.Should().NotContain(", ,");
    }

    [Fact]
    public void ParteVacia_SeReconoce()
    {
        new ParteDelActo("", "").EstaVacia.Should().BeTrue();
        new ParteDelActo("   ", "").EstaVacia.Should().BeTrue();
        new ParteDelActo("Juan", "").EstaVacia.Should().BeFalse();
    }

    // ================= qué le falta al acta =================

    [Fact]
    public void ActaVacia_ListaTodoLoQueFalta()
    {
        var faltan = new DatosNotariales().QueFalta();

        faltan.Should().Contain("el notario");
        faltan.Should().Contain("quién firma por la empresa");
        faltan.Should().Contain("la dirección de la empresa");
        faltan.Should().Contain("el municipio del acto");
        faltan.Should().Contain("la garantía");
        faltan.Should().Contain("los dos testigos");
    }

    [Fact]
    public void ActaCompleta_NoLeFaltaNada()
    {
        var acta = new DatosNotariales
        {
            Municipio = "La Vega",
            Notario = new ParteDelActo("Juan José Castillo", "047-0035382-6"),
            EmpresaDireccion = "calle Manuel Ubaldo Gómez No. 14, La Vega",
            Representante = new ParteDelActo("Marleny Abreu", "402-2796799-5"),
            Garantia = "Un solar de 200 M2, designación catastral No. 401850735326",
            Deudor = new ParteDelActo("José Martínez", "001-1234567-8",
                Domicilio: "calle Hermanos Estrellas No. 4"),
            Testigos =
            [
                new ParteDelActo("Verónica Núñez", "402-1188504-7"),
                new ParteDelActo("Quírico Caminero", "047-0140835-5")
            ]
        };

        acta.QueFalta().Should().BeEmpty();
    }

    [Fact]
    public void UnSoloTestigo_NoAlcanza()
    {
        var acta = new DatosNotariales
        {
            Testigos = [new ParteDelActo("Verónica Núñez", "402-1188504-7")]
        };

        acta.QueFalta().Should().Contain("los dos testigos");
        acta.TestigosConNombre.Should().HaveCount(1);
    }

    [Fact]
    public void LosTestigosSinNombre_NoCuentan()
    {
        var acta = new DatosNotariales
        {
            Testigos = [new ParteDelActo("Verónica Núñez", "402-1188504-7"), new ParteDelActo("", "")]
        };

        acta.TestigosConNombre.Should().HaveCount(1,
            "una fila en blanco en Configuración no es un testigo");
    }

    // ================= los tres documentos =================

    [Fact]
    public void LosTresTipos_TienenNombreYArchivoDistintos()
    {
        var nombres = TiposDeContrato.Todos.Select(TiposDeContrato.Nombre).ToList();
        nombres.Should().OnlyHaveUniqueItems();

        var archivos = TiposDeContrato.Todos
            .Select(t => TiposDeContrato.NombreArchivo(t, "P-0001")).ToList();
        archivos.Should().OnlyHaveUniqueItems(
            "si dos contratos generaran el mismo archivo, uno pisaría al otro en el expediente");
        archivos.Should().AllSatisfy(a => a.Should().Contain("P-0001"));
    }

    [Fact]
    public void ElValorDelEnumNoSeReordena()
    {
        // Se guardan por nombre en ajustes.json, pero el orden importa para la
        // interfaz: cambiar los valores movería los tildes ya guardados.
        ((int)TipoContrato.Pagare).Should().Be(0);
        ((int)TipoContrato.Notarial).Should().Be(1);
        ((int)TipoContrato.Combinado).Should().Be(2);
    }
}
