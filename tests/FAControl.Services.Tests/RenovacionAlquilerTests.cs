using FluentAssertions;
using FAControl.Models;
using FAControl.Services;

namespace FAControl.Services.Tests;

/// <summary>
/// Renovación de alquileres (039).
///
/// Pedido de Yuber (2026-08-01): "cuando se cumple el tiempo pactado del
/// alquiler, el auto debe volver a estar disponible o preguntar si el cliente
/// seguirá con el alquiler... habrá que actualizar su fecha de devolución según
/// el usuario confirme la nueva fecha y precio nuevo o el mismo".
///
/// Lo delicado es EL PRECIO NUEVO. Si la renovación solo corriera la fecha, el
/// monto del contrato se recalcularía entero a la tarifa nueva y le cambiaría
/// el precio a días que el cliente ya usó, y quizás ya pagó. Por eso cada
/// renovación es un TRAMO con su tarifa, y acá se prueba esa cuenta: es la que
/// decide cuánto se le cobra al cliente al cerrar.
/// </summary>
public class RenovacionAlquilerTests
{
    /// <summary>
    /// 15 días desde el 1 de julio, a 2,000 el día. Termina el 16 y no el 15:
    /// CalcularDias cuenta los días facturables, y el día en que se devuelve el
    /// auto no se cobra.
    /// </summary>
    private static Alquiler Contrato() => new()
    {
        Id = 1,
        Codigo = "AL-0001",
        FechaInicio = new DateOnly(2026, 7, 1),
        FechaFin = new DateOnly(2026, 7, 16),
        TarifaDia = 2_000m,
        Dias = 15,
        MontoTotal = 30_000m,
        Estado = EstadoAlquiler.Activo
    };

    private static AlquilerRenovacion Tramo(DateOnly anterior, DateOnly nueva, decimal tarifa) => new()
    {
        AlquilerId = 1,
        FechaFinAnterior = anterior,
        FechaFinNueva = nueva,
        TarifaDia = tarifa,
        Dias = nueva.DayNumber - anterior.DayNumber,
        Monto = tarifa * (nueva.DayNumber - anterior.DayNumber)
    };

    // ---------- Sin renovaciones: nada cambia ----------

    /// <summary>
    /// La cuenta vieja era `tarifa × días`. Un contrato que nunca se renovó
    /// tiene que dar exactamente lo mismo que antes de 039, o la migración le
    /// habría movido el precio a todos los alquileres existentes.
    /// </summary>
    [Theory]
    [InlineData(15, 30_000)]   // devolvió el día pactado
    [InlineData(10, 20_000)]   // devolvió antes
    [InlineData(18, 36_000)]   // devolvió tarde
    public void SinRenovaciones_EsTarifaPorDias(int diasUsados, decimal esperado) =>
        AlquilerService.CalcularMonto(Contrato(), [], diasUsados).Should().Be(esperado);

    [Fact]
    public void CeroDias_NoSeCobra() =>
        AlquilerService.CalcularMonto(Contrato(), [], 0).Should().Be(0m);

    // ---------- Renovación a la misma tarifa ----------

    [Fact]
    public void ALaMismaTarifa_EsElTotalDeSiempre()
    {
        // 15 días + 10 más, todo a 2,000
        var renovaciones = new[]
        {
            Tramo(new DateOnly(2026, 7, 16), new DateOnly(2026, 7, 26), 2_000m)
        };

        AlquilerService.CalcularMonto(Contrato(), renovaciones, 25).Should().Be(50_000m);
    }

    // ---------- Renovación a otra tarifa: el corazón del asunto ----------

    /// <summary>
    /// Los 15 días del primer tramo se cobran a 2,000 aunque la renovación haya
    /// venido a 2,500. Si se recalculara todo a la tarifa nueva darían 62,500 y
    /// el cliente pagaría 7,500 de más por días que ya usó al precio viejo.
    /// </summary>
    [Fact]
    public void ConTarifaNueva_LosDiasViejosSeCobranAlPrecioViejo()
    {
        var renovaciones = new[]
        {
            Tramo(new DateOnly(2026, 7, 16), new DateOnly(2026, 7, 26), 2_500m)
        };

        // 15 × 2,000 + 10 × 2,500 = 30,000 + 25,000
        AlquilerService.CalcularMonto(Contrato(), renovaciones, 25).Should().Be(55_000m);
    }

