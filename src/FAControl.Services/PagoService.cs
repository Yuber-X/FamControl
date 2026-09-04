using FAControl.Common;
using FAControl.Data;
using FAControl.Models;
using MySqlConnector;
using Serilog;

namespace FAControl.Services;

/// <summary>
/// Registro de cobros. Cubre los cuatro escenarios del CLAUDE.md:
///  - pago exacto de la cuota
///  - abono parcial (aplica PRIMERO a interés, luego a capital)
///  - adelanto de cuotas futuras (el monto cae en cascada cuota por cuota)
///  - liquidación anticipada (cuotas futuras pagan solo su capital pendiente;
///    el interés futuro se exonera — decisión de negocio corregible, ver BLOCKERS.md)
///
/// Todo cobro es UNA transacción: contador de recibo (FOR UPDATE) + N pagos
/// + actualización de cuotas + estado del préstamo + auditoría.
///
/// Nota sobre recibos: pago.numero_recibo es UNIQUE por fila, así que cuando un
/// cobro afecta varias cuotas cada abono lleva su propio número; el recibo
/// impreso agrupa la operación completa bajo el primer número.
/// </summary>
public class PagoService
{
    private readonly ConexionFactory _factory;
    private readonly PrestamoRepository _prestamos;
    private readonly PagoRepository _pagos;
    private readonly ClienteRepository _clientes;
    private readonly ContadorRepository _contador;
    private readonly AuditoriaService _auditoria;
    private readonly AjustesLocales _ajustes;
    private readonly NcfRepository _ncf;

    public PagoService(ConexionFactory factory, PrestamoRepository prestamos, PagoRepository pagos,
        ClienteRepository clientes, ContadorRepository contador, AuditoriaService auditoria,
        AjustesLocales ajustes, NcfRepository ncf)
    {
        _factory = factory;
        _prestamos = prestamos;
        _pagos = pagos;
        _clientes = clientes;
        _contador = contador;
        _auditoria = auditoria;
        _ajustes = ajustes;
        _ncf = ncf;
    }

    // ============================================================
    // Lógica pura de distribución (sin BD — 100% testeable)
    // ============================================================

    /// <summary>
    /// Interés ya cubierto: lo pagado menos lo que fue a capital (043).
    /// Antes esto se deducía con la regla "primero interés", que reparte bien
    /// un cobro normal pero NO un abono a capital —que no paga interés—, así
    /// que se comía una cuota de interés de cada abono.
    /// </summary>
    private static decimal InteresPagado(Cuota cuota) =>
        Math.Max(0m, cuota.MontoPagado - cuota.CapitalPagado);

    /// <summary>Interés aún no cubierto de la cuota.</summary>
    public static decimal InteresPendiente(Cuota cuota) =>
        Math.Max(0m, cuota.Interes - InteresPagado(cuota));

    /// <summary>Capital aún no cubierto de la cuota.</summary>
    public static decimal CapitalPendiente(Cuota cuota) =>
        Math.Max(0m, cuota.Capital - cuota.CapitalPagado);

    /// <summary>
    /// Distribuye un monto entre las cuotas impagas en orden: dentro de cada
    /// cuota primero interés, luego capital; el excedente adelanta la siguiente.
    /// </summary>
    public static List<AplicacionPago> DistribuirPago(decimal monto, IReadOnlyList<Cuota> cuotasImpagas)
    {
        if (monto <= 0m)
            throw new ArgumentException("El monto del pago debe ser mayor que cero.", nameof(monto));
        if (cuotasImpagas.Count == 0)
            throw new ArgumentException("El préstamo no tiene cuotas pendientes de cobro.", nameof(cuotasImpagas));

        var restante = monto;
        var aplicaciones = new List<AplicacionPago>();

        foreach (var cuota in cuotasImpagas)
        {
            if (restante <= 0m)
                break;

            var pendiente = cuota.SaldoPendiente;
            if (pendiente <= 0m)
                continue;

            var aplicar = Math.Min(restante, pendiente);
            var interesAplicado = Math.Min(aplicar, InteresPendiente(cuota));
            var capitalAplicado = aplicar - interesAplicado;

            aplicaciones.Add(new AplicacionPago(
                cuota, aplicar, interesAplicado, capitalAplicado,
                QuedaPagada: aplicar == pendiente));
            restante -= aplicar;
        }

        if (restante > 0m)
        {
            var deudaTotal = cuotasImpagas.Sum(c => c.SaldoPendiente);
            throw new ArgumentException(
                $"El monto ({monto:N2}) excede la deuda pendiente del préstamo ({deudaTotal:N2}). " +
                "Para saldar el préstamo use la liquidación anticipada.");
        }

        return aplicaciones;
    }

