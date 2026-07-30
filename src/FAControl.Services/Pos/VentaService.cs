// Portado de POS-500 el 2026-07-30 al integrar el punto de venta a la suite.
// Cambios respecto del original: usa ConexionPos500 (base pos500_db, aparte de
// facontrol_db) y el SesionActual / la auditoria compartidos de FAControl.
using MySqlConnector;
using FAControl.Common;
using FAControl.Data.Pos;
// Solo el enum de la auditoria compartida: importar todo FAControl.Models
// chocaria con Cliente/ClienteDatos, que en el POS son otra cosa.
using AccionAuditoria = FAControl.Models.AccionAuditoria;
using FAControl.Models.Pos;

namespace FAControl.Services.Pos;

/// <summary>
/// El cerebro financiero del POS. Reglas innegociables (spec §9):
///  - ITBIS calculado SOBRE EL SUBTOTAL, nunca acumulando redondeos por línea.
///  - Redondeo Math.Round(v, 2, AwayFromZero); el modo (centavo/peso/arriba)
///    solo afecta el TOTAL. Matemática documentada en docs/ITBIS.md.
///  - Emisión atómica: número FOR UPDATE + factura + detalles + stock +
///    auditoría en UNA transacción; cualquier fallo revierte todo.
///  - El stock se valida al facturar (otro cajero pudo vender lo mismo).
/// Los métodos estáticos son puros y testeables sin base de datos.
/// </summary>
public class VentaService
{
    private readonly FacturaRepository _facturas;
    private readonly ClienteRepository _clientes;
    private readonly ConfiguracionNegocioService _config;
    private readonly AuditoriaService _auditoria;

    public VentaService(FacturaRepository facturas, ClienteRepository clientes,
        ConfiguracionNegocioService config, AuditoriaService auditoria)
    {
        _facturas = facturas;
        _clientes = clientes;
        _config = config;
        _auditoria = auditoria;
    }

    // ------------------------------------------------------------------
    // Cálculos puros
    // ------------------------------------------------------------------

    /// <summary>Subtotal = Σ líneas · ITBIS = redondeo(subtotal × tasa%) · Total según modo.</summary>
    public static VentaTotales CalcularTotales(
        IReadOnlyList<VentaLinea> lineas, decimal itbisTasa, ModoRedondeo redondeo)
    {
        var subtotal = lineas.Sum(l => l.Subtotal);
        var itbis = Math.Round(subtotal * itbisTasa / 100m, 2, MidpointRounding.AwayFromZero);
        var total = subtotal + itbis;

        total = redondeo switch
        {
            ModoRedondeo.Peso => Math.Round(total, 0, MidpointRounding.AwayFromZero),
            ModoRedondeo.Arriba => Math.Ceiling(total),
            _ => total   // centavo: subtotal e itbis ya están a 2 decimales
        };

        return new VentaTotales(subtotal, itbisTasa, itbis, total);
    }

    /// <summary>Cambio = efectivo − total. Solo aplica a pagos en efectivo.</summary>
    public static decimal CalcularCambio(decimal efectivoRecibido, decimal total) =>
        efectivoRecibido - total;

    /// <summary>F-0001 (simple) o F-2026-0001 (con año). El año es el del día de negocio.</summary>
    public static string FormatearNumeroFactura(
        string prefijo, long numero, FormatoFactura formato, int anio) => formato switch
    {
        FormatoFactura.ConAnio => $"{prefijo}{anio}-{numero:0000}",
        _ => $"{prefijo}{numero:0000}"
    };

    // ------------------------------------------------------------------
    // Emisión
    // ------------------------------------------------------------------

