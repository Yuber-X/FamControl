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
    private readonly NcfRepository _ncf;

    public VentaPlazoService(ConexionFactory factory, VentaPlazoRepository plazos,
        VentaVehiculoRepository ventas, ContadorRepository contador, AuditoriaService auditoria,
        VehiculoRepository vehiculos, NcfRepository ncf)
    {
        _factory = factory;
        _plazos = plazos;
        _ventas = ventas;
        _contador = contador;
        _vehiculos = vehiculos;
        _auditoria = auditoria;
        _ncf = ncf;
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
    /// <param name="ncfManual">e-NCF pegado a mano (Facturador Gratuito DGII). NULL = sin comprobante.</param>
    /// <param name="asignarNcfAuto">True = tomar el siguiente de la secuencia del modo (ignora <paramref name="ncfManual"/>).</param>
    public async Task<AbonoVentaResultado> CobrarPlazoAsync(long plazoId, decimal monto,
        MetodoPago metodo, string? notas = null, CancellationToken ct = default,
        string? ncfManual = null, bool asignarNcfAuto = false)
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

            // Comprobante fiscal del cobro (042). Se resuelve con los plazos ya
            // bloqueados: la reserva usa FOR UPDATE y tiene que vivir en la
            // misma transaccion, o dos cajas se llevan el mismo numero.
            var ncfDelCobro = asignarNcfAuto
                ? await _ncf.ReservarSiguienteAsync(
                    SesionActual.Modo, conexion, transaccion, FechaNegocio.Hoy, ct)
                : string.IsNullOrWhiteSpace(ncfManual)
                    ? null
                    : ncfManual.Trim().ToUpperInvariant();

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
                    // Solo la primera fila: el abono es UN documento fiscal
                    // aunque se reparta entre varios plazos.
                    Ncf = recibos.Count == 0 ? ncfDelCobro : null,
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
                (saldados > 0 ? $" — {saldados} plazo(s) saldado(s)" : string.Empty) +
                (ncfDelCobro is null ? string.Empty : $" — comprobante fiscal {ncfDelCobro}" +
                    (asignarNcfAuto ? " (de la secuencia)" : " (registrado externo)")),
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

        // Una venta SALDADA no se cancela (2026-08-01). El cliente pagó todo y
        // el vehículo es suyo: cancelarla lo devolvería al inventario y
        // retendría un porcentaje de lo cobrado, es decir, rompería el
        // histórico y el inventario de un tirón.
        //
        // La regla vive ACÁ y no solo en la pantalla: ocultar un botón no es
        // una regla, es una sugerencia. Si de verdad hay que revertir una venta
        // cobrada, eso es una devolución y se registra como tal.
        if (estado.EstaSaldada && estado.CantidadPlazos > 0)
            throw new InvalidOperationException(
                $"La venta {estado.Codigo} ya está saldada: el cliente pagó todo. " +
                "Una venta cobrada por completo no se cancela; si hubo una devolución, " +
                "registrala como tal.");

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

    /// <summary>
    /// Corrige una venta ya registrada (033, ampliado en 035).
    ///
    /// SOLO ADMIN, no basta el permiso ventas_editar: "como esto es muy
    /// delicado solo puede ser realizado por el mismo admin" (Yuber
    /// 2026-07-31). Rehacer el calendario mueve plata que el cliente ya
    /// entrego.
    ///
    /// QUE SE PUEDE CORREGIR: precio, inicial, CANTIDAD DE PLAZOS, metodo y
    /// notas. Se conservan la fecha del primer vencimiento y el intervalo entre
    /// plazos: eso es lo que se pacto con el cliente y no es lo que se corrige.
    ///
    /// QUE PASA CON LO YA COBRADO — el punto delicado:
    /// Los recibos NO se tocan. Cada abono conserva su numero, su fecha, su
    /// monto y quien lo cobro; lo unico que cambia es a que plazo se imputa.
    /// La plata se reparte de nuevo sobre el plan corregido, en cascada y en
    /// orden cronologico: satura el plazo 1, sigue con el 2, y asi.
    ///
    /// Si lo cobrado alcanza para MAS que el plan nuevo, el sobrante queda como
    /// SALDO A FAVOR del cliente. El sistema no devuelve plata ni la descuenta
    /// de nada: avisa el monto y la decision la toma el dueño.
    ///
    /// Lo que NUNCA se toca: el codigo (VC-0001), el vehiculo y el cliente.
    /// Una venta CANCELADA no se corrige: ya se liquido con su retencion.
    /// </summary>
    public async Task<ResultadoEdicionVenta> EditarVentaAsync(EdicionVenta cambios,
        CancellationToken ct = default)
    {
        if (!SesionActual.EsAdmin)
            throw new UnauthorizedAccessException(
                "Corregir una venta con cobros hechos rehace el calendario y reparte de nuevo la " +
                "plata que el cliente ya entrego. Solo un administrador puede hacerlo.");
        if (string.IsNullOrWhiteSpace(cambios.Motivo))
            throw new ArgumentException(
                "Indica por que se corrige la venta: queda en el historial.", nameof(cambios));

        var venta = await _ventas.ObtenerPorIdAsync(cambios.VentaId, ct)
            ?? throw new InvalidOperationException($"No existe la venta con id {cambios.VentaId}.");
        if (await _ventas.ObtenerCancelacionAsync(cambios.VentaId, ct) is not null)
            throw new InvalidOperationException(
                $"La venta {venta.Codigo} esta cancelada: ya se liquido con su retencion y no se corrige.");

        var precio = Math.Round(cambios.Precio, 2, MidpointRounding.AwayFromZero);
        var inicial = Math.Round(cambios.Inicial, 2, MidpointRounding.AwayFromZero);
        if (precio <= 0m)
            throw new ArgumentException("El precio de venta debe ser mayor que cero.", nameof(cambios));
        if (inicial < 0m || inicial > precio)
            throw new ArgumentException(
                "La inicial no puede ser negativa ni mayor que el precio.", nameof(cambios));

        var detalle = new List<string>();
        if (venta.Precio != precio) detalle.Add($"precio {venta.Precio:N2} a {precio:N2}");
        if (venta.Inicial != inicial) detalle.Add($"inicial {venta.Inicial:N2} a {inicial:N2}");
        if (venta.MetodoPago != cambios.Metodo) detalle.Add($"metodo {venta.MetodoPago} a {cambios.Metodo}");
        if (venta.Notas != cambios.Notas) detalle.Add("notas");

        // ---------- El plan nuevo ----------
        List<VentaPlazo>? plazosNuevos = null;
        var actuales = await _plazos.ObtenerDeVentaAsync(cambios.VentaId, ct);
        var cantidad = cambios.CantidadPlazos ?? actuales.Count;

        if (venta.TipoVenta == TipoVenta.Plazos)
        {
            if (actuales.Count == 0)
                throw new InvalidOperationException(
                    "Esta venta figura como financiada pero no tiene plazos cargados. " +
                    "Avisale al soporte antes de corregirla.");
            if (cantidad < 1)
                throw new ArgumentException("La cantidad de plazos debe ser al menos 1.", nameof(cambios));

            if (cantidad != actuales.Count)
                detalle.Add($"plazos {actuales.Count} a {cantidad}");

            // Se conservan la fecha del primero y el intervalo: es lo pactado.
            var cadaDias = actuales.Count > 1
                ? Math.Max(1, actuales[1].FechaVencimiento.DayNumber - actuales[0].FechaVencimiento.DayNumber)
                : 30;
            plazosNuevos = CalcularPlazos(precio,
                new PlanPlazos(inicial, cantidad, actuales[0].FechaVencimiento, cadaDias));
        }

        if (detalle.Count == 0)
        {
            // Nada cambio: no se ensucia la auditoria ni se remueve la plata
            var totalActual = actuales.Sum(z => z.Monto);
            return new ResultadoEdicionVenta(venta.Codigo, actuales.Count, totalActual,
                actuales.Sum(z => z.MontoPagado), actuales.Count(z => z.Estado == EstadoPlazo.Pagado), 0m);
        }

        using var conexion = await _factory.AbrirAsync(ct);
        using var transaccion = await conexion.BeginTransactionAsync(ct);
        try
        {
            await _ventas.ActualizarDatosAsync(cambios.VentaId, precio, inicial, cambios.Metodo,
                cambios.Notas, conexion, transaccion, ct);

            decimal yaCobrado = 0m, saldoAFavor = 0m;
            int saldados = 0, cantidadFinal = actuales.Count;
            var totalAPlazos = actuales.Sum(z => z.Monto);

            if (plazosNuevos is not null)
            {
                // 1. Los pagos, en orden cronologico. Se leen ANTES de tocar
                //    nada: son la verdad de lo que el cliente entrego.
                var pagos = await _plazos.ObtenerPagosParaReimputarAsync(
                    cambios.VentaId, conexion, transaccion, ct);
                yaCobrado = pagos.Sum(g => g.Monto);

                // 2. Los plazos viejos se corren de numeracion para que el plan
                //    nuevo pueda entrar sin chocar con la clave unica.
                await _plazos.ApartarPlazosAsync(cambios.VentaId, conexion, transaccion, ct);
                await _plazos.InsertarPlazosAsync(cambios.VentaId, plazosNuevos, conexion, transaccion, ct);

                var nuevos = await _plazos.ObtenerPlazosNuevosAsync(
                    cambios.VentaId, conexion, transaccion, ct);

                // 3. La plata se reparte en CASCADA sobre el plan nuevo: satura
                //    el primero, sigue con el segundo. Es el mismo criterio que
                //    ya usa el cobro normal, para que no haya dos formas de
                //    repartir un abono en el mismo sistema.
                var restante = yaCobrado;
                var acumulado = new decimal[nuevos.Count];
                for (var i = 0; i < nuevos.Count && restante > 0m; i++)
                {
                    var cabe = Math.Min(restante, nuevos[i].Monto);
                    acumulado[i] = cabe;
                    restante -= cabe;
                }
                saldoAFavor = restante;   // lo que no entro en ningun plazo

                // 4. Cada recibo se reapunta al plazo donde cayo su plata. El
                //    recibo en si NO se toca: numero, fecha, monto y cobrador
                //    quedan como estan. Sin esto, borrar los plazos viejos
                //    fallaria por la clave foranea, que es justamente la red
                //    que impide perder un pago.
                var indice = 0;
                var usadoEnPlazo = 0m;
                foreach (var (pagoId, monto) in pagos)
                {
                    while (indice < nuevos.Count - 1 && usadoEnPlazo >= acumulado[indice])
                    {
                        indice++;
                        usadoEnPlazo = 0m;
                    }
                    await _plazos.ReapuntarPagoAsync(pagoId, nuevos[indice].Id, conexion, transaccion, ct);
                    usadoEnPlazo += monto;
                }

                // 5. El estado de cada plazo, ya con la plata repartida
                for (var i = 0; i < nuevos.Count; i++)
                {
                    var estado = acumulado[i] >= nuevos[i].Monto ? EstadoPlazo.Pagado : EstadoPlazo.Pendiente;
                    if (estado == EstadoPlazo.Pagado) saldados++;
                    await _plazos.ActualizarTrasPagoAsync(nuevos[i].Id, acumulado[i], estado,
                        conexion, transaccion, ct);
                }

                // 6. Recien ahora los viejos quedan sin pagos y se pueden borrar
                await _plazos.BorrarPlazosApartadosAsync(cambios.VentaId, conexion, transaccion, ct);

                cantidadFinal = nuevos.Count;
                totalAPlazos = plazosNuevos.Sum(z => z.Monto);
            }

            var detalleCobrado = yaCobrado > 0m
                ? $" — se re-imputaron {yaCobrado:N2} DOP ya cobrados ({saldados} plazo(s) saldado(s))" +
                  (saldoAFavor > 0m ? $", quedan {saldoAFavor:N2} DOP a favor del cliente" : "")
                : "";
            await _auditoria.RegistrarEnTransaccionAsync(AccionAuditoria.Modificar,
                DbNames.VentaVehiculo, cambios.VentaId,
                $"Venta {venta.Codigo} corregida ({string.Join(", ", detalle)}). " +
                $"Motivo: {cambios.Motivo.Trim()}{detalleCobrado}",
                conexion, transaccion, ct);

            await transaccion.CommitAsync(ct);
            Log.Information("Venta {Codigo} corregida por {Usuario}: {Detalle}. Saldo a favor: {Saldo}",
                venta.Codigo, SesionActual.Username, string.Join(", ", detalle), saldoAFavor);

            return new ResultadoEdicionVenta(venta.Codigo, cantidadFinal, totalAPlazos,
                yaCobrado, saldados, saldoAFavor);
        }
        catch
        {
            await transaccion.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
