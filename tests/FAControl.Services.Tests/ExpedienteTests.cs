using FAControl.Common;
using FAControl.Services;
using FluentAssertions;
using Xunit;

namespace FAControl.Services.Tests;

/// <summary>
/// Puertas del expediente digital (018). Lo que se prueba acá es lo que
/// protege al equipo del cliente: que no entre un ejecutable disfrazado de
/// documento y que sin permiso no se vea nada.
///
/// Las validaciones corren ANTES de tocar la BD, por eso se puede construir el
/// servicio con repositorios null.
/// </summary>
[Collection(ColeccionSesion.Nombre)]   // SesionActual es global
public class ExpedienteTests : IDisposable
{
    private readonly ExpedienteService _servicio = new(null!, null!, new AjustesLocales());
    private readonly List<string> _temporales = [];

    public void Dispose()
    {
        SesionActual.Cerrar();
        foreach (var ruta in _temporales.Where(File.Exists))
            File.Delete(ruta);
    }

    private static void IniciarComoVendedor() =>
        SesionActual.Iniciar(1, "vendedor", "Vendedor", Roles.Vendedor,
            [Permisos.Ventas, Permisos.Inventario], DateTime.UtcNow, 1);

    private string CrearArchivo(string extension)
    {
        var ruta = Path.Combine(Path.GetTempPath(), $"facontrol_test_{Guid.NewGuid():N}{extension}");
        File.WriteAllText(ruta, "contenido de prueba");
        _temporales.Add(ruta);
        return ruta;
    }

    /// <summary>La razón de ser de la lista blanca: el expediente se abre con doble clic.</summary>
    [Theory]
    [InlineData(".exe")]
    [InlineData(".bat")]
    [InlineData(".ps1")]
    [InlineData(".lnk")]
    public async Task No_deja_subir_ejecutables_al_expediente(string extension)
    {
        IniciarComoVendedor();
        var archivo = CrearArchivo(extension);

        var accion = () => _servicio.AgregarAsync(1, archivo);

        await accion.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no se permite*");
    }

    [Fact]
    public async Task Un_archivo_que_no_existe_da_un_mensaje_claro()
    {
        IniciarComoVendedor();

        var accion = () => _servicio.AgregarAsync(1, @"C:\no\existe\cedula.pdf");

        await accion.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No encuentro el archivo*");
    }

    [Fact]
    public async Task Sin_permiso_no_se_ve_ni_se_sube_nada()
    {
        SesionActual.Iniciar(2, "curioso", "Curioso", Roles.Cobrador,
            [Permisos.Cobros], DateTime.UtcNow, 2);

        var ver = () => _servicio.ObtenerAsync(1);
        var subir = () => _servicio.AgregarAsync(1, CrearArchivo(".pdf"));

        await ver.Should().ThrowAsync<UnauthorizedAccessException>();
        await subir.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    /// <summary>Eliminar y re-ubicar son exclusivos del Admin (regla del cliente).</summary>
    [Fact]
    public async Task Eliminar_y_reubicar_son_solo_del_admin()
    {
        IniciarComoVendedor();

        var eliminar = () => _servicio.EliminarAsync(1);
        var mover = () => _servicio.MoverAsync(1, 2);

        await eliminar.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*administrador*");
        await mover.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*administrador*");
    }

    /// <summary>Lo que el cliente pidió poder guardar tiene que estar permitido.</summary>
    [Theory]
    [InlineData(".zip")]
    [InlineData(".rar")]
    [InlineData(".docx")]
    [InlineData(".xlsx")]
    [InlineData(".jpg")]
    [InlineData(".png")]
    [InlineData(".heic")]   // fotos de iPhone
    [InlineData(".pdf")]
    public void Los_formatos_que_pidio_el_cliente_estan_permitidos(string extension)
    {
        ExpedienteService.ExtensionesPermitidas.Should().Contain(extension);
    }

    [Fact]
    public void La_carpeta_por_defecto_va_junto_al_ejecutable()
    {
        var raiz = ExpedienteService.CarpetaRaiz(new AjustesLocales());

        raiz.Should().EndWith("expedientes");
    }

    [Fact]
    public void La_carpeta_se_puede_mudar_a_otra_unidad()
    {
        var ajustes = new AjustesLocales { CarpetaExpedientes = @"D:\FAControl\expedientes" };

        ExpedienteService.CarpetaRaiz(ajustes).Should().Be(@"D:\FAControl\expedientes");
    }
}