    /// <summary>
    /// Cobro con ABONO A CAPITAL (cliente 2026-07-17): el monto base paga las
    /// cuotas en orden (interés + capital, como siempre), y el abono se aplica
    /// EXTRA sobre el capital de las siguientes cuotas —soonest first—
    /// EXONERANDO su interés. Es una liquidación parcial: el cliente adelanta
    /// capital y se ahorra los intereses de lo que adelantó.
    ///
    /// El abono nunca puede exceder el capital pendiente del préstamo.
    /// </summary>
    public static List<AplicacionPago> DistribuirConAbono(
        decimal montoBase, decimal abono, IReadOnlyList<Cuota> cuotasImpagas, DateOnly hoy)
    {
        if (abono < 0m)
            throw new ArgumentException("El abono no puede ser negativo.", nameof(abono));
        if (cuotasImpagas.Count == 0)
            throw new ArgumentException("El préstamo no tiene cuotas pendientes de cobro.", nameof(cuotasImpagas));

        // 1) El monto base se distribuye como siempre (paga cuota, adelanta la siguiente)
        var aplicaciones = montoBase > 0m
            ? DistribuirPago(montoBase, cuotasImpagas)
            : [];
        if (abono == 0m)
            return aplicaciones;

        // Capital que el monto base ya cubrió en cada cuota (para no cobrarlo dos veces)
        var capitalYaAplicado = aplicaciones.ToDictionary(a => a.Cuota.Id, a => a.CapitalAplicado);

        // 2) El abono va al capital de las cuotas que aún tienen capital pendiente,
        //    de la más próxima a la más lejana, exonerando su interés.
        var restante = abono;
        foreach (var cuota in cuotasImpagas)
        {
            if (restante <= 0m)
                break;

            var yaAplicado = capitalYaAplicado.GetValueOrDefault(cuota.Id, 0m);
            var capitalPendiente = CapitalPendiente(cuota) - yaAplicado;
            if (capitalPendiente <= 0m)
                continue;

            var aplicarCapital = Math.Min(restante, capitalPendiente);
            var quedaPagada = aplicarCapital == capitalPendiente;
            // Si esta cuota ya recibió pago base, se fusiona; si no, es nueva
            var existente = aplicaciones.FirstOrDefault(a => a.Cuota.Id == cuota.Id);
            var interesExonerado = quedaPagada ? InteresPendiente(cuota) : 0m;

            if (existente is not null)
                aplicaciones[aplicaciones.IndexOf(existente)] = existente with
                {
                    MontoAplicado = existente.MontoAplicado + aplicarCapital,
                    CapitalAplicado = existente.CapitalAplicado + aplicarCapital,
                    QuedaPagada = existente.QuedaPagada || quedaPagada,
                    InteresExonerado = existente.InteresExonerado + interesExonerado
                };
            else
                aplicaciones.Add(new AplicacionPago(
                    cuota, aplicarCapital, 0m, aplicarCapital, quedaPagada,
                    InteresExonerado: interesExonerado));

            restante -= aplicarCapital;
        }

        if (restante > 0m)
        {
            // El tope NO es el capital del préstamo: es el que queda DESPUÉS de
            // lo que el monto base ya cubrió. Antes el mensaje mostraba el
            // capital total y podía decir cosas como "el abono (333.33) excede
            // el capital pendiente (1,000.00)" —que no se entiende, porque no lo
            // excede— cuando lo que pasaba era que el cobro base se lo había
            // llevado entero.
            var disponible = cuotasImpagas.Sum(CapitalPendiente)
                             - capitalYaAplicado.Values.Sum();
            throw new ArgumentException(disponible <= 0m
                ? $"El cobro ya cubre todo el capital pendiente, así que no queda nada para abonar. " +
                  $"Quita el abono de {abono:N2} o cobra menos."
                : $"El abono ({abono:N2}) pasa el capital que queda por abonar ({disponible:N2}) " +
                  $"después de aplicar el cobro.");
        }

        return aplicaciones;
    }

