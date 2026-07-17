using FluentAssertions;
using FAControl.Models;
using FAControl.Services;
using Xunit;

namespace FAControl.Services.Tests;

/// <summary>Pruebas de la agrupación semanal del reporte Ingresos por período.</summary>
public class ReporteServiceTests
{
    [Fact]
    public void AgruparPorSemana_MesDe30Dias_Genera5BucketsYSumaExacta()
    {
        var desde = new DateOnly(2026, 6, 1);
        var hasta = new DateOnly(2026, 6, 30);
        var porDia = new List<IngresoDiario>
        {
            new(new DateOnly(2026, 6, 3), Interes: 100m, Capital: 200m, Total: 300m),   // sem. 1
            new(new DateOnly(2026, 6, 10), Interes: 50m, Capital: 150m, Total: 200m),   // sem. 2
            new(new DateOnly(2026, 6, 30), Interes: 25m, Capital: 75m, Total: 100m)     // sem. 5
        };

        var semanas = ReporteService.AgruparPorSemana(porDia, desde, hasta);

        semanas.Should().HaveCount(5); // 01–07, 08–14, 15–21, 22–28, 29–30
        semanas[0].Interes.Should().Be(100m);
        semanas[0].Capital.Should().Be(200m);
        semanas[1].Total.Should().Be(200m);
        semanas[4].Desde.Should().Be(new DateOnly(2026, 6, 29));
        semanas[4].Hasta.Should().Be(new DateOnly(2026, 6, 30)); // última semana recortada
        semanas[4].Interes.Should().Be(25m);
        semanas.Sum(s => s.Total).Should().Be(porDia.Sum(d => d.Total));
    }

    [Fact]
    public void AgruparPorSemana_UnSoloDia_UnBucketDeUnDia()
    {
        var dia = new DateOnly(2026, 7, 10);
        var semanas = ReporteService.AgruparPorSemana([], dia, dia);

        semanas.Should().HaveCount(1);
        semanas[0].Desde.Should().Be(dia);
        semanas[0].Hasta.Should().Be(dia);
        semanas[0].Total.Should().Be(0m);
    }
}
