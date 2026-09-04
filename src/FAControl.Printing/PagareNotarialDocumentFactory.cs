using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using FAControl.Common;
using FAControl.Models;

namespace FAControl.Printing;

/// <summary>
/// El pagaré notarial, siguiendo la plantilla que mandó el cliente el
/// 2026-08-26 (Verónica: "Este es el contrato notarial, los dueños lo necesitan
/// subido en el sistema").
///
/// SOBRE EL TEXTO. Las cláusulas son las de la plantilla, con las fallas
/// evidentes corregidas: el original numeraba DOS cláusulas como "QUINTO",
/// dejaba una frase cortada a la mitad ("EL DEUDOR autoriza al SEXTO:") y traía
/// varias erratas de tipeo (DOCIENTOS, taza, AMNBITO, DECIGNACION, ESPANCION,
/// ALUZING, Hemanos, el Tes). Se arreglaron aquí porque el sistema va a reimprimir
/// este texto cientos de veces y arrastrar las erratas hacía ver mal al cliente
/// delante de su propio notario.
///
/// Lo que NO se tocó es el fondo jurídico: las cláusulas dicen lo mismo, en el
/// mismo orden, con las mismas referencias legales (art. 545 del Código de
/// Procedimiento Civil).
///
/// FlowDocument y no un Visual fijo: el acta ocupa dos o tres páginas y con
/// PrintVisual se recortaría, que es el mismo defecto que fue BLOCKER el
/// 2026-07-17.
/// </summary>
public static class PagareNotarialDocumentFactory
{
    private static readonly CultureInfo CulturaRd = CultureInfo.GetCultureInfo("es-DO");
    private static readonly FontFamily Fuente = new("Segoe UI");
    private static readonly Brush Tinta = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
    private static readonly Brush TintaSuave = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x53));
    private static readonly Brush BrushOro = Congelar(new SolidColorBrush(Color.FromRgb(0xC9, 0xA1, 0x5A)));
    private static readonly Brush BrushNavy = Congelar(new SolidColorBrush(Color.FromRgb(0x1B, 0x26, 0x3B)));

    private static Brush Congelar(SolidColorBrush b) { b.Freeze(); return b; }

    /// <summary>Solo el acta.</summary>
    public static FlowDocument Crear(PagareNotarialImpreso n) => Construir(n, conTablaDeCuotas: false);

    /// <summary>
    /// El acta y, atrás, la tabla de cuotas completa (el tercer documento del
    /// pedido 2026-09-03). Es un solo papel: el deudor firma el acta y se lleva
    /// el cuadro de pagos pegado, sin que nadie tenga que juntar dos impresiones.
    /// </summary>
    public static FlowDocument CrearCombinado(PagareNotarialImpreso n) =>
        Construir(n, conTablaDeCuotas: true);

    private static FlowDocument Construir(PagareNotarialImpreso n, bool conTablaDeCuotas)
    {
        var a = n.Acto;
        var doc = new FlowDocument
        {
            FontFamily = Fuente,
            Foreground = Tinta,
            PagePadding = new Thickness(72),
            ColumnWidth = double.PositiveInfinity,
            FontSize = 11.5,
            Background = Brushes.White,
            PageWidth = 816,      // carta, 8.5" a 96 DPI
            PageHeight = 1056
        };

        // ---------- Encabezado ----------
        doc.Blocks.Add(Encabezado(n.Deuda.NombreNegocio));
        doc.Blocks.Add(ReglaDorada());

        var titulo = Centrado("PAGARÉ NOTARIAL", 17, FontWeights.Bold, espacioDespues: 4);
        doc.Blocks.Add(titulo);

        var acto = Centrado(
            $"Acto No. {Hueco(a.ActoNo, 22)}          Folio No. {Hueco(a.FolioNo, 22)}",
            11, FontWeights.Normal, TintaSuave, espacioDespues: 18);
        doc.Blocks.Add(acto);

        // ---------- Comparecencia ----------
        doc.Blocks.Add(Justificado(Comparecencia(n)));

        // ---------- Cláusulas ----------
        doc.Blocks.Add(Clausula("PRIMERO", Primero(n)));
        doc.Blocks.Add(Clausula("SEGUNDO", Segundo(n)));
        doc.Blocks.Add(Clausula("TERCERO",
            "Las partes consienten en darle un carácter ejecutorio al presente acto, conforme a " +
            "lo establecido por las leyes dominicanas y por estar éste revestido de la fuerza " +
            "ejecutoria, conforme al contenido del artículo 545 del Código de Procedimiento Civil " +
            "de la República Dominicana."));
        doc.Blocks.Add(Clausula("CUARTO", Cuarto(n)));
        doc.Blocks.Add(Clausula("QUINTO",
            $"Queda claro entre las partes que, si por alguna razón es necesario realizar " +
            $"procedimientos legales, los gastos correrán por cuenta de {Deudor(a)}."));
        doc.Blocks.Add(Clausula("SEXTO", Sexto(n)));
        doc.Blocks.Add(Clausula("SÉPTIMO", Septimo(a)));

        // ---------- Cierre ----------
        doc.Blocks.Add(Justificado(
            "Acto que fue leído el día, mes y año antes indicados, en alta voz, en presencia de " +
            "los comparecientes y testigos, quienes lo encontraron conforme a sus declaraciones, " +
            "a los cuales puse en conocimiento de las implicaciones de violar la ley que tipifica " +
            "y castiga el perjurio, y en señal de aprobación lo firmaron junto conmigo y por ante " +
            "mí, Notario Público, que CERTIFICO Y DOY FE.", espacioAntes: 12, espacioDespues: 34));

        // ---------- Firmas ----------
        foreach (var bloque in Firmas(n))
            doc.Blocks.Add(bloque);

        // ---------- Tabla de cuotas (solo el combinado) ----------
        if (conTablaDeCuotas)
        {
            var salto = new Paragraph { Margin = new Thickness(0, 30, 0, 0), BreakPageBefore = true };
            doc.Blocks.Add(salto);
            doc.Blocks.Add(Centrado("CUADRO DE PAGOS", 14, FontWeights.Bold, espacioDespues: 4));
            doc.Blocks.Add(Centrado(
                $"Préstamo {n.Deuda.CodigoPrestamo}  ·  {n.Deuda.DeudorNombre}",
                10.5, FontWeights.Normal, TintaSuave, espacioDespues: 14));
            doc.Blocks.Add(TablaCuotas(n.Deuda));
            var total = Parrafo($"Total a pagar: RD${n.Deuda.TotalAPagar.ToString("N2", CulturaRd)}",
                12, FontWeights.Bold, espacioAntes: 12);
            total.TextAlignment = TextAlignment.Right;
            doc.Blocks.Add(total);
        }

        return doc;
    }

    // ==================================================================
    // Texto de las cláusulas
    // ==================================================================

    private static string Comparecencia(PagareNotarialImpreso n)
    {
        var a = n.Acto;
        var municipio = Hueco(a.Municipio, 24);
        var texto =
            $"En la Ciudad, Municipio y Provincia de {municipio}, República Dominicana, " +
            $"{NumeroALetras.FechaLarga(a.FechaActo)}. Por ante mí, {DescribirNotario(a)}; " +
            $"compareció libre y voluntariamente {Genero.Tratamiento(a.Deudor.Sexo)} " +
            $"{a.Deudor.Descripcion()}, quien me manifestó, para que lo haga constar en el " +
            "presente acto, lo siguiente:";
        return texto;
    }

    /// <summary>
    /// El notario se describe distinto que los demás comparecientes: primero la
    /// condición profesional y la matrícula del Colegio, después los datos
    /// personales. Es el orden de la plantilla y el habitual en un acta.
    /// </summary>
    private static string DescribirNotario(DatosNotariales a)
    {
        if (a.Notario.EstaVacia)
            return $"{Hueco(string.Empty, 46)}, abogado notario público";

        var partes = new List<string>
        {
            a.Notario.Nombre.ToUpperInvariant(),
            $"abogado notario público del Municipio de {Hueco(a.Municipio, 18)}",
            "miembro activo del Colegio Dominicano de Notarios, Inc."
        };
        if (!string.IsNullOrWhiteSpace(a.NotarioMatricula))
            partes.Add($"Matrícula No. {a.NotarioMatricula.Trim()}");

        partes.Add(Genero.Gentilicio(a.Notario.Nacionalidad, a.Notario.Sexo));
        partes.Add("mayor de edad");
        var estado = Genero.EstadoCivil(a.Notario.EstadoCivil, a.Notario.Sexo);
        if (!string.IsNullOrWhiteSpace(estado))
            partes.Add(estado);
        if (!string.IsNullOrWhiteSpace(a.Notario.Cedula))
            partes.Add($"titular de la Cédula de Identidad y Electoral No. {a.Notario.Cedula.Trim()}");
        if (!string.IsNullOrWhiteSpace(a.Notario.Domicilio))
            partes.Add($"con domicilio profesional abierto en {a.Notario.Domicilio.Trim()}");

        return string.Join(", ", partes);
    }

    private static string Primero(PagareNotarialImpreso n)
    {
        var a = n.Acto;
        var d = n.Deuda;
        var deudor = Deudor(a);

        var empresa = new List<string> { $"la entidad comercial {d.NombreNegocio.ToUpperInvariant()}" };
        if (!string.IsNullOrWhiteSpace(d.Rnc))
            empresa.Add($"sociedad comercial identificada con el RNC No. {d.Rnc.Trim()}");
        if (!string.IsNullOrWhiteSpace(a.EmpresaDireccion))
            empresa.Add($"con asiento social en {a.EmpresaDireccion.Trim()}");
        if (!a.Representante.EstaVacia)
            empresa.Add($"debidamente representada en este acto por " +
                        $"{Genero.Tratamiento(a.Representante.Sexo)} {a.Representante.Descripcion()}");

        var quienEntrego = a.Representante.EstaVacia
            ? d.NombreNegocio
            : $"{Genero.Tratamiento(a.Representante.Sexo)} {a.Representante.Nombre.ToUpperInvariant()}";

        return
            $"Que reconoce que adeuda y pagará a {string.Join(", ", empresa)}, la suma de " +
            $"{NumeroALetras.PesosConCifra(d.MontoPrestado)}, dinero que {deudor} afirma haber " +
            $"recibido, en calidad de préstamo, de manos de {quienEntrego}. {deudor} se compromete " +
            $"a pagar en un plazo de {DescribirPlazo(n)}, de la manera siguiente: en " +
            $"{NumeroALetras.ConCifra(n.CantidadCuotas, genero: NumeroALetras.GeneroPalabra.Femenino)} " +
            $"cuotas {NombreModalidad(n.Modalidad)}, por " +
            $"la suma de {NumeroALetras.PesosConCifra(n.MontoCuota)} cada una, por concepto de " +
            $"intereses y capital, calculada a una tasa de un {NumeroALetras.Porcentaje(n.TasaMensual)} " +
            $"de interés mensual, iniciando los pagos {NumeroALetras.FechaEnTexto(n.FechaPrimerPago)} " +
            $"y concluyendo {NumeroALetras.FechaEnTexto(n.FechaUltimoPago)}.";
    }

    private static string Segundo(PagareNotarialImpreso n)
    {
        var a = n.Acto;
        var deudor = Deudor(a);
        return
            $"Queda entendido entre las partes que si {deudor} incumple con el pago en la forma y " +
            $"fechas establecidas, de {NumeroALetras.ConCifraDosDigitos(a.CuotasParaExigibilidad, capitalizar: false, genero: NumeroALetras.GeneroPalabra.Femenino)} " +
            $"cuotas, LA ACREEDORA puede reclamar el monto total adeudado, tal y como si hubiese " +
            $"llegado el plazo final, así como también podrá ejecutar el presente pagaré sobre todos " +
            $"los bienes muebles e inmuebles, presentes y futuros, propiedad {DelDeudor(a)}. Además, " +
            $"los retrasos en el pago tendrán una gracia de " +
            $"{NumeroALetras.ConCifraDosDigitos(a.DiasDeGracia, capitalizar: false, genero: NumeroALetras.GeneroPalabra.Masculino)} días, y " +
            $"transcurridos esos {NumeroALetras.ConCifraDosDigitos(a.DiasDeGracia, capitalizar: false, genero: NumeroALetras.GeneroPalabra.Masculino)} " +
            $"días después de la fecha de pago se cargará una mora de un " +
            $"{NumeroALetras.Porcentaje(a.MoraPorcentaje).ToLower(CulturaRd)} del monto adeudado.";
    }

    private static string Cuarto(PagareNotarialImpreso n)
    {
        var a = n.Acto;
        var deudor = Deudor(a);
        var garantia = string.IsNullOrWhiteSpace(a.Garantia)
            ? Hueco(string.Empty, 90)
            : a.Garantia.Trim();
        var registro = string.IsNullOrWhiteSpace(a.RegistroTitulos)
            ? $"EL REGISTRO DE TÍTULOS CORRESPONDIENTE"
            : $"EL {a.RegistroTitulos.Trim().ToUpperInvariant()}";

        return
            $"{deudor}, para asegurar el fiel cumplimiento de todos los acuerdos aquí arribados, " +
            $"pone como GARANTÍA: {garantia}. Por lo que {deudor} AUTORIZA A {registro} Y A LAS " +
            $"AUTORIDADES CORRESPONDIENTES A EJECUTAR EL TRASPASO DEL INMUEBLE OBJETO DE ESTE " +
            $"CONTRATO A NOMBRE DE LA ACREEDORA, cumplidas las formalidades de intimación de pago " +
            $"correspondientes.";
    }

    private static string Sexto(PagareNotarialImpreso n)
    {
        var deudor = Deudor(n.Acto);
        return
            $"{deudor} AUTORIZA A LA ACREEDORA, por medio del presente acto, A TRABAR OPOSICIÓN AL " +
            "RETIRO DE VALORES DE TODAS SUS CUENTAS PERSONALES Y EMPRESARIALES, EN CUALQUIER BANCO " +
            "DE LA REPÚBLICA DOMINICANA, COOPERATIVA O CUALQUIER INSTITUCIÓN PÚBLICA O PRIVADA QUE " +
            "DETENTE BIENES O ACTIVOS DE SU PROPIEDAD BAJO CUALQUIER CONCEPTO, en caso de no cumplir " +
            "con lo pactado en el presente pagaré notarial.";
    }

    private static string Septimo(DatosNotariales a)
    {
        var testigos = a.TestigosConNombre;
        if (testigos.Count == 0)
            return "El presente acto fue hecho en presencia de los señores " +
                   Hueco(string.Empty, 40) + " y " + Hueco(string.Empty, 40) +
                   ", testigos libres de tachas y excepciones como lo establece la ley.";

        var descritos = testigos.Select(t => $"{Genero.Tratamiento(t.Sexo)} {t.Descripcion()}");
        return $"El presente acto fue hecho en presencia de {string.Join("; y ", descritos)}; " +
               "testigos libres de tachas y excepciones como lo establece la ley.";
    }

    // ==================================================================
    // Helpers de texto
    // ==================================================================

    /// <summary>"EL DEUDOR" / "LA DEUDORA".</summary>
    private static string Deudor(DatosNotariales a) => Genero.Deudor(a.Deudor.Sexo);

    /// <summary>"del DEUDOR" / "de LA DEUDORA" — la contracción cambia con el género.</summary>
    private static string DelDeudor(DatosNotariales a) =>
        Genero.EsFemenino(a.Deudor.Sexo) ? "de LA DEUDORA" : "del DEUDOR";

    private static string NombreModalidad(Modalidad modalidad, bool singular = false) =>
        modalidad switch
        {
            Modalidad.Diaria => singular ? "diaria" : "diarias",
            Modalidad.Semanal => singular ? "semanal" : "semanales",
            Modalidad.Quincenal => singular ? "quincenal" : "quincenales",
            Modalidad.Mensual => singular ? "mensual" : "mensuales",
            _ => "de pago único"
        };

    /// <summary>
    /// El plazo como lo escribe el acta: "Dos (02) años", "Dieciocho (18) meses".
    /// Se dice en años cuando la cuenta da un número redondo de años, porque es
    /// como lo dice la gente y como lo escribió el notario en la plantilla.
    /// </summary>
    private static string DescribirPlazo(PagareNotarialImpreso n)
    {
        var meses = n.Modalidad switch
        {
            Modalidad.Mensual => n.CantidadCuotas,
            Modalidad.Quincenal => n.CantidadCuotas / 2,
            Modalidad.Semanal => n.CantidadCuotas / 4,
            Modalidad.Diaria => n.CantidadCuotas / 30,
            _ => 0
        };

        if (meses >= 12 && meses % 12 == 0)
        {
            var anios = meses / 12;
            return $"{NumeroALetras.ConCifraDosDigitos(anios, capitalizar: false, genero: NumeroALetras.GeneroPalabra.Masculino)} " +
                   (anios == 1 ? "año" : "años");
        }
        if (meses >= 1)
            return $"{NumeroALetras.ConCifraDosDigitos(meses, capitalizar: false, genero: NumeroALetras.GeneroPalabra.Masculino)} " +
                   (meses == 1 ? "mes" : "meses");

        // Plazos cortos (diario/semanal de pocas cuotas) se dicen en cuotas.
        return $"{NumeroALetras.ConCifra(n.CantidadCuotas, capitalizar: false, genero: NumeroALetras.GeneroPalabra.Femenino)} " +
               (n.CantidadCuotas == 1 ? "cuota " : "cuotas ") +
               NombreModalidad(n.Modalidad, n.CantidadCuotas == 1);
    }

    /// <summary>
    /// Un dato que falta se imprime como una raya para llenar a mano. Es lo que
    /// hace un acta de verdad: el notario completa lo que falte en el momento.
    /// </summary>
    private static string Hueco(string? valor, int largo) =>
        string.IsNullOrWhiteSpace(valor) ? new string('_', largo) : valor.Trim();

    // ==================================================================
    // Bloques
    // ==================================================================

    private static Paragraph Clausula(string numero, string cuerpo)
    {
        var p = new Paragraph
        {
            Margin = new Thickness(0, 0, 0, 10),
            LineHeight = 19,
            TextAlignment = TextAlignment.Justify
        };
        p.Inlines.Add(new Bold(new Run($"{numero}: ")));
        p.Inlines.Add(new Run(cuerpo));
        return p;
    }

    private static Paragraph Justificado(string texto, double espacioAntes = 0,
        double espacioDespues = 12) =>
        new(new Run(texto))
        {
            Margin = new Thickness(0, espacioAntes, 0, espacioDespues),
            LineHeight = 19,
            TextAlignment = TextAlignment.Justify
        };

    private static Paragraph Centrado(string texto, double tamano, FontWeight peso,
        Brush? color = null, double espacioDespues = 0)
    {
        var p = Parrafo(texto, tamano, peso, color, espacioDespues: espacioDespues);
        p.TextAlignment = TextAlignment.Center;
        return p;
    }

    private static Paragraph Parrafo(string texto, double tamano, FontWeight peso,
        Brush? color = null, double espacioAntes = 0, double espacioDespues = 0) =>
        new(new Run(texto))
        {
            FontSize = tamano,
            FontWeight = peso,
            Foreground = color ?? Tinta,
            Margin = new Thickness(0, espacioAntes, 0, espacioDespues)
        };

    private static BlockUIContainer Encabezado(string nombreNegocio)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var badge = LogoFa.Badge(46);
        Grid.SetColumn(badge, 0);
        grid.Children.Add(badge);

        var texto = new TextBlock
        {
            Text = nombreNegocio,
            FontFamily = Fuente,
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Foreground = BrushNavy,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(texto, 1);
        grid.Children.Add(texto);

        return new BlockUIContainer(grid) { Margin = new Thickness(0, 0, 0, 6) };
    }

    private static BlockUIContainer ReglaDorada() =>
        new(new Border { Height = 2.5, Background = BrushOro, Margin = new Thickness(0, 0, 0, 16) })
        {
            Margin = new Thickness(0)
        };

    /// <summary>
    /// Las firmas del acta: acreedora, deudor, los dos testigos y el notario.
    /// Van de a dos por fila para que entren en el ancho de la hoja, y el
    /// notario cierra solo, centrado, como corresponde.
    /// </summary>
    private static IEnumerable<Block> Firmas(PagareNotarialImpreso n)
    {
        var a = n.Acto;

        var acreedora = a.Representante.EstaVacia
            ? n.Deuda.NombreNegocio
            : a.Representante.Nombre.ToUpperInvariant();
        yield return ParFirmas(
            (acreedora, $"Acreedora, en representación de {n.Deuda.NombreNegocio}"),
            (n.Deuda.DeudorNombre.ToUpperInvariant(), Genero.EsFemenino(a.Deudor.Sexo) ? "Deudora" : "Deudor"));

        var testigos = a.TestigosConNombre;
        yield return ParFirmas(
            (testigos.Count > 0 ? testigos[0].Nombre.ToUpperInvariant() : string.Empty, "Testigo"),
            (testigos.Count > 1 ? testigos[1].Nombre.ToUpperInvariant() : string.Empty, "Testigo"));

        yield return FirmaCentrada(a.Notario.Nombre.ToUpperInvariant(), "Notario Público");
    }

    private static Table ParFirmas((string Nombre, string Rol) izquierda, (string Nombre, string Rol) derecha)
    {
        // Anchos FIJOS: con columnas estrella las celdas colapsan y el nombre
        // sale en vertical, una letra por línea (defecto ya visto en el pagaré).
        var tabla = new Table { CellSpacing = 0, Margin = new Thickness(0, 0, 0, 34) };
        tabla.Columns.Add(new TableColumn { Width = new GridLength(300) });
        tabla.Columns.Add(new TableColumn { Width = new GridLength(72) });
        tabla.Columns.Add(new TableColumn { Width = new GridLength(300) });

        var grupo = new TableRowGroup();
        var fila = new TableRow();
        fila.Cells.Add(CeldaFirma(izquierda.Nombre, izquierda.Rol));
        fila.Cells.Add(new TableCell());
        fila.Cells.Add(CeldaFirma(derecha.Nombre, derecha.Rol));
        grupo.Rows.Add(fila);
        tabla.RowGroups.Add(grupo);
        return tabla;
    }

    private static Table FirmaCentrada(string nombre, string rol)
    {
        var tabla = new Table { CellSpacing = 0, Margin = new Thickness(0, 0, 0, 8) };
        tabla.Columns.Add(new TableColumn { Width = new GridLength(186) });
        tabla.Columns.Add(new TableColumn { Width = new GridLength(300) });
        tabla.Columns.Add(new TableColumn { Width = new GridLength(186) });

        var grupo = new TableRowGroup();
        var fila = new TableRow();
        fila.Cells.Add(new TableCell());
        fila.Cells.Add(CeldaFirma(nombre, rol));
        fila.Cells.Add(new TableCell());
        grupo.Rows.Add(fila);
        tabla.RowGroups.Add(grupo);
        return tabla;
    }

    private static TableCell CeldaFirma(string nombre, string rol)
    {
        var celda = new TableCell();
        celda.Blocks.Add(new Paragraph(new Run("________________________________________"))
        {
            Margin = new Thickness(0),
            TextAlignment = TextAlignment.Center,
            Foreground = TintaSuave
        });
        celda.Blocks.Add(new Paragraph(new Run(string.IsNullOrWhiteSpace(nombre) ? " " : nombre))
        {
            Margin = new Thickness(0, 2, 0, 0),
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center
        });
        celda.Blocks.Add(new Paragraph(new Run(rol))
        {
            Margin = new Thickness(0),
            FontSize = 9.5,
            Foreground = TintaSuave,
            TextAlignment = TextAlignment.Center
        });
        return celda;
    }

    private static Table TablaCuotas(PagareImpreso p)
    {
        var tabla = new Table { CellSpacing = 0, Margin = new Thickness(0) };
        tabla.Columns.Add(new TableColumn { Width = new GridLength(50) });
        tabla.Columns.Add(new TableColumn { Width = new GridLength(150) });
        tabla.Columns.Add(new TableColumn { Width = new GridLength(150) });

        var grupo = new TableRowGroup();
        var cab = new TableRow { Background = new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2)) };
        cab.Cells.Add(CeldaTabla("#", FontWeights.SemiBold));
        cab.Cells.Add(CeldaTabla("Vencimiento", FontWeights.SemiBold));
        cab.Cells.Add(CeldaTabla("Cuota", FontWeights.SemiBold, TextAlignment.Right));
        grupo.Rows.Add(cab);

        foreach (var c in p.Cuotas)
        {
            var fila = new TableRow();
            fila.Cells.Add(CeldaTabla(c.Numero.ToString(CulturaRd), FontWeights.Normal));
            fila.Cells.Add(CeldaTabla(c.FechaTexto, FontWeights.Normal));
            fila.Cells.Add(CeldaTabla(c.Cuota.ToString("N2", CulturaRd), FontWeights.Normal,
                TextAlignment.Right));
            grupo.Rows.Add(fila);
        }

        tabla.RowGroups.Add(grupo);
        return tabla;
    }

    private static TableCell CeldaTabla(string texto, FontWeight peso,
        TextAlignment alineacion = TextAlignment.Left) =>
        new(new Paragraph(new Run(texto))
        {
            FontWeight = peso,
            FontSize = 11,
            Margin = new Thickness(0),
            TextAlignment = alineacion
        })
        {
            Padding = new Thickness(6, 4, 6, 4),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
            BorderThickness = new Thickness(0, 0, 0, 0.5)
        };
}
