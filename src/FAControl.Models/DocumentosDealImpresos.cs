namespace FAControl.Models;

/// <summary>Una fila del calendario de plazos tal como se imprime.</summary>
public record PlazoImpreso(int Numero, string FechaTexto, decimal Monto, string EstadoTexto);

/// <summary>
/// Carta de compromiso de pago (pedido 2026-07-25). El comprador reconoce la
/// deuda del vehículo y se compromete al calendario de plazos pactado.
/// DTO plano: la capa Printing no conoce ViewModels ni entidades de BD.
/// </summary>
public record CartaCompromisoImpresa(
    // Negocio
    string NegocioNombre,
    string NegocioRnc,
    string NegocioTelefono,
    string NegocioCiudad,
    // Venta
    string Codigo,
    string FechaTexto,
    decimal Precio,
    decimal Inicial,
    decimal TotalAPlazos,
    // Comprador
    string ClienteNombre,
    string ClienteCedula,
    string ClienteDireccion,
    string ClienteTelefono,
    // Vehículo
    string VehiculoDescripcion,
    string Vin,
    string Placa,
    string Matricula,
    string Color,
    string AnioTexto,
    // Plan
    IReadOnlyList<PlazoImpreso> Plazos,
    string EmitidoPor);

/// <summary>
/// Recibo de separación / apartado (pedido 2026-07-25): el cliente deja un
/// adelanto y tiene derecho hasta la fecha límite (el dealer usa 15 días).
/// </summary>
public record ReciboSeparacionImpreso(
    // Negocio
    string NegocioNombre,
    string NegocioRnc,
    string NegocioTelefono,
    string NegocioCiudad,
    // Separación
    string Codigo,
    string FechaTexto,
    decimal Precio,
    decimal Adelanto,
    decimal Pendiente,
    string FechaLimiteTexto,
    int DiasDerecho,
    // Comprador
    string ClienteNombre,
    string ClienteCedula,
    string ClienteTelefono,
    // Vehículo
    string VehiculoDescripcion,
    string Vin,
    string Placa,
    string Color,
    string AnioTexto,
    string EmitidoPor);
