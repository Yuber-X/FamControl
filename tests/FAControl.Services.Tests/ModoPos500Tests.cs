using FAControl.Common;
using FluentAssertions;
using Xunit;

namespace FAControl.Services.Tests;

/// <summary>
/// El punto de venta como modo de la suite (2026-07-30). Son comprobaciones
/// chicas pero que, si se rompen, rompen cosas grandes: que el código 5 habilite
/// el modo, que el POS tenga su propia base y que sus permisos existan.
/// </summary>
public class ModoPos500Tests
{
    private static readonly DateTime Ahora = new(2026, 7, 30, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// El código 5 guarda el producto "Pos500", y el modo pide ese mismo nombre.
    /// Si alguien cambia uno de los dos, el cliente paga y no puede entrar.
    /// </summary>
    [Fact]
    public void El_codigo_del_pos_habilita_exactamente_el_modo_del_pos()
    {
        ProductosLicencia.De(ModoApp.Pos500).Should().Be(ProductosLicencia.Pos500);

        var licencia = new LicenciaLocal();
        licencia.AgregarProducto(ProductosLicencia.Pos500);

        licencia.PermiteModo(ModoApp.Pos500, Ahora).Should().BeTrue();
        licencia.PermiteModo(ModoApp.PrestControl, Ahora).Should().BeFalse();
        licencia.PermiteModo(ModoApp.DealerControl, Ahora).Should().BeFalse();
    }

    [Fact]
    public void El_pos_aparece_en_el_launcher_y_esta_disponible()
    {
        var pos = IdentidadModo.Todos.SingleOrDefault(m => m.Modo == ModoApp.Pos500);

        pos.Should().NotBeNull();
        pos!.Nombre.Should().Be("POS-500");
        pos.Disponible.Should().BeTrue();
    }

    /// <summary>El único modo cuyos datos NO viven en facontrol_db.</summary>
    [Fact]
    public void Solo_el_pos_usa_la_segunda_base()
    {
        ModoApp.Pos500.UsaBasePos500().Should().BeTrue();
        ModoApp.PrestControl.UsaBasePos500().Should().BeFalse();
        ModoApp.DealerControl.UsaBasePos500().Should().BeFalse();
        ModoApp.Pos500.ClaveDb().Should().Be("pos500");
    }

    [Fact]
    public void El_acceso_al_pos_tiene_su_permiso_y_esta_en_el_catalogo()
    {
        Permisos.AccesoDe(ModoApp.Pos500).Should().Be(Permisos.AccesoPos500);
        Permisos.Todos.Should().Contain(Permisos.AccesoPos500);
    }

    /// <summary>
    /// Los permisos que usan las pantallas del POS tienen que existir en el
    /// catálogo: si falta uno, esa pantalla no aparece en el sidebar de nadie.
    /// </summary>
    [Theory]
    [InlineData("vender")]
    [InlineData("productos")]
    [InlineData("almacen")]
    [InlineData("caducidad")]
    [InlineData("comprobantes")]
    [InlineData("comprobantes_todos")]
    [InlineData("cuadre")]
    [InlineData("cuadre_todos")]
    [InlineData("facturas_anular")]
    public void Los_permisos_del_punto_de_venta_estan_en_el_catalogo(string codigo)
    {
        Permisos.Todos.Should().Contain(codigo);
    }
}