    /// <summary>
    /// Monto necesario para liquidar hoy: cuotas vencidas o vigentes pagan su
    /// saldo completo; cuotas futuras pagan SOLO su capital pendiente
    /// (el interés futuro se exonera).
    /// </summary>
    public static decimal CalcularLiquidacion(IReadOnlyList<Cuota> cuotasImpagas, DateOnly hoy) =>
        cuotasImpagas.Sum(c => c.FechaVencimiento <= hoy ? c.SaldoPendiente : CapitalPendiente(c));

    /// <summary>Distribución de una liquidación anticipada: todas las cuotas quedan pagadas.</summary>
    public static List<AplicacionPago> DistribuirLiquidacion(IReadOnlyList<Cuota> cuotasImpagas, DateOnly hoy)
    {
        if (cuotasImpagas.Count == 0)
            throw new ArgumentException("El préstamo no tiene cuotas pendientes de cobro.", nameof(cuotasImpagas));

        var aplicaciones = new List<AplicacionPago>();
        foreach (var cuota in cuotasImpagas)
        {
            if (cuota.FechaVencimiento <= hoy)
            {
                // Vencida o vigente: se cobra completa (interés + capital pendientes)
                var interes = InteresPendiente(cuota);
                var capital = CapitalPendiente(cuota);
                aplicaciones.Add(new AplicacionPago(
                    cuota, interes + capital, interes, capital, QuedaPagada: true));
            }
            else
            {
                // Futura: solo capital; el interés pendiente se exonera
                var capital = CapitalPendiente(cuota);
                aplicaciones.Add(new AplicacionPago(
                    cuota, capital, 0m, capital, QuedaPagada: true,
                    InteresExonerado: InteresPendiente(cuota)));
            }
        }
        return aplicaciones;
    }

    // ============================================================
    // Registro transaccional
    // ============================================================

