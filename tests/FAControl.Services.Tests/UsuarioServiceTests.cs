using FAControl.Common;
using FAControl.Models;
using FAControl.Services;
using FluentAssertions;
using Xunit;

namespace FAControl.Services.Tests;

/// <summary>
/// Reglas de autorización de UsuarioService. Son las más caras de romper:
/// si fallan, un cobrador podría crearse un Admin.
///
/// No tocan la BD: ExigirAdmin() corta ANTES de llegar al repositorio, así que
/// basta con manipular SesionActual. Por eso se puede pasar null! como repos.
/// </summary>
[Collection(ColeccionSesion.Nombre)]   // SesionActual es global: ver ColeccionSesion
public class UsuarioServiceTests : IDisposable
{
    private readonly UsuarioService _servicio = new(null!, null!);

    public void Dispose() => SesionActual.Cerrar();

    private static void IniciarComo(string rol) =>
        SesionActual.Iniciar(1, "u", "Usuario", rol, Permisos.Todos, DateTime.UtcNow, 1);

    [Theory]
    [InlineData(Roles.Supervisor)]
    [InlineData(Roles.Cobrador)]
    public async Task CrearAsync_sin_ser_admin_es_rechazado(string rol)
    {
        IniciarComo(rol);

        var accion = () => _servicio.CrearAsync("nuevo", "Nuevo", null,
            new RolesUsuario(true, null, null, null), "password123");

        await accion.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*administrador*");
    }

    [Theory]
    [InlineData(Roles.Supervisor)]
    [InlineData(Roles.Cobrador)]
    public async Task RestablecerPasswordAsync_sin_ser_admin_es_rechazado(string rol)
    {
        IniciarComo(rol);

        var accion = () => _servicio.RestablecerPasswordAsync(2, "password123");

        await accion.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task ObtenerTodosAsync_sin_ser_admin_es_rechazado()
    {
        IniciarComo(Roles.Cobrador);

        var accion = () => _servicio.ObtenerTodosAsync();

        await accion.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task ObtenerRolesDeUsuario_sin_ser_admin_es_rechazado()
    {
        IniciarComo(Roles.Cobrador);

        var accion = () => _servicio.ObtenerRolesDeUsuarioAsync(2);

        await accion.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task Sin_sesion_activa_tambien_es_rechazado()
    {
        SesionActual.Cerrar();

        var accion = () => _servicio.ObtenerTodosAsync();

        await accion.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    /// <summary>
    /// Siendo Admin, ExigirAdmin deja pasar: falla DESPUÉS, al usar el
    /// repositorio null. Que la excepción NO sea UnauthorizedAccess prueba
    /// que la puerta se abrió.
    /// </summary>
    [Fact]
    public async Task Siendo_admin_la_autorizacion_deja_pasar()
    {
        IniciarComo(Roles.Admin);

        var accion = () => _servicio.ObtenerTodosAsync();

        await accion.Should().NotThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task Password_corta_es_rechazada_antes_de_tocar_la_bd()
    {
        IniciarComo(Roles.Admin);

        var accion = () => _servicio.CrearAsync("nuevo", "Nuevo", null,
            new RolesUsuario(true, null, null, null), "corta");

        await accion.Should().ThrowAsync<ArgumentException>()
            .WithMessage($"*{UsuarioService.MinLargoPassword}*");
    }

    [Fact]
    public void EsAdmin_distingue_los_roles()
    {
        IniciarComo(Roles.Admin);
        SesionActual.EsAdmin.Should().BeTrue();

        IniciarComo(Roles.Supervisor);
        SesionActual.EsAdmin.Should().BeFalse();
    }

    // ---------- Rol PROGRAMADOR (017, cliente 2026-07-27) ----------

    /// <summary>El Programador manda sobre todo: es admin y además intocable.</summary>
    [Fact]
    public void Programador_es_admin_y_se_distingue_del_admin()
    {
        IniciarComo(Roles.Programador);
        SesionActual.EsProgramador.Should().BeTrue();
        SesionActual.EsAdmin.Should().BeTrue();

        IniciarComo(Roles.Admin);
        SesionActual.EsProgramador.Should().BeFalse();
        SesionActual.EsAdmin.Should().BeTrue();
    }

    /// <summary>
    /// La regla central del pedido: ni siquiera el Admin puede CREAR un
    /// Programador. Corta antes de tocar la BD (repos null), así que si la
    /// excepción no fuera UnauthorizedAccess el test explotaría con otra.
    /// </summary>
    [Fact]
    public async Task Un_admin_no_puede_crear_un_programador()
    {
        IniciarComo(Roles.Admin);

        var accion = () => _servicio.CrearAsync("dev", "Dev", null,
            new RolesUsuario(false, null, null, null, null, EsProgramador: true), "password123");

        await accion.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*Programador*");
    }

    [Fact]
    public async Task Un_admin_no_puede_promover_a_programador_editando()
    {
        IniciarComo(Roles.Admin);

        var accion = () => _servicio.ActualizarAsync(2, "Dev", null,
            new RolesUsuario(false, null, null, null, null, EsProgramador: true), activo: true);

        await accion.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*Programador*");
    }

    /// <summary>Otro Programador sí puede: la puerta se abre y falla más adelante (repos null).</summary>
    [Fact]
    public async Task Un_programador_si_puede_crear_otro_programador()
    {
        IniciarComo(Roles.Programador);

        var accion = () => _servicio.CrearAsync("dev2", "Dev", null,
            new RolesUsuario(false, null, null, null, null, EsProgramador: true), "password123");

        await accion.Should().NotThrowAsync<UnauthorizedAccessException>();
    }

    /// <summary>Marcar solo Programador ya es acceso suficiente (no exige rol por modo).</summary>
    [Fact]
    public async Task Programador_no_necesita_rol_por_modo()
    {
        IniciarComo(Roles.Programador);

        var accion = () => _servicio.CrearAsync("dev3", "Dev", null,
            new RolesUsuario(false, null, null, null, null, EsProgramador: true), "password123");

        await accion.Should().NotThrowAsync<ArgumentException>();
    }

    [Fact]
    public void TienePermiso_solo_devuelve_los_otorgados()
    {
        SesionActual.Iniciar(1, "cobrador", "Cobrador", Roles.Cobrador,
            [Permisos.Panel, Permisos.Cobros], DateTime.UtcNow, 1);

        SesionActual.TienePermiso(Permisos.Cobros).Should().BeTrue();
        SesionActual.TienePermiso(Permisos.PrestamosCrear).Should().BeFalse();
        SesionActual.TienePermiso(Permisos.Usuarios).Should().BeFalse();
    }

    [Fact]
    public void Cerrar_borra_los_permisos()
    {
        IniciarComo(Roles.Admin);
        SesionActual.Cerrar();

        SesionActual.EsAdmin.Should().BeFalse();
        SesionActual.TienePermiso(Permisos.Panel).Should().BeFalse();
        SesionActual.HaySesionActiva.Should().BeFalse();
    }
}
