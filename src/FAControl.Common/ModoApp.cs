namespace FAControl.Common;

/// <summary>
/// Los tres modos de la suite (decisión con Yuber 2026-07-17).
/// La frontera entre Dealer y Auto es por ETAPA del vehículo, no por cliente:
/// el vehículo NACE en DealerControl (activo) y AutoControl lo CONSUME (crédito).
/// </summary>
public enum ModoApp
{
    /// <summary>Préstamos personales e hipotecarios. Es el PrestControl mejorado.</summary>
    PrestControl,
    /// <summary>El vehículo como ACTIVO: inventario, importación, rent a car, venta al contado.</summary>
    DealerControl,
    /// <summary>
    /// RETIRADO como estancia el 2026-07-29 por pedido del cliente: "ya con
    /// DealControl que hace todas las operaciones del mismo autoControl, es
    /// prácticamente una copia innecesaria". En su lugar el launcher ofrece
    /// POS-500 (<see cref="Pos500"/>).
    ///
    /// El valor SIGUE en el enum a propósito: la columna `ambito` de la base, los
    /// roles por modo y las ventas financiadas ya creadas lo referencian. Sacarlo
    /// del enum obligaría a migrar datos históricos sin ninguna ganancia. Lo que
    /// se quitó es la entrada: no está en <see cref="IdentidadModo.Todos"/>, así
    /// que no aparece en el launcher ni en la pantalla de usuarios.
    /// </summary>
    AutoControl,
    /// <summary>
    /// POS-500: punto de venta (facturación, inventario, caja). Integrado a la
    /// suite como un modo más el 2026-07-30, con **base de datos aparte**
    /// (`pos500_db`): se vende por separado, así que sus datos tienen que poder
    /// irse solos. Lo que SÍ comparte con el resto es lo mismo que los demás
    /// modos — usuarios, roles por modo y permisos, que viven en facontrol_db.
    ///
    /// Se habilita con el código 5 de la licencia.
    /// </summary>
    Pos500
}

/// <summary>Datos de marca del POS-500 (los usa el launcher y la oferta).</summary>
public static class Pos500
{
    public const string Nombre = "POS-500";
    public const string Etiqueta = "Punto de venta";
    public const string Descripcion =
        "Facturación, inventario, almacén, caja y reportes para tiendas y colmados.";
    /// <summary>El verde que usaba AutoControl: el launcher conserva su equilibrio de color.</summary>
    public const string ColorHex = "#3A7D5C";
    public const string ColorBrilloHex = "#48B07E";
}

/// <summary>
/// Identidad de cada modo: nombre, descripción y color.
/// Vive en Common porque la usan el launcher (Views) y el shell (App).
/// </summary>
public record IdentidadModo(
    ModoApp Modo,
    string Nombre,
    string Etiqueta,
    string Descripcion,
    /// <summary>Color de acento del modo (hex).</summary>
    string ColorHex,
    /// <summary>
    /// Color del glow al pasar el mouse. Debe tener suficiente luminancia para
    /// resaltar contra el navy del launcher (el dorado de PrestControl funciona
    /// porque es cálido y claro; el azul y el verde necesitan versiones vivas,
    /// no oscuras — pedido de Yuber 2026-07-19).
    /// </summary>
    string ColorBrilloHex,
    /// <summary>False mientras el módulo no exista: el launcher lo marca.</summary>
    bool Disponible)
{
    /// <summary>
    /// Las estancias que se abren desde el launcher. AutoControl salió el
    /// 2026-07-29 (ver el comentario en <see cref="ModoApp.AutoControl"/>).
    /// </summary>
    public static readonly IReadOnlyList<IdentidadModo> Todos =
    [
        new(ModoApp.PrestControl, "PrestControl", "Préstamos",
            "Préstamos personales e hipotecarios: clientes, cuotas, cobros y reportes.",
            // Dorado de la marca Familia Almonte (glow perfecto según Yuber)
            "#C9A15A", "#B5893F", Disponible: true),

        new(ModoApp.DealerControl, "DealControl", "Dealer",
            "Inventario de vehículos, importación, costos, rent a car y ventas financiadas.",
            // Azul acero + glow azul vivo para que resalte como el dorado
            "#3D5A80", "#5B90D4", Disponible: true),

        // Disponible: false hasta que terminen de portarse sus pantallas. Los
        // cimientos (modo, licencia, permisos, roles y su base pos500_db) ya
        // están; lo que falta es mudar las pantallas al shell de la suite.
        // Cuando eso esté, esto pasa a true y no hay que tocar nada más.
        new(ModoApp.Pos500, Pos500.Nombre, Pos500.Etiqueta, Pos500.Descripcion,
            Pos500.ColorHex, Pos500.ColorBrilloHex, Disponible: false)
    ];

    /// <summary>
    /// Identidad de un modo, incluido el retirado: hay pantallas que muestran el
    /// nombre de la estancia de datos históricos (auditoría, roles viejos).
    /// </summary>
    private static readonly IdentidadModo AutoControlRetirado =
        new(ModoApp.AutoControl, "AutoControl", "Ventas de vehículos",
            "Retirado: sus operaciones las hace DealControl.",
            "#3A7D5C", "#48B07E", Disponible: false);

    public static IdentidadModo De(ModoApp modo) =>
        Todos.FirstOrDefault(m => m.Modo == modo) ?? AutoControlRetirado;
}

/// <summary>Utilidades del modo/ámbito compartidas por todas las capas.</summary>
public static class ModoAppExtensiones
{
    /// <summary>
    /// Valor de la columna `ambito` en la BD (enum: prestcontrol/dealercontrol/autocontrol).
    /// Aísla los datos por estancia de trabajo (decisión Yuber 2026-07-18: 3 dominios).
    /// </summary>
    public static string ClaveDb(this ModoApp modo) => modo switch
    {
        ModoApp.PrestControl => "prestcontrol",
        ModoApp.DealerControl => "dealercontrol",
        ModoApp.AutoControl => "autocontrol",
        ModoApp.Pos500 => "pos500",
        _ => throw new ArgumentOutOfRangeException(nameof(modo), modo, "Modo desconocido")
    };

    /// <summary>
    /// True si el modo guarda sus datos en la BASE APARTE `pos500_db`. Los otros
    /// tres viven en facontrol_db; usuarios, roles y permisos son de facontrol_db
    /// siempre, incluso para el POS.
    /// </summary>
    public static bool UsaBasePos500(this ModoApp modo) => modo == ModoApp.Pos500;
}
