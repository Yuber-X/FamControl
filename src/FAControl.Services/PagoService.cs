using FAControl.Common;
using FAControl.Data;
using FAControl.Models;
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

    public PagoService(ConexionFactory factory, PrestamoRepository prestamos, PagoRepository pagos,
        ClienteRepository clientes, ContadorRepository contador, AuditoriaService auditoria)
    {
        _factory = factory;
        _prestamos = prestamos;
        _pagos = pagos;
        _clientes = clientes;
        _contador = contador;
        _auditoria = auditoria;
    }

    // ============================================================
    // Lógica pura de distribución (sin BD — 100% testeable)
    // ============================================================

    /// <summary>Interés aún no cubierto de la cuota (los abonos aplican primero a interés).</summary>
    public static decimal InteresPendiente(Cuota cuota) =>
        Math.Max(0m, cuota.Interes - cuota.MontoPagado);

    /// <summary>Capital aún no cubierto de la cuota.</summary>
    public static decimal CapitalPendiente(Cuota cuota) =>
        cuota.Capital - Math.Max(0m, cuota.MontoPagado - cuota.Interes);

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
            var capitalTotal = cuotasImpagas.Sum(CapitalPendiente);
            throw new ArgumentException(
                $"El abono ({abono:N2}) excede el capital pendiente del préstamo ({capitalTotal:N2}).");
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

            var pagosInsertados = new List<Pago>();
            var lineas = new List<ReciboLinea>();

            foreach (var aplicacion in aplicaciones)
            {
                var nuevoAcumulado = aplicacion.Cuota.MontoPagado + aplicacion.MontoAplicado;
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
                        Notas = notas
                    };
                    pago.Id = await _pagos.InsertarAsync(pago, conexion, transaccion, ct);
                    pagosInsertados.Add(pago);
                    lineas.Add(new ReciboLinea(numeroRecibo, aplicacion.Cuota.NumeroCuota,
                        aplicacion.InteresAplicado, aplicacion.CapitalAplicado, aplicacion.MontoAplicado));
                }

                await _prestamos.ActualizarCuotaTrasPagoAsync(
                    aplicacion.Cuota.Id, nuevoAcumulado, nuevoEstado, conexion, transaccion, ct);
            }

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
                (prestamoQuedoPagado ? " — préstamo saldado" : string.Empty),
                conexion, transaccion, ct);

            await transaccion.CommitAsync(ct);

            var saldoAntes = cuotas.Sum(c => c.SaldoPendiente);
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
                SesionActual.Nombre);

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

    public Task<IReadOnlyList<PagoResumen>> ObtenerRecientesAsync(int limite = 20, CancellationToken ct = default) =>
        _pagos.ObtenerRecientesAsync(limite, ct);

    private static string AgregarNota(string? notas, string extra) =>
        string.IsNullOrWhiteSpace(notas) ? extra : $"{notas} | {extra}";
}