    /// <summary>
    /// Registra el cobro completo de forma atómica y devuelve el recibo listo
    /// para imprimir. Si el pago cubre todas las cuotas, el préstamo pasa a 'pagado'.
    /// </summary>
    public async Task<ResultadoPago> RegistrarPagoAsync(SolicitudPago solicitud, CancellationToken ct = default)
    {
        var prestamo = await _prestamos.ObtenerPorIdAsync(solicitud.PrestamoId, ct)
            ?? throw new InvalidOperationException($"No existe el préstamo con id {solicitud.PrestamoId}.");
        if (prestamo.Estado != EstadoPrestamo.Activo)
            throw new InvalidOperationException($"Solo se cobran préstamos activos (actual: {prestamo.Estado}).");

        var cliente = await _clientes.ObtenerPorIdAsync(prestamo.ClienteId, ct)
            ?? throw new InvalidOperationException($"No existe el cliente del préstamo {prestamo.Codigo}.");

        var hoy = FechaNegocio.Hoy;
        var fechaPagoUtc = DateTime.UtcNow;

        using var conexion = await _factory.AbrirAsync(ct);
        using var transaccion = await conexion.BeginTransactionAsync(ct);
        try
        {
            // FOR UPDATE: bloquea las cuotas hasta el COMMIT (sin dobles cobros)
            var cuotas = await _prestamos.ObtenerCuotasImpagasParaPagoAsync(
                solicitud.PrestamoId, conexion, transaccion, ct);
            if (cuotas.Count == 0)
                throw new InvalidOperationException($"El préstamo {prestamo.Codigo} no tiene cuotas pendientes.");

            var aplicaciones = solicitud.EsLiquidacion
                ? DistribuirLiquidacion(cuotas, hoy)
                : solicitud.AbonoCapital > 0m
                    ? DistribuirConAbono(solicitud.Monto, solicitud.AbonoCapital, cuotas, hoy)
                    : DistribuirPago(solicitud.Monto, cuotas);

            // Comprobante fiscal del cobro (041). Se resuelve ACA ADENTRO, con
            // las cuotas ya bloqueadas: la reserva de la secuencia usa FOR
            // UPDATE y tiene que vivir en la misma transaccion que el cobro, o
            // dos cajas cobrando a la vez se llevan el mismo numero. Si el
            // cobro despues falla, el rollback devuelve el NCF sin consumir.
            var ncfDelCobro = solicitud.AsignarNcfAuto
                ? await _ncf.ReservarSiguienteAsync(SesionActual.Modo, conexion, transaccion, hoy, ct)
                : string.IsNullOrWhiteSpace(solicitud.Ncf)
                    ? null
                    : solicitud.Ncf.Trim().ToUpperInvariant();

            // El saldo ANTES de tocar nada. Va aca y no despues del bucle
            // porque el bucle ahora actualiza las cuotas en memoria (las
            // necesita el recalculo del prestamo abierto): leerlo al final
            // devolveria el saldo YA descontado y el recibo restaria el cobro
            // dos veces.
            var saldoAntes = cuotas.Sum(c => c.SaldoPendiente);

            var pagosInsertados = new List<Pago>();
            var lineas = new List<ReciboLinea>();

            foreach (var aplicacion in aplicaciones)
            {
                var nuevoAcumulado = aplicacion.Cuota.MontoPagado + aplicacion.MontoAplicado;
                var nuevoCapital = aplicacion.Cuota.CapitalPagado + aplicacion.CapitalAplicado;
                var nuevoEstado = aplicacion.QuedaPagada ? EstadoCuota.Pagada : aplicacion.Cuota.Estado;

                if (aplicacion.MontoAplicado != 0m)
                {
                    var numeroRecibo = $"R-{await _contador.SiguienteAsync(ContadorRepository.Recibo, conexion, transaccion, ct):D6}";
                    var notas = aplicacion.InteresExonerado > 0m
                        ? AgregarNota(solicitud.Notas, $"Liquidación anticipada: interés exonerado {aplicacion.InteresExonerado:N2} DOP")
                        : solicitud.Notas;

                    var pago = new Pago
                    {
                        CuotaId = aplicacion.Cuota.Id,
                        NumeroRecibo = numeroRecibo,
                        FechaPagoUtc = fechaPagoUtc,
                        MontoPagado = aplicacion.MontoAplicado,
                        MontoInteres = aplicacion.InteresAplicado,
                        MontoCapital = aplicacion.CapitalAplicado,
                        MetodoPago = solicitud.MetodoPago,
                        // Solo la primera fila del cobro lleva el comprobante:
                        // es UN documento fiscal aunque toque varias cuotas, y
                        // uq_pago_ncf rechazaria el duplicado.
                        Ncf = pagosInsertados.Count == 0 ? ncfDelCobro : null,
                        Notas = notas
                    };
                    pago.Id = await _pagos.InsertarAsync(pago, conexion, transaccion, ct);
                    pagosInsertados.Add(pago);
                    lineas.Add(new ReciboLinea(numeroRecibo, aplicacion.Cuota.NumeroCuota,
                        aplicacion.InteresAplicado, aplicacion.CapitalAplicado, aplicacion.MontoAplicado,
                        aplicacion.QuedaPagada));
                }

                await _prestamos.ActualizarCuotaTrasPagoAsync(
                    aplicacion.Cuota.Id, nuevoAcumulado, nuevoCapital, nuevoEstado,
                    conexion, transaccion, ct);

                // El objeto en memoria queda al dia: el recalculo de mas abajo
                // lee estas mismas cuotas para saber cuanto capital falta.
                aplicacion.Cuota.MontoPagado = nuevoAcumulado;
                aplicacion.Cuota.CapitalPagado = nuevoCapital;
            }

            // Recalculo del interes sobre el capital rebajado (pedido del
            // cliente 2026-08-27). Ver RecalcularInteresAbiertoAsync.
            var capitalAbonado = aplicaciones.Sum(a => a.CapitalAplicado);
            var interesRecalculado = await RecalcularInteresAbiertoAsync(
                prestamo, cuotas, capitalAbonado, hoy, conexion, transaccion, ct);

            // Si el cobro cubrió TODAS las cuotas impagas, el préstamo queda pagado
            var prestamoQuedoPagado = aplicaciones.Count == cuotas.Count && aplicaciones.All(a => a.QuedaPagada);
            if (prestamoQuedoPagado)
                await _prestamos.ActualizarEstadoAsync(solicitud.PrestamoId, EstadoPrestamo.Pagado, conexion, transaccion, ct);

            var totalPagado = aplicaciones.Sum(a => a.MontoAplicado);
            var interesExonerado = aplicaciones.Sum(a => a.InteresExonerado);
            var reciboPrincipal = lineas.Count > 0 ? lineas[0].NumeroRecibo : "—";

            await _auditoria.RegistrarEnTransaccionAsync(AccionAuditoria.Crear, DbNames.Pago, pagosInsertados.FirstOrDefault()?.Id,
                $"Cobro {reciboPrincipal} de {totalPagado:N2} DOP al préstamo {prestamo.Codigo} " +
                $"({lineas.Count} cuota(s), {solicitud.MetodoPago})" +
                (solicitud.EsLiquidacion ? $" — liquidación anticipada, interés exonerado {interesExonerado:N2} DOP" : string.Empty) +
                (prestamoQuedoPagado ? " — préstamo saldado" : string.Empty) +
                (interesRecalculado is null ? string.Empty :
                    $" — interés recalculado a {interesRecalculado.Value.Interes:N2} DOP " +
                    $"sobre el capital pendiente de {interesRecalculado.Value.Capital:N2} DOP " +
                    $"({interesRecalculado.Value.Cuotas} cuota(s) por vencer)") +
                (ncfDelCobro is null ? string.Empty :
                    $" — comprobante fiscal {ncfDelCobro}" +
                    (solicitud.AsignarNcfAuto ? " (de la secuencia)" : " (registrado externo)")),
                conexion, transaccion, ct);

            await transaccion.CommitAsync(ct);

            // El comprobante digitado a mano pasa a ser el predeterminado
            // (2026-09-03). Va DESPUES del commit y no puede tumbar la operacion.
            if (!solicitud.AsignarNcfAuto)
                await NcfPredeterminado.AdoptarAsync(_ncf, SesionActual.Modo, ncfDelCobro, ct);

            var recibo = new ReciboPago(
                reciboPrincipal,
                fechaPagoUtc,
                cliente.NombreCompleto,
                prestamo.Codigo,
                lineas,
                totalPagado,
                solicitud.MetodoPago,
                Math.Max(0m, saldoAntes - totalPagado - interesExonerado),
                interesExonerado,
                solicitud.Notas,
                SesionActual.Nombre,
                NegocioNombre: _ajustes.NombreNegocio,
                NegocioRnc: _ajustes.RncNegocio,
                NegocioTelefono: _ajustes.TelefonoNegocio,
                // El comprobante del COBRO, no el del prestamo (041). Hasta el
                // 2026-08-26 aca iba prestamo.Ncf, asi que las 24 facturas de un
                // prestamo salian con el mismo numero repetido — que es
                // justamente lo que el cliente pidio corregir. Si este cobro no
                // lleva comprobante, el recibo no muestra ninguno.
                Ncf: ncfDelCobro);

            Log.Information("Cobro {Recibo} registrado: {Monto:N2} DOP al préstamo {Codigo} ({Cuotas} cuotas)",
                reciboPrincipal, totalPagado, prestamo.Codigo, lineas.Count);

            return new ResultadoPago(pagosInsertados, prestamoQuedoPagado, recibo);
        }
        catch
        {
            await transaccion.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    // ============================================================
    // Recalculo del interes sobre capital rebajado (2026-08-27)
    // ============================================================

    /// <summary>Lo que hizo el recalculo, para la auditoria. Null = no aplico.</summary>
    private readonly record struct RecalculoInteres(decimal Capital, decimal Interes, int Cuotas);

    /// <summary>
    /// Pedido del cliente (2026-08-27, reenviado por Jean Carlo):
    ///
    ///   "si un cliente toma RD$1,000,000 y va pagando sus intereses
    ///    mensualmente, pero en noviembre realiza un abono de RD$200,000 al
    ///    capital [...] A partir del siguiente mes, los intereses deben
    ///    calcularse unicamente sobre los RD$800,000 restantes, no sobre el
    ///    millon original."
    ///
    /// SOLO PARA EL PRESTAMO ABIERTO (decision de Yuber, 2026-08-27). En
    /// frances y en cuota fija el capital ya baja por diseno y la tabla se
    /// pacta completa al firmar; reescribirla ahi cambiaria un contrato. El
    /// abierto es distinto: no hay tabla pactada, el cliente paga interes sobre
    /// lo que debe, y es donde vive la mayor parte de la cartera (7 de los 10
    /// prestamos del listado del 29-07-2026).
    ///
    /// QUE CUOTAS SE TOCAN: solo las que NO han vencido y NO estan pagadas.
    /// El interes de una cuota ya vencida se devengo sobre plata que el deudor
    /// efectivamente tenia; bajarlo hacia atras le perdonaria interes ya
    /// ganado, y eso no lo decide el sistema. El interes YA COBRADO nunca se
    /// toca: esto reescribe deuda futura, no historia.
    /// </summary>
    private async Task<RecalculoInteres?> RecalcularInteresAbiertoAsync(
        Prestamo prestamo, IReadOnlyList<Cuota> cuotas, decimal capitalAbonado,
        DateOnly hoy, MySqlConnection conexion, MySqlTransaction transaccion,
        CancellationToken ct)
    {
        if (prestamo.MetodoAmortizacion != MetodoAmortizacion.SoloInteres || capitalAbonado <= 0m)
            return null;

        // Capital que queda debiendo el prestamo, de la fuente verdadera:
        // lo pactado menos lo efectivamente cubierto (043).
        var capitalPendiente = Math.Max(0m, prestamo.MontoCapital - cuotas.Sum(c => c.CapitalPagado));

        var tasa = AmortizacionService.TasaPorPeriodo(prestamo.TasaInteres, prestamo.Modalidad);
        var interesNuevo = Math.Round(capitalPendiente * tasa, 2, MidpointRounding.AwayFromZero);

        // Solo las que todavia no vencen y siguen debiendo algo
        var porVencer = cuotas
            .Where(c => c.FechaVencimiento > hoy && c.Estado != EstadoCuota.Pagada)
            .ToList();
        if (porVencer.Count == 0)
            return null;

        var ultima = cuotas.Max(c => c.NumeroCuota);
        var tocadas = 0;
        foreach (var cuota in porVencer)
        {
            // El capital PACTADO de la cuota no se toca: lo abonado vive en
            // capital_pagado. Mutar `capital` borraria de que tamano era el
            // compromiso original.
            var montoTotal = cuota.Capital + interesNuevo;
            // En el abierto el saldo se arrastra hasta la ultima, que lo salda
            var saldoDespues = cuota.NumeroCuota == ultima ? 0m : capitalPendiente;

            if (cuota.Interes == interesNuevo && cuota.SaldoDespues == saldoDespues)
                continue;

            await _prestamos.ActualizarInteresCuotaAsync(
                cuota.Id, interesNuevo, montoTotal, saldoDespues, conexion, transaccion, ct);

            cuota.Interes = interesNuevo;
            cuota.MontoTotal = montoTotal;
            cuota.SaldoDespues = saldoDespues;
            tocadas++;
        }

        if (tocadas == 0)
            return null;

        Log.Information(
            "Prestamo abierto {Codigo}: abono de {Abono:N2} DOP — interes recalculado a " +
            "{Interes:N2} sobre capital pendiente {Capital:N2} en {Cuotas} cuota(s) por vencer",
            prestamo.Codigo, capitalAbonado, interesNuevo, capitalPendiente, tocadas);

        return new RecalculoInteres(capitalPendiente, interesNuevo, tocadas);
    }

    public Task<IReadOnlyList<PagoResumen>> ObtenerRecientesAsync(int limite = 20, CancellationToken ct = default) =>
        _pagos.ObtenerRecientesAsync(limite, ct);

    private static string AgregarNota(string? notas, string extra) =>
        string.IsNullOrWhiteSpace(notas) ? extra : $"{notas} | {extra}";
}
