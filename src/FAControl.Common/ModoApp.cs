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
    /// <summary>El vehículo como CRÉDITO: venta financiada, garantía, cuotas.</summary>
    AutoControl
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
    /// <summary>Mismo color más oscuro: es el del glow al pasar el mouse.</summary>
    string ColorOscuroHex,
    /// <summary>False mientras el módulo no exista: el launcher lo marca.</summary>
    bool Disponible)
{
    public static readonly IReadOnlyList<IdentidadModo> Todos =
    [
        new(ModoApp.PrestControl, "PrestControl", "Préstamos",
            "Préstamos personales e hipotecarios: clientes, cuotas, cobros y reportes.",
            // Dorado de la marca Familia Almonte
            "#C9A15A", "#8A6A2F", Disponible: true),

        new(ModoApp.DealerControl, "DealerControl", "Dealer",
            "Inventario de vehículos, importación, costos y rent a car.",
            // Azul acero
            "#3D5A80", "#25384F", Disponible: false),

        new(ModoApp.AutoControl, "AutoControl", "Ventas de vehículos",
            "Venta financiada: crédito con el vehículo en garantía, cuotas y pagaré.",
            // Verde esmeralda (aprobado por Yuber 2026-07-17)
            "#3A7D5C", "#22503A", Disponible: false)
    ];

    public static IdentidadModo De(ModoApp modo) => Todos.First(m => m.Modo == modo);
}
