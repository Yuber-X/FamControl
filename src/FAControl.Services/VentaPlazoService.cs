using FAControl.Common;
using FAControl.Data;
using FAControl.Models;
using Serilog;

namespace FAControl.Services;

/// <summary>
/// Financiamiento por plazos del dealer (016 — pedido del cliente 2026-07-25).
///
/// CÓMO FINANCIA UN DEALER RD (expediente real del cliente): inicial/anticipo
/// al firmar + N pagos pactados, SIN interés. No es un préstamo: el crédito con
/// interés, amortización y pagaré es AutoControl. Acá el plan es un calendario
/// de pagos propio del dealer, y la separación es una reserva con fecha límite
/// (el cliente tiene 15 días de derecho).
///
/// Cobrar un plazo es UNA transacción: plazo FOR UPDATE + recibo atómico +
/// abono + actualización del plazo + auditoría.
/// </summary>
public class VentaPlazoService
{
    private readonly ConexionFactory _factory;
    private readonly VentaPlazoRepository _plazos;
    private readonly VentaVehiculoRepository _ventas;
    private readonly ContadorRepository _contador;
    private readonly AuditoriaService _auditoria;
    private readonly VehiculoRepository _vehiculos;

    public VentaPlazoService(ConexionFactory factory, VentaPlazoRepository plazos,
        VentaVehiculoRepository ventas, ContadorRepository contador, AuditoriaService auditoria, VehiculoRepository vehiculos)
    {
        _factory = factory;
        _plazos = plazos;
        _ventas = ventas;
        _contador = contador;
        _vehiculos = vehiculos;
        _auditoria = auditoria;
    }

    // ============================================================
    // Lógica pura del plan (sin BD — 100% testeable)
    // ============================================================

    /// <summary>
    /// Reparte el saldo (precio − inicial) en N plazos iguales. El REDONDEO se
    /// ajusta en el ÚLTIMO plazo, para que la suma dé exactamente el saldo:
    /// repartir 100,000 en 3 da 33,333.33 + 33,333.33 + 33,333.34.
    /// </summary>
    public static List<VentaPlazo> CalcularPlazos(decimal precio, PlanPlazos plan)
    {
        if (precio <= 0m)
            throw new ArgumentException("El precio de venta debe ser mayor que cero.");
        if (plan.Inicial < 0m)
            throw new ArgumentException("La inicial no puede ser negativa.");
        if (plan.Inicial > precio)
            throw new ArgumentException("La inicial no puede ser mayor que el precio de venta.");
        if (plan.CantidadPlazos is < 1 or > 240)
            throw new ArgumentException("La cantidad de plazos debe estar entre 1 y 240.");
        if (plan.CadaDias < 1)
            throw new ArgumentException("El intervalo entre plazos debe ser de al menos 1 día.");

        var saldo = precio - plan.Inicial;
        if (saldo <= 0m)
            throw new ArgumentException("Con esa inicial no queda saldo por financiar: registrá la venta al contado.");

        var montoBase = Math.Round(saldo / plan.CantidadPlazos, 2, MidpointRounding.AwayFromZero);
        var plazos = new List<VentaPlazo>(plan.CantidadPlazos);
        var acumulado = 0m;

        for (var i = 1; i <= plan.CantidadPlazos; i++)
        {
            var esUltimo = i == plan.CantidadPlazos;
            var monto = esUltimo ? saldo - acumulado : montoBase;
            acumulado += monto;
            plazos.Add(new VentaPlazo
            {
                Numero = i,
                // Mensual = cada 30 días desde el primer vencimiento pactado
                FechaVencimiento = plan.FechaPrimerPlazo.AddDays((i - 1) * plan.CadaDias),
                Monto = monto
            });
        }
        return plazos;
    }

    // ============================================================
    // Lecturas
    // ============================================================

    /// <summary>
    /// Estado de pago de la venta: "Total por pagar > lo pendiente > cantidad
    /// de plazos > lo pagado" (pedido textual del cliente).
    /// </summary>
    public async Task<EstadoFinanciamiento> ObtenerEstadoAsync(long ventaId, CancellationToken ct = default)
    {
        ExigirLectura();
        var venta = await _ventas.ObtenerPorIdAsync(ventaId, ct)
            ?? throw new InvalidOperationException("La venta no existe.");
        var plazos = await _plazos.ObtenerDeVentaAsync(ventaId, ct);
        var hoy = FechaNegocio.Hoy;

        return new EstadoFinanciamiento(
            venta.Id,
            venta.Codigo,
            venta.TipoVenta,
            venta.Precio,
            venta.Inicial,
            TotalAPlazos: venta.Precio - venta.Inicial,
            Pagado: plazos.Sum(p => p.MontoPagado),
            CantidadPlazos: plazos.Count,
            PlazosPagados: plazos.Count(p => p.Estado == EstadoPlazo.Pagado),
            PlazosAtrasados: plazos.Count(p => p.EstaAtrasado(hoy)),
            FechaLimite: venta.FechaLimite,
            Plazos: plazos);
    }

