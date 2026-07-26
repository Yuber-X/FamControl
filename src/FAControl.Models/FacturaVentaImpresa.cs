namespace FAControl.Models;

/// <summary>Datos crudos de la venta para armar su factura (query del repositorio).</summary>
public record FacturaVentaDatos(
    string Codigo,
    DateTime FechaVentaUtc,
    decimal Precio,
    MetodoPago MetodoPago,
    string? Notas,
    string ClienteNombre,
    string? ClienteCedula,
    string? ClienteTelefono,
    string? ClienteDireccion,
    string VehiculoDescripcion,
    string? Vin,
    string? Placa,
    string? Matricula,
    string? Color,
    int? Anio,
    string VendedorNombre);

/// <summary>
/// Factura de una venta al contado del dealer, lista para imprimir en carta
/// (pedido 2026-07-25: "Facturación > ver/imprimir"). Estructura inspirada en
/// el documento real "DATOS Y CONDICIONES DE VENTAS" del expediente del dealer.
/// DTO plano: la capa Printing no conoce ViewModels ni entidades de BD.
/// </summary>
public record FacturaVentaImpresa(
    // Marca del negocio
    string NegocioNombre,
    string NegocioRnc,
    string NegocioTelefono,
    string NegocioCiudad,
    // Venta
    string Codigo,
    string FechaTexto,
    decimal Precio,
    string MetodoTexto,
    string? Notas,
    string VendedorNombre,
    // Cliente
    string ClienteNombre,
    string ClienteCedula,
    string ClienteTelefono,
    string ClienteDireccion,
    // Vehículo
    string VehiculoDescripcion,
    string Vin,
    string Placa,
    string Matricula,
    string Color,
    string AnioTexto);
