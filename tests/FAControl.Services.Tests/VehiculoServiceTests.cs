using FluentAssertions;
using FAControl.Models;
using FAControl.Services;
using Xunit;

namespace FAControl.Services.Tests;

/// <summary>
/// Pruebas de la validación/normalización pura de vehículos (sin BD) y de la
/// matemática de costo/ganancia del modelo.
/// </summary>
public class VehiculoServiceTests
{
    private static VehiculoDatos Datos(
        string marca = "Toyota", string modelo = "Corolla", int? anio = 2020,
        string? vin = null, decimal costo = 500000m, decimal importacion = 50000m,
        decimal precio = 700000m, string? color = null, string? placa = null) =>
        new(vin, marca, modelo, anio, color, placa, TipoVehiculo.Sedan, null,
            costo, importacion, precio, null);

    [Fact]
    public void Normalizar_DatosValidos_RecortaYRedondea()
    {
        var datos = Datos(marca: "  Honda ", modelo: " CR-V ", color: "  Rojo  ",
            costo: 800000.125m, importacion: 100000.004m, precio: 1050000.006m);

        var r = VehiculoService.Normalizar(datos);

        r.Marca.Should().Be("Honda");
        r.Modelo.Should().Be("CR-V");
        r.Color.Should().Be("Rojo");
        r.CostoAdquisicion.Should().Be(800000.13m);   // AwayFromZero
        r.GastosImportacion.Should().Be(100000.00m);
        r.PrecioVenta.Should().Be(1050000.01m);
    }

    [Fact]
    public void Normalizar_VinYPlaca_SeGuardanEnMayusculas()
    {
        var r = VehiculoService.Normalizar(Datos(vin: "jt2bg22k1w0123456", placa: "a123456"));
        r.Vin.Should().Be("JT2BG22K1W0123456");
        r.Placa.Should().Be("A123456");
    }

    [Theory]
    [InlineData("", "Corolla", "*marca*")]
    [InlineData("   ", "Corolla", "*marca*")]
    [InlineData("Toyota", "", "*modelo*")]
    public void Normalizar_MarcaOModeloVacios_Lanza(string marca, string modelo, string mensaje)
    {
        var accion = () => VehiculoService.Normalizar(Datos(marca: marca, modelo: modelo));
        accion.Should().Throw<ArgumentException>().WithMessage(mensaje);
    }

    [Theory]
    [InlineData(1899)]
    [InlineData(3000)]
    public void Normalizar_AnioFueraDeRango_Lanza(int anio)
    {
        var accion = () => VehiculoService.Normalizar(Datos(anio: anio));
        accion.Should().Throw<ArgumentException>().WithMessage("*año*");
    }

    [Fact]
    public void Normalizar_MontoNegativo_Lanza()
    {
        var accion = () => VehiculoService.Normalizar(Datos(costo: -1m));
        accion.Should().Throw<ArgumentException>().WithMessage("*negativos*");
    }

    [Fact]
    public void Normalizar_VinDemasiadoLargo_Lanza()
    {
        var accion = () => VehiculoService.Normalizar(Datos(vin: new string('A', 18)));
        accion.Should().Throw<ArgumentException>().WithMessage("*17*");
    }

    [Fact]
    public void Normalizar_SinAnio_EsValido()
    {
        var r = VehiculoService.Normalizar(Datos(anio: null));
        r.Anio.Should().BeNull();
    }

    [Fact]
    public void Vehiculo_CostoTotalYGanancia_SeCalculan()
    {
        var v = new Vehiculo { CostoAdquisicion = 500000m, GastosImportacion = 80000m, PrecioVenta = 700000m };
        v.CostoTotal.Should().Be(580000m);
        v.GananciaEstimada.Should().Be(120000m);
    }

    [Fact]
    public void Vehiculo_PrecioBajoElCosto_GananciaNegativa()
    {
        var v = new Vehiculo { CostoAdquisicion = 500000m, GastosImportacion = 80000m, PrecioVenta = 550000m };
        v.GananciaEstimada.Should().Be(-30000m);
    }

    [Fact]
    public void Vehiculo_Descripcion_IncluyeAnioCuandoExiste()
    {
        new Vehiculo { Marca = "Kia", Modelo = "Sportage", Anio = 2019 }.Descripcion.Should().Be("Kia Sportage 2019");
        new Vehiculo { Marca = "Kia", Modelo = "Sportage", Anio = null }.Descripcion.Should().Be("Kia Sportage");
    }
}