    public async Task<IReadOnlyList<VentaPlazoPago>> ObtenerPagosAsync(long ventaId,
        CancellationToken ct = default)
    {
        ExigirLectura();
        return await _plazos.ObtenerPagosDeVentaAsync(ventaId, ct);
    }

    // ============================================================
    // Cobro de un plazo (transaccional)
    // ============================================================

    /// <summary>
    /// Cobra un abono empezando por el plazo indicado. Si el cliente paga MÁS de
    /// lo que falta de ese plazo, el excedente baja al siguiente plazo pendiente,
    /// y al siguiente, en orden — igual que el adelanto de PrestControl (pedido
    /// de Yuber 2026-07-31: "si tiene que pagar 66,666.67 y paga 70,000, al
    /// registrar el cobro debe reducir lo que pagará en el próximo").
    ///
    /// Cada plazo tocado recibe su PROPIO recibo: el número de recibo es único y
    /// además así el cliente ve a qué plazo fue cada peso.
    ///
    /// Todo va en UNA transacción, con los plazos bloqueados de entrada: sin eso,
    /// dos cajeros cobrando a la vez aplicarían el mismo excedente dos veces.
    /// </summary>
    public async Task<AbonoVentaResultado> CobrarPlazoAsync(long plazoId, decimal monto,
        MetodoPago metodo, string? notas = null, CancellationToken ct = default)
    {
        ExigirEscritura();
        if (monto <= 0m)
            throw new ArgumentException("El monto del abono debe ser mayor que cero.");
        var porAplicar = Math.Round(monto, 2, MidpointRounding.AwayFromZero);

        using var conexion = await _factory.AbrirAsync(ct);
        using var transaccion = await conexion.BeginTransactionAsync(ct);
        try
        {
            var plazo = await _plazos.ObtenerParaCobroAsync(plazoId, conexion, transaccion, ct)
                ?? throw new InvalidOperationException("El plazo no existe.");
            if (plazo.Estado == EstadoPlazo.Cancelado)
                throw new InvalidOperationException("Ese plazo está cancelado; no se puede cobrar.");
            if (plazo.SaldoPendiente <= 0m)
                throw new InvalidOperationException($"El plazo #{plazo.Numero} ya está saldado.");

            // El plazo elegido y los que siguen, todos bloqueados de una vez
            var pendientes = await _plazos.ObtenerPendientesDesdeAsync(
                plazo.VentaId, plazo.Numero, conexion, transaccion, ct);

            var deudaTotal = pendientes.Sum(p => p.SaldoPendiente);
            if (porAplicar > deudaTotal)
                throw new InvalidOperationException(
                    $"El abono ({porAplicar:N2}) supera todo lo que falta de la venta ({deudaTotal:N2}). " +
                    "Cobrá como máximo el saldo pendiente.");

            var recibos = new List<string>();
            var saldados = 0;
            var aplicado = 0m;

            foreach (var actual in pendientes)
            {
                if (porAplicar <= 0m)
                    break;

                var aEste = Math.Min(porAplicar, actual.SaldoPendiente);
                if (aEste <= 0m)
                    continue;

                var numero = await _contador.SiguienteAsync(
                    ContadorRepository.ReciboVenta, conexion, transaccion, ct);
                var recibo = $"RV-{numero:D6}";

                await _plazos.InsertarPagoAsync(new VentaPlazoPago
                {
                    PlazoId = actual.Id,
                    NumeroRecibo = recibo,
                    FechaPagoUtc = DateTime.UtcNow,
                    Monto = aEste,
                    MetodoPago = metodo,
                    Notas = string.IsNullOrWhiteSpace(notas) ? null : notas.Trim()
                }, conexion, transaccion, ct);

                var acumulado = actual.MontoPagado + aEste;
                var estado = acumulado >= actual.Monto ? EstadoPlazo.Pagado : EstadoPlazo.Pendiente;
                await _plazos.ActualizarTrasPagoAsync(actual.Id, acumulado, estado,
                    conexion, transaccion, ct);

                recibos.Add(recibo);
                aplicado += aEste;
                porAplicar -= aEste;
                if (estado == EstadoPlazo.Pagado)
                    saldados++;
            }

            var detalle = recibos.Count == 1
                ? $"Abono {recibos[0]} de {aplicado:N2} DOP al plazo #{plazo.Numero}"
                : $"Abono de {aplicado:N2} DOP repartido en {recibos.Count} plazos desde el " +
                  $"#{plazo.Numero} (recibos {string.Join(", ", recibos)})";
            await _auditoria.RegistrarEnTransaccionAsync(AccionAuditoria.Crear, DbNames.VentaPlazoPago,
                plazoId, $"{detalle} de la venta #{plazo.VentaId} ({metodo})" +
                (saldados > 0 ? $" — {saldados} plazo(s) saldado(s)" : string.Empty),
                conexion, transaccion, ct);

            await transaccion.CommitAsync(ct);
            Log.Information("Abono de {Monto:N2} DOP en la venta {VentaId}: {Recibos}",
                aplicado, plazo.VentaId, string.Join(", ", recibos));

            return new AbonoVentaResultado(recibos, aplicado, saldados, deudaTotal - aplicado);
        }
        catch
        {
            await transaccion.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    /// <summary>
    /// CANCELA la venta: el cliente devolvió el vehículo (028, pedido de Yuber
    /// 2026-07-31). En una sola transacción:
    ///   1. los plazos que aún se debían quedan 'cancelado' (los pagados NO se
    ///      tocan: ya se cobraron y su recibo existe);
    ///   2. la venta queda 'cancelada' con su motivo y el reparto de la plata;
    ///   3. el vehículo vuelve al inventario como disponible.
    ///
    /// La venta NUNCA se borra: queda en el historial como cancelada, igual que
    /// un préstamo anulado. Lo cobrado tampoco se borra — se reparte.
    ///
    /// El PORCENTAJE de retención lo trae quien llama, digitado por el dueño: lo
    /// fija el contrato de cada dealer y no es algo que el programa deba decidir.
    /// </summary>
    public async Task<ResultadoCancelacion> CancelarVentaAsync(CancelacionVenta datos,
        CancellationToken ct = default)
    {
        // Cancelar mueve plata y devuelve un vehículo al inventario: es de Admin
        if (!SesionActual.EsAdmin)
            throw new UnauthorizedAccessException(
                "Solo un administrador puede cancelar una venta y registrar la devolución.");
        if (string.IsNullOrWhiteSpace(datos.Motivo))
            throw new ArgumentException("Escribí el motivo de la cancelación: queda en el historial.");
        if (datos.RetencionPorcentaje is < 0m or > 100m)
            throw new ArgumentException("El porcentaje de retención va entre 0 y 100.");

        var estado = await ObtenerEstadoAsync(datos.VentaId, ct);

        // Lo cobrado es la inicial más todos los abonos: eso es lo que se reparte
        var cobrado = estado.RecibidoTotal;
        var retenido = Math.Round(cobrado * datos.RetencionPorcentaje / 100m, 2,
            MidpointRounding.AwayFromZero);
        var devuelto = Math.Round(cobrado - retenido, 2, MidpointRounding.AwayFromZero);

        using var conexion = await _factory.AbrirAsync(ct);
        using var transaccion = await conexion.BeginTransactionAsync(ct);
        try
        {
            var vehiculoId = await _plazos.ObtenerVehiculoDeVentaAsync(
                datos.VentaId, conexion, transaccion, ct);

            var cancelados = await _plazos.CancelarPendientesAsync(
                datos.VentaId, conexion, transaccion, ct);

            await _plazos.MarcarVentaCanceladaAsync(datos.VentaId, datos.Motivo.Trim(),
                datos.RetencionPorcentaje, retenido, devuelto, conexion, transaccion, ct);

            // El vehículo vuelve a estar a la venta
            await _vehiculos.CambiarEstadoAsync(vehiculoId, EstadoVehiculo.Disponible,
                conexion, transaccion, ct);

            await _auditoria.RegistrarEnTransaccionAsync(AccionAuditoria.Anular,
                DbNames.VentaVehiculo, datos.VentaId,
                $"Venta {estado.Codigo} CANCELADA (devolución del vehículo). " +
                $"Motivo: {datos.Motivo.Trim()}. Cobrado {cobrado:N2}, retenido {retenido:N2} " +
                $"({datos.RetencionPorcentaje:0.##}%), a devolver {devuelto:N2} DOP. " +
                $"{cancelados} plazo(s) pendientes cancelados; el vehículo volvió al inventario.",
                conexion, transaccion, ct);

            await transaccion.CommitAsync(ct);
            Log.Warning("Venta {Codigo} cancelada: retenido {Retenido:N2}, devuelto {Devuelto:N2} DOP",
                estado.Codigo, retenido, devuelto);

            return new ResultadoCancelacion(cobrado, retenido, devuelto, datos.RetencionPorcentaje);
        }
        catch
        {
            await transaccion.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static void ExigirLectura()
    {
        if (!SesionActual.TienePermiso(Permisos.Ventas))
            throw new UnauthorizedAccessException("No tenés permiso para ver el financiamiento de las ventas.");
    }

    private static void ExigirEscritura()
    {
        if (!SesionActual.TienePermiso(Permisos.Ventas))
            throw new UnauthorizedAccessException("No tenés permiso para cobrar plazos de ventas.");
    }
}
