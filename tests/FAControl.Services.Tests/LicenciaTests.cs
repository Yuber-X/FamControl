using FAControl.Common;
using FAControl.Services;
using FluentAssertions;
using Xunit;

namespace FAControl.Services.Tests;

/// <summary>
/// Licencia local y códigos del launcher (pedido del cliente 2026-07-27).
///
/// A propósito NO hay tests con los códigos reales: si estuvieran acá, estarían
/// en el repositorio y el hash del binario no serviría de nada. Lo que se prueba
/// es la máquina de estados de la prueba/activación (que es donde se rompe el
/// negocio) y que un código cualquiera NO habilita nada.
/// El mapeo código → acción se verifica a mano con el MD de códigos.
/// </summary>
public class LicenciaTests
{
    private static readonly DateTime Ahora = new(2026, 7, 27, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Instalacion_nueva_esta_sin_activar_y_no_deja_usar()
    {
        var licencia = new LicenciaLocal();

        licencia.EstadoEn(Ahora).Should().Be(EstadoLicencia.SinActivar);
        licencia.PermiteUsar(Ahora).Should().BeFalse();
        licencia.DiasRestantesEn(Ahora).Should().Be(0);
    }

    [Fact]
    public void Con_la_prueba_recien_iniciada_quedan_los_14_dias()
    {
        var licencia = new LicenciaLocal { PruebaIniciadaUtc = Ahora };

        licencia.EstadoEn(Ahora).Should().Be(EstadoLicencia.EnPrueba);
        licencia.PermiteUsar(Ahora).Should().BeTrue();
        licencia.DiasRestantesEn(Ahora).Should().Be(LicenciaLocal.DiasDePrueba);
    }

    /// <summary>El último día todavía se puede trabajar: el corte es a los 14 días exactos.</summary>
    [Fact]
    public void El_ultimo_dia_de_prueba_todavia_permite_usar()
    {
        var licencia = new LicenciaLocal { PruebaIniciadaUtc = Ahora };
        var casiVencida = Ahora.AddDays(LicenciaLocal.DiasDePrueba).AddMinutes(-1);

        licencia.EstadoEn(casiVencida).Should().Be(EstadoLicencia.EnPrueba);
        licencia.PermiteUsar(casiVencida).Should().BeTrue();
        licencia.DiasRestantesEn(casiVencida).Should().Be(1);
    }

    [Fact]
    public void Pasados_los_14_dias_la_prueba_vence_y_bloquea()
    {
        var licencia = new LicenciaLocal { PruebaIniciadaUtc = Ahora };
        var vencida = Ahora.AddDays(LicenciaLocal.DiasDePrueba);

        licencia.EstadoEn(vencida).Should().Be(EstadoLicencia.PruebaVencida);
        licencia.PermiteUsar(vencida).Should().BeFalse();
        licencia.DiasRestantesEn(vencida).Should().Be(0);
    }

    /// <summary>Activado manda sobre todo: aunque la prueba haya vencido hace meses.</summary>
    [Fact]
    public void Activada_manda_sobre_la_prueba_vencida()
    {
        var licencia = new LicenciaLocal
        {
            PruebaIniciadaUtc = Ahora.AddDays(-90),
            Activada = true,
            ActivadaUtc = Ahora
        };

        licencia.EstadoEn(Ahora).Should().Be(EstadoLicencia.Activada);
        licencia.PermiteUsar(Ahora).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("FA-0000-0000-0000")]
    [InlineData("cualquier cosa")]
    public void Un_codigo_que_no_es_no_habilita_nada(string codigo)
    {
        LicenciaService.Reconocer(codigo).Should().Be(AccionCodigo.Invalido);

        var licencia = new LicenciaLocal();
        var resultado = new LicenciaService(licencia).Aplicar(codigo);

        resultado.Aceptado.Should().BeFalse();
        licencia.PruebaIniciadaUtc.Should().BeNull("un código inválido no puede arrancar la prueba");
        licencia.Activada.Should().BeFalse();
    }

    [Fact]
    public void Reconocer_null_no_revienta()
    {
        LicenciaService.Reconocer(null).Should().Be(AccionCodigo.Invalido);
    }
}