    /// <summary>
    /// Devolvió en medio del tramo renovado: se pagan los 15 del primero y solo
    /// los 4 que usó del segundo.
    /// </summary>
    [Fact]
    public void DevolvioEnMedioDelTramoNuevo_SoloPagaLosDiasQueUso()
    {
        var renovaciones = new[]
        {
            Tramo(new DateOnly(2026, 7, 16), new DateOnly(2026, 7, 26), 2_500m)
        };

        // 15 × 2,000 + 4 × 2,500 = 30,000 + 10,000
        AlquilerService.CalcularMonto(Contrato(), renovaciones, 19).Should().Be(40_000m);
    }

    /// <summary>
    /// Devolvió antes de que terminara el primer tramo: la renovación no llegó a
    /// correr y no se cobra ni un día de ella.
    /// </summary>
    [Fact]
    public void DevolvioAntesDelPrimerVencimiento_LaRenovacionNoSeCobra()
    {
        var renovaciones = new[]
        {
            Tramo(new DateOnly(2026, 7, 16), new DateOnly(2026, 7, 26), 2_500m)
        };

        AlquilerService.CalcularMonto(Contrato(), renovaciones, 8).Should().Be(16_000m);
    }

    [Fact]
    public void VariasRenovaciones_CadaTramoASuPrecio()
    {
        var renovaciones = new[]
        {
            Tramo(new DateOnly(2026, 7, 16), new DateOnly(2026, 7, 26), 2_500m),   // 10 días
            Tramo(new DateOnly(2026, 7, 26), new DateOnly(2026, 8, 5), 1_800m)     // 10 días
        };

        // 15 × 2,000 + 10 × 2,500 + 10 × 1,800 = 30,000 + 25,000 + 18,000
        AlquilerService.CalcularMonto(Contrato(), renovaciones, 35).Should().Be(73_000m);
    }

    /// <summary>
    /// Se pasó del último tramo: esos días van a la tarifa VIGENTE, que es la
    /// última que el cliente aceptó. Cobrarlos a la original sería regalarle (o
    /// cobrarle de más) por no haber devuelto a tiempo.
    /// </summary>
    [Fact]
    public void DevolvioTardeDespuesDeRenovar_LosDiasDeMasVanALaTarifaVigente()
    {
        var renovaciones = new[]
        {
            Tramo(new DateOnly(2026, 7, 16), new DateOnly(2026, 7, 26), 2_500m)
        };

        // 15 × 2,000 + 10 × 2,500 + 3 × 2,500 (atraso) = 30,000 + 25,000 + 7,500
        AlquilerService.CalcularMonto(Contrato(), renovaciones, 28).Should().Be(62_500m);
    }

    // ---------- Tarifa vigente ----------

    [Fact]
    public void SinRenovar_LaVigenteEsLaOriginal() =>
        AlquilerService.TarifaVigente(Contrato(), []).Should().Be(2_000m);

    [Fact]
    public void LaVigenteEsLaDelUltimoTramo()
    {
        var renovaciones = new[]
        {
            Tramo(new DateOnly(2026, 7, 16), new DateOnly(2026, 7, 26), 2_500m),
            Tramo(new DateOnly(2026, 7, 26), new DateOnly(2026, 8, 5), 1_800m)
        };

        AlquilerService.TarifaVigente(Contrato(), renovaciones).Should().Be(1_800m);
    }

    /// <summary>
    /// El redondeo es UNO SOLO, al final. Tramo por tramo acumularía centavos
    /// que no cuadrarían con la suma de los cobros del contrato.
    /// </summary>
    [Fact]
    public void SeRedondeaUnaSolaVez_AlFinal()
    {
        var contrato = Contrato();
        contrato.TarifaDia = 1_333.333m;
        var renovaciones = new[]
        {
            Tramo(new DateOnly(2026, 7, 16), new DateOnly(2026, 7, 19), 1_666.666m)
        };

        // 15 × 1,333.333 + 3 × 1,666.666 = 19,999.995 + 4,999.998 = 24,999.993
        AlquilerService.CalcularMonto(contrato, renovaciones, 18).Should().Be(24_999.99m);
    }
}
