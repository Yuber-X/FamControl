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

    public VentaPlazoService(ConexionFactory factory, VentaPlazoRepository plazos,
        VentaVehiculoRepository ventas, ContadorRepository contador, AuditoriaService auditoria)
    {
        _factory = factory;
        _plazos = plazos;
        _ventas = ventas;
        _contador = contador;
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
    /// Registra un abono a un plazo. Atómico: plazo FOR UPDATE → recibo
    /// atómico RV-000001 → abono → actualización del plazo → auditoría.
    /// Devuelve el número de recibo emitido.
    /// </summary>
    public async Task<string> CobrarPlazoAsync(long plazoId, decimal monto, MetodoPago metodo,
        string? notas = null, CancellationToken ct = default)
    {
        ExigirEscritura();
        if (monto <= 0m)
            throw new ArgumentException("El monto del abono debe ser mayor que cero.");
        var montoRedondeado = Math.Round(monto, 2, MidpointRounding.AwayFromZero);

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
            if (montoRedondeado > plazo.SaldoPendiente)
                throw new InvalidOperationException(
                    $"El abono ({montoRedondeado:N2}) supera lo que falta del plazo #{plazo.Numero} " +
                    $"({plazo.SaldoPendiente:N2}). Cobrá el resto en el siguiente plazo.");

            var numero = await _contador.SiguienteAsync(ContadorRepository.ReciboVenta, conexion, transaccion, ct);
            var recibo = $"RV-{numero:D6}";

            await _plazos.InsertarPagoAsync(new VentaPlazoPago
            {
                PlazoId = plazoId,
                NumeroRecibo = recibo,
                FechaPagoUtc = DateTime.UtcNow,
                Monto = montoRedondeado,
                MetodoPago = metodo,
                Notas = string.IsNullOrWhiteSpace(notas) ? null : notas.Trim()
            }, conexion, transaccion, ct);

            var nuevoAcumulado = plazo.MontoPagado + montoRedondeado;
            var nuevoEstado = nuevoAcumulado >= plazo.Monto ? EstadoPlazo.Pagado : EstadoPlazo.Pendiente;
            await _plazos.ActualizarTrasPagoAsync(plazoId, nuevoAcumulado, nuevoEstado,
                conexion, transaccion, ct);

            await _auditoria.RegistrarEnTransaccionAsync(AccionAuditoria.Crear, DbNames.VentaPlazoPago, plazoId,
                $"Abono {recibo} de {montoRedondeado:N2} DOP al plazo #{plazo.Numero} " +
                $"de la venta #{plazo.VentaId} ({metodo})" +
                (nuevoEstado == EstadoPlazo.Pagado ? " — plazo saldado" : string.Empty),
                conexion, transaccion, ct);

            await transaccion.CommitAsync(ct);
            Log.Information("Abono {Recibo} de {Monto:N2} DOP al plazo {PlazoId}", recibo, montoRedondeado, plazoId);
            return recibo;
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
