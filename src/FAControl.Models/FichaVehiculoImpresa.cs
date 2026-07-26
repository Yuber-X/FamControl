namespace FAControl.Models;

/// <summary>Fila del historial de reparaciones tal como se imprime.</summary>
public record ReparacionImpresa(string FechaTexto, string Detalle, decimal Costo);

/// <summary>
/// Ficha del vehículo lista para imprimir en hoja carta (pedido 2026-07-25:
/// "Ver ficha > imprimir PDF con datos completos"). DTO plano: la capa
/// Printing no conoce ViewModels ni entidades de BD.
/// </summary>
public record FichaVehiculoImpresa(
    // Marca del negocio
    string NegocioNombre,
    string NegocioRnc,
    string NegocioTelefono,
    // Identificación del vehículo
    string Codigo,
    string Descripcion,
    string TipoTexto,
    string EstadoTexto,
    string Vin,
    string Placa,
    string Matricula,
    string Color,
    string AnioTexto,
    string KilometrajeTexto,
    string? Notas,
    // Costos: solo si quien imprime puede verlos (el Vendedor no)
    bool MostrarCostos,
    decimal CostoAdquisicion,
    decimal GastosImportacion,
    decimal PrecioVenta,
    /// <summary>"Juan Pérez — venta VC-0003 del 14/08/2025 por RD$ 715,000.00", crédito, o null.</summary>
    string? CompradorTexto,
    IReadOnlyList<ReparacionImpresa> Reparaciones,
    decimal CostoReparaciones,
    string EmitidoPor)
{
    public decimal CostoTotal => CostoAdquisicion + GastosImportacion;
}
