using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace FAControl.Services.Tests;

/// <summary>
/// El tema oscuro reemplaza a Colores.xaml EN CALIENTE, así que ambos
/// diccionarios tienen que definir exactamente las mismas claves.
///
/// Si al tema oscuro le falta una, ese brush queda sin resolver y el control
/// se pinta transparente: un bug invisible hasta que alguien activa el modo
/// noche justo en la demo. Este test lo caza en cada build.
///
/// Se leen los .xaml como TEXTO a propósito: cargarlos como ResourceDictionary
/// exigiría un hilo STA y levantar WPF dentro del test.
/// </summary>
public class TemaOscuroTests
{
    private static readonly Regex Claves = new(@"x:Key=""(?<clave>[^""]+)""", RegexOptions.Compiled);

    private static HashSet<string> LeerClaves(string archivo)
    {
        var ruta = BuscarTema(archivo);
        var xaml = File.ReadAllText(ruta);
        return [.. Claves.Matches(xaml).Select(m => m.Groups["clave"].Value)];
    }

    private static string BuscarTema(string archivo)
    {
        var directorio = new DirectoryInfo(AppContext.BaseDirectory);
        while (directorio is not null)
        {
            var candidato = Path.Combine(directorio.FullName,
                "src", "FAControl.Views", "Themes", archivo);
            if (File.Exists(candidato))
                return candidato;
            directorio = directorio.Parent;
        }
        throw new FileNotFoundException($"No se encontró Themes/{archivo}");
    }

    [Fact]
    public void El_tema_oscuro_define_todas_las_claves_del_claro()
    {
        var claro = LeerClaves("Colores.xaml");
        var oscuro = LeerClaves("ColoresOscuro.xaml");

        var faltantes = claro.Except(oscuro).ToList();

        faltantes.Should().BeEmpty(
            "cada clave que falte en el tema oscuro deja su control transparente: " +
            $"faltan [{string.Join(", ", faltantes)}]");
    }

    [Fact]
    public void El_tema_oscuro_no_inventa_claves_que_el_claro_no_tenga()
    {
        var claro = LeerClaves("Colores.xaml");
        var oscuro = LeerClaves("ColoresOscuro.xaml");

        var sobrantes = oscuro.Except(claro).ToList();

        // Una clave solo en oscuro significa que alguien la agregó a un tema y
        // olvidó el otro: al volver a claro, ese control se rompe.
        sobrantes.Should().BeEmpty(
            $"al volver al tema claro esas claves no existirían: [{string.Join(", ", sobrantes)}]");
    }

    [Fact]
    public void Los_dos_temas_usan_colores_distintos()
    {
        // Sanidad: que no se haya copiado el claro tal cual
        var rutaClaro = BuscarTema("Colores.xaml");
        var rutaOscuro = BuscarTema("ColoresOscuro.xaml");

        var fondoClaro = ExtraerColor(File.ReadAllText(rutaClaro), "Brush.FondoApp");
        var fondoOscuro = ExtraerColor(File.ReadAllText(rutaOscuro), "Brush.FondoApp");

        fondoOscuro.Should().NotBe(fondoClaro);
        // El fondo oscuro debe ser realmente oscuro, y el texto realmente claro
        Luminancia(fondoOscuro).Should().BeLessThan(0.2);
        Luminancia(ExtraerColor(File.ReadAllText(rutaOscuro), "Brush.TextoPrincipal"))
            .Should().BeGreaterThan(0.8);
    }

    private static string ExtraerColor(string xaml, string clave)
    {
        var m = Regex.Match(xaml, $@"x:Key=""{Regex.Escape(clave)}""\s+Color=""(?<color>#[0-9A-Fa-f]{{6}})""");
        m.Success.Should().BeTrue($"'{clave}' debería declarar un color hexadecimal literal");
        return m.Groups["color"].Value;
    }

    /// <summary>Luminancia relativa aproximada (0 = negro, 1 = blanco).</summary>
    private static double Luminancia(string hex)
    {
        var r = Convert.ToInt32(hex.Substring(1, 2), 16) / 255.0;
        var g = Convert.ToInt32(hex.Substring(3, 2), 16) / 255.0;
        var b = Convert.ToInt32(hex.Substring(5, 2), 16) / 255.0;
        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    }
}
