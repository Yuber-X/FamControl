using FAControl.Common;
using FAControl.Models;
using FAControl.Services;
using FluentAssertions;
using Xunit;

namespace FAControl.Services.Tests;

/// <summary>
/// La regla más delicada del proyecto (cliente 2026-07-16): "si el admin no da
/// autorizacion, no se permite el nuevo prestamo".
///
/// Se prueba contra PrestamoService y NO contra el ViewModel a propósito: la
/// UI puede olvidarse de pedir la autorización, el servicio no. Si estos tests
/// pasan, un cobrador no puede colocar dinero sin aprobación aunque la pantalla
/// tenga un bug.
///
/// No tocan la BD: los chequeos cortan ANTES del repositorio, por eso se puede
/// pasar null! como dependencias.
/// </summary>
[Collection(ColeccionSesion.Nombre)]   // SesionActual es global: ver ColeccionSesion
public class AutorizacionPrestamoTests : IDisposable
{
    private readonly PrestamoService _prestamos = new(null!, null!, null!, null!, null!, null!, null!, null!);

    private static readonly NuevoPrestamo Solicitud = new(
        ClienteId: 1, MontoCapital: 50_000m, TasaInteresMensual: 5m, PlazoCuotas: 12,
        Modalidad: Modalidad.Mensual, Metodo: MetodoAmortizacion.CuotaFija,
        FechaPrimerPago: new DateOnly(2026, 8, 17), Garantia: null, Notas: null);

    public void Dispose() => SesionActual.Cerrar();

    private static void IniciarComo(string rol, params string[] permisos) =>
        SesionActual.Iniciar(7, "usuario", "Usuario", rol, permisos, DateTime.UtcNow, 1);

    // ------------------------------------------------------------------
    // Sin autorización no hay préstamo
    // ------------------------------------------------------------------

    [Fact]
    public async Task Cobrador_sin_autorizacion_no_puede_crear_prestamo()
    {
        IniciarComo(Roles.Cobrador, Permisos.PrestamosCrear);   // crear sí, autorizar no

        var accion = () => _prestamos.CrearAsync(Solicitud, autorizacion: null);

        await accion.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*autorización de un administrador*");
    }

    [Fact]
    public async Task Sin_permiso_para_crear_ni_siquiera_llega_a_la_autorizacion()
    {
        IniciarComo(Roles.Cobrador, Permisos.Cobros);   // no tiene prestamos_crear

        var accion = () => _prestamos.CrearAsync(Solicitud, autorizacion: null);

        await accion.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*permiso para crear préstamos*");
    }

    [Fact]
    public async Task Sin_sesion_activa_no_se_puede_crear()
    {
        SesionActual.Cerrar();

        var accion = () => _prestamos.CrearAsync(Solicitud, autorizacion: null);

        await accion.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    // ------------------------------------------------------------------
    // Con autorización, o siendo admin, la puerta se abre
    // ------------------------------------------------------------------

    /// <summary>
    /// Que la excepción NO sea UnauthorizedAccess prueba que la autorización
    /// pasó: revienta después, al usar el repositorio null.
    /// </summary>
    [Fact]
    public async Task Cobrador_CON_autorizacion_valida_pasa_el_control()
    {
        IniciarComo(Roles.Cobrador, Permisos.PrestamosCrear);
        var autorizacion = new AutorizacionPrestamo(1, "admin", "La Jefa");

        var accion = () => _prestamos.CrearAsync(Solicitud, autorizacion);

        await accion.Should().NotThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task Admin_no_necesita_autorizacion_aparte()
    {
        IniciarComo(Roles.Admin, Permisos.PrestamosCrear, Permisos.PrestamosAutorizar);

        var accion = () => _prestamos.CrearAsync(Solicitud, autorizacion: null);

        await accion.Should().NotThrowAsync<UnauthorizedAccessException>();
    }

    // ------------------------------------------------------------------
    // Quién puede autorizar
    // ------------------------------------------------------------------

    [Fact]
    public void UsuarioActualPuedeAutorizar_depende_del_permiso_no_del_rol()
    {
        // Un Supervisor con el permiso otorgado a mano SÍ puede: manda el
        // permiso efectivo, no el nombre del rol.
        IniciarComo(Roles.Supervisor, Permisos.PrestamosCrear, Permisos.PrestamosAutorizar);
        AutorizacionService.UsuarioActualPuedeAutorizar.Should().BeTrue();

        // Y un Admin al que se lo quitaron, no puede.
        IniciarComo(Roles.Admin, Permisos.PrestamosCrear);
        AutorizacionService.UsuarioActualPuedeAutorizar.Should().BeFalse();
    }

    [Theory]
    [InlineData("", "password123")]
    [InlineData("admin", "")]
    [InlineData("   ", "password123")]
    public async Task ValidarAsync_con_credenciales_vacias_no_autoriza(string usuario, string password)
    {
        var servicio = new AutorizacionService(null!, null!);

        // Corta antes de tocar AuthService: por eso null! no explota
        var resultado = await servicio.ValidarAsync(usuario, password);

        resultado.Should().BeNull();
    }
}