    public async Task<VentaResultado> RegistrarVentaAsync(VentaSolicitud solicitud, CancellationToken ct = default)
    {
        if (!SesionActual.TienePermiso("vender"))
            throw new InvalidOperationException("No tienes permiso para vender.");

        Validar(solicitud);

        var cfg = _config.Actual;
        // Tasa EFECTIVA: 0 si el ITBIS está desactivado en Configuración
        // (la factura persiste itbis_tasa = 0 e itbis = 0, coherente con el ticket)
        var totales = CalcularTotales(solicitud.Lineas, cfg.ItbisTasaEfectiva, cfg.Redondeo);

        decimal? efectivo = null, cambio = null;
        if (solicitud.MetodoPago is MetodoPagoFactura.Efectivo)
        {
            if (solicitud.EfectivoRecibido is not { } recibido)
                throw new ArgumentException("Indica el efectivo recibido.");
            if (recibido < totales.Total)
                throw new ArgumentException(
                    $"El efectivo recibido no cubre el total ({totales.Total:0.00}).");
            efectivo = recibido;
            cambio = CalcularCambio(recibido, totales.Total);
        }
        else if (solicitud.MetodoPago is MetodoPagoFactura.Mixto)
        {
            // Mixto: la parte en efectivo es informativa; el cambio se maneja manual
            efectivo = solicitud.EfectivoRecibido;
        }

        var ahoraUtc = DateTime.UtcNow;

        using var conexion = await _facturas.AbrirConexionAsync(ct);
        using var transaccion = await conexion.BeginTransactionAsync(ct);
        try
        {
            var numero = await ConfiguracionNegocioRepository.ReservarNumeroFacturaAsync(conexion, transaccion, ct);
            var numeroFactura = FormatearNumeroFactura(
                cfg.FacturaPrefijo, numero, cfg.FacturaFormato, FechaNegocio.Hoy.Year);

            foreach (var linea in solicitud.Lineas)
            {
                if (!await FacturaRepository.DescontarStockAsync(conexion, transaccion, linea.ProductoId, linea.Cantidad, ct))
                    throw new InvalidOperationException(
                        $"Stock insuficiente de \"{linea.NombreProducto}\". " +
                        "Otro cajero pudo haberlo vendido; actualiza el carrito.");
            }

            var facturaId = await FacturaRepository.InsertarFacturaAsync(
                conexion, transaccion, numeroFactura, solicitud.ClienteId, SesionActual.Id,
                ahoraUtc, totales, solicitud.MetodoPago, efectivo, cambio, ct);

            foreach (var linea in solicitud.Lineas)
                await FacturaRepository.InsertarDetalleAsync(conexion, transaccion, facturaId, linea, ct);

            // La auditoría es la de la SUITE (facontrol_db) aunque estemos en la
            // conexión del POS: se califica el esquema para que entre en esta misma
            // transacción. Venta y registro en el historial se guardan juntos.
            await _auditoria.RegistrarEnTransaccionDeOtraBaseAsync(
                AccionAuditoria.Crear, DbNamesPos.Factura, facturaId,
                $"Factura {numeroFactura} emitida: {solicitud.Lineas.Count} líneas, total {totales.Total:0.00}",
                conexion, transaccion, _facturas.EsquemaSuite, ct);

            await transaccion.CommitAsync(ct);

            string? nombreCliente = null;
            if (solicitud.ClienteId is { } clienteId)
                nombreCliente = (await _clientes.ObtenerPorIdAsync(clienteId, ct))?.Nombre;

            return new VentaResultado(facturaId, numeroFactura, ahoraUtc, totales,
                efectivo, cambio, solicitud.Lineas, nombreCliente, solicitud.MetodoPago);
        }
        catch
        {
            await transaccion.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static void Validar(VentaSolicitud solicitud)
    {
        if (solicitud.Lineas.Count == 0)
            throw new ArgumentException("El carrito está vacío.");
        if (solicitud.Lineas.Any(l => l.Cantidad <= 0))
            throw new ArgumentException("Las cantidades deben ser mayores que cero.");
        if (solicitud.Lineas.Any(l => l.PrecioUnitario < 0m))
            throw new ArgumentException("Hay precios inválidos en el carrito.");
        if (solicitud.Lineas.GroupBy(l => l.ProductoId).Any(g => g.Count() > 1))
            throw new ArgumentException("Hay productos repetidos en el carrito; combina las líneas.");
    }
}
