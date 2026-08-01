namespace FAControl.Common;

/// <summary>
/// Nombre del archivo de exportación a Excel.
///
/// Lleva el modo al final (pedido del cliente, 2026-08-01):
/// <c>FAControl_Export_2026-08-01 DealControl.xlsx</c>. Cada estancia exporta
/// SUS datos y nada más; sin el nombre del modo, tres archivos generados el
/// mismo día quedaban idénticos en la carpeta y el segundo pisaba al primero.
///
/// Vive en Common porque lo necesitan la Vista (los diálogos de "Guardar como"
/// proponen el nombre) y el Service (el export automático lo arma solo), y esas
/// dos capas no se ven entre sí.
/// </summary>
public static class NombreExport
{
    /// <summary>Nombre sugerido para la estancia activa, con fecha de negocio.</summary>
    public static string Sugerido() => De(SesionActual.Modo, FechaNegocio.Hoy);

    public static string De(ModoApp modo, DateOnly fecha) =>
        $"FAControl_Export_{fecha:yyyy-MM-dd} {IdentidadModo.De(modo).Nombre}.xlsx";
}
