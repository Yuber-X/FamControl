using FAControl.Common;
using FAControl.Data;
using FAControl.Models;
using Serilog;

namespace FAControl.Services;

/// <summary>
/// Lógica de negocio de préstamos. Crear y cancelar son operaciones multi-paso
/// que se ejecutan dentro de UNA MySqlTransaction (regla 3 del CLAUDE.md):
/// nunca puede quedar un préstamo sin cuotas ni una cancelación a medias.
/// </summary>
public class PrestamoService
{
    private readonly ConexionFactory _factory;
    private readonly PrestamoRepository _prestamos;
    private readonly ContadorRepository _contador;
    private readonly AmortizacionService _amortizacion;
    private readonly AuditoriaService _auditoria;
    private readonly VehiculoRepository _vehiculos;
    private readonly NcfRepository _ncf;
    private readonly PagoRepository _pagos;

    public PrestamoService(ConexionFactory factory, PrestamoRepository prestamos,
        ContadorRepository contador, AmortizacionService amortizacion, AuditoriaService auditoria,
        VehiculoRepository vehiculos, NcfRepository ncf, PagoRepository pagos)
    {
        _factory = factory;
        _prestamos = prestamos;
        _contador = contador;
        _amortizacion = amortizacion;
        _auditoria = auditoria;
        _vehiculos = vehiculos;
        _ncf = ncf;
        _pagos = pagos;
    }

    /// <summary>
    /// Crea el préstamo completo de forma atómica:
    /// contador (FOR UPDATE) → prestamo → N cuotas → auditoría → COMMIT.
    /// Devuelve el id y el código visible (P-0001).
    ///
    /// AUTORIZACIÓN (regla del cliente 2026-07-16): todo préstamo nuevo necesita
    /// el visto bueno de alguien con permiso 'prestamos_autorizar'. Si quien lo
    /// crea ya lo tiene, se autoriza solo; si no (un cobrador), debe venir una
    /// <paramref name="autorizacion"/> emitida por AutorizacionService tras
    /// validar la contraseña del admin. Sin ella, esto TIRA y no se crea nada.
    ///
    /// La regla se aplica ACÁ y no en el ViewModel: la UI puede olvidarse, el
    /// servicio no.
    /// </summary>
    public async Task<(long Id, string Codigo)> CrearAsync(NuevoPrestamo solicitud,
        AutorizacionPrestamo? autorizacion = null, CancellationToken ct = default)
    {
        if (!SesionActual.TienePermiso(Permisos.PrestamosCrear))
            throw new UnauthorizedAccessException("No tenés permiso para crear préstamos.");

        var aprobador = AutorizacionService.UsuarioActualPuedeAutorizar
            ? AutorizacionService.DelUsuarioActual()
            : autorizacion ?? throw new UnauthorizedAccessException(
                "Un préstamo nuevo necesita la autorización de un administrador.");

        // Calcular ANTES de abrir la transacción: valida los parámetros y
        // produce la tabla definitiva que se persiste tal cual se mostró en el preview.
        var tabla = _amortizacion.Calcular(new ParametrosAmortizacion(
            solicitud.MontoCapital,
            solicitud.TasaInteresMensual,
            solicitud.PlazoCuotas,
            solicitud.Modalidad,
            solicitud.Metodo,
            solicitud.FechaPrimerPago,
            solicitud.CuotaInicioCapital));

        // AutoControl: si el préstamo financia un vehículo, valida que esté
        // disponible ANTES de la transacción y usa su descripción como garantía.
        Vehiculo? vehiculo = null;
        if (solicitud.VehiculoId is { } vehiculoId)
        {
            vehiculo = await _vehiculos.ObtenerPorIdAsync(vehiculoId, ct)
                ?? throw new InvalidOperationException("El vehículo a financiar no existe o fue eliminado.");
            if (vehiculo.Estado is EstadoVehiculo.Vendido)
                throw new InvalidOperationException($"El vehículo {vehiculo.Codigo} ya está vendido.");
            if (vehiculo.Estado is EstadoVehiculo.Alquilado)
                throw new InvalidOperationException($"El vehículo {vehiculo.Codigo} está alquilado; no se puede financiar.");
        }

        using var conexion = await _factory.AbrirAsync(ct);
        using var transaccion = await conexion.BeginTransactionAsync(ct);
        try
        {
            var numero = await _contador.SiguienteAsync(ContadorRepository.Prestamo, conexion, transaccion, ct);
            var codigo = $"P-{numero:D4}";

            // Comprobante fiscal (pedido 2026-07-25): pegado a mano (Facturador
            // Gratuito DGII) o reservado de la secuencia local, DENTRO de la
            // misma transacción — un rollback no consume el número.
            var ncf = solicitud.AsignarNcfAuto
                ? await _ncf.ReservarSiguienteAsync(SesionActual.Modo, conexion, transaccion, FechaNegocio.Hoy, ct)
                : string.IsNullOrWhiteSpace(solicitud.Ncf) ? null : solicitud.Ncf.Trim().ToUpperInvariant();

            var prestamo = new Prestamo
            {
                Codigo = codigo,
                Ncf = ncf,
                ClienteId = solicitud.ClienteId,
                VehiculoId = solicitud.VehiculoId,
                MontoCapital = solicitud.MontoCapital,
                TasaInteres = solicitud.TasaInteresMensual,
                PlazoCuotas = solicitud.PlazoCuotas,
                Modalidad = solicitud.Modalidad,
                MetodoAmortizacion = solicitud.Metodo,
                // Solo tiene sentido en el diferido: en los otros métodos se
                // guarda NULL para que la ficha no muestre un dato que no aplica.
                CuotaInicioCapital = solicitud.Metodo == MetodoAmortizacion.CapitalDiferido
                    ? solicitud.CuotaInicioCapital ??
                      AmortizacionService.CuotaInicioCapitalSugerida(solicitud.PlazoCuotas)
                    : null,
                FechaInicio = solicitud.FechaPrimerPago,
                // Garantía: la del formulario o, en un crédito vehicular, el propio vehículo.
                Garantia = string.IsNullOrWhiteSpace(solicitud.Garantia) && vehiculo is not null
                    ? $"Vehículo {vehiculo.Codigo} — {vehiculo.Descripcion}"
                    : solicitud.Garantia,
                Notas = solicitud.Notas
            };

            var id = await _prestamos.InsertarAsync(prestamo, conexion, transaccion, ct);
            await _prestamos.InsertarCuotasAsync(id, tabla, conexion, transaccion, ct);

            // Préstamo ANTIGUO (pedido 2026-07-25): las primeras N cuotas nacen
            // pagadas con recibos HISTÓRICOS fechados en su vencimiento (así los
            // reportes las ubican en su mes real, no en el de hoy). Todo en la
            // misma transacción.
            var cuotasHistoricas = Math.Clamp(solicitud.CuotasPagadasAlCrear, 0, tabla.Count);
            if (cuotasHistoricas > 0)
            {
                var cuotas = await _prestamos.ObtenerCuotasImpagasParaPagoAsync(id, conexion, transaccion, ct);
                foreach (var cuota in cuotas.OrderBy(c => c.NumeroCuota).Take(cuotasHistoricas))
                {
                    var numeroRecibo = $"R-{await _contador.SiguienteAsync(ContadorRepository.Recibo, conexion, transaccion, ct):D6}";
                    // Mediodía local RD (UTC-4) del día de vencimiento
                    var fechaHistoricaUtc = cuota.FechaVencimiento.ToDateTime(new TimeOnly(12, 0)).AddHours(4);
                    var pago = new Pago
                    {
                        CuotaId = cuota.Id,
                        NumeroRecibo = numeroRecibo,
                        FechaPagoUtc = fechaHistoricaUtc,
                        MontoPagado = cuota.MontoTotal,
                        MontoInteres = cuota.Interes,
                        MontoCapital = cuota.Capital,
                        MetodoPago = MetodoPago.Otro,
                        Notas = "Registro histórico — préstamo antiguo cargado al sistema"
                    };
                    await _pagos.InsertarAsync(pago, conexion, transaccion, ct);
                    await _prestamos.ActualizarCuotaTrasPagoAsync(
                        cuota.Id, cuota.MontoTotal, EstadoCuota.Pagada, conexion, transaccion, ct);
                }
                // Caso borde: el cliente ya había pagado TODO el préstamo
                if (cuotasHistoricas == tabla.Count)
                    await _prestamos.ActualizarEstadoAsync(id, EstadoPrestamo.Pagado, conexion, transaccion, ct);
            }

            // El vehículo financiado sale del inventario: pasa a 'vendido' en la MISMA transacción.
            if (vehiculo is not null)
                await _vehiculos.CambiarEstadoAsync(vehiculo.Id, EstadoVehiculo.Vendido, conexion, transaccion, ct);

            // Quién autorizó queda en la auditoría: sin eso la regla no sirve
            // para rendir cuentas después.
            var quienAutorizo = aprobador.UsuarioId == SesionActual.Id
                ? "autorizado por él mismo"
                : $"autorizado por {aprobador.Username}";
            var detalleVehiculo = vehiculo is null ? "" : $" — financia el vehículo {vehiculo.Codigo}";
            var detalleNcf = ncf is null ? "" : $" — comprobante fiscal {ncf}";
            var detalleHistorico = cuotasHistoricas > 0
                ? $" — préstamo antiguo: {cuotasHistoricas} cuota(s) marcadas pagadas con recibos históricos"
                : "";
            await _auditoria.RegistrarEnTransaccionAsync(AccionAuditoria.Crear, DbNames.Prestamo, id,
                $"Préstamo {codigo}: capital {solicitud.MontoCapital:N2} DOP, " +
                $"{solicitud.PlazoCuotas} cuotas {solicitud.Modalidad}, " +
                $"tasa {solicitud.TasaInteresMensual}% mensual — {quienAutorizo}{detalleVehiculo}{detalleNcf}{detalleHistorico}",
                conexion, transaccion, ct);

            await transaccion.CommitAsync(ct);
            Log.Information("Préstamo {Codigo} creado (id {Id}) para cliente {ClienteId}, autorizado por {Autorizador}",
                codigo, id, solicitud.ClienteId, aprobador.Username);
            return (id, codigo);
        }
        catch
        {
            await transaccion.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    /// <summary>
    /// Hasta dónde se puede corregir este préstamo (029). La UI lo consulta
    /// para habilitar o bloquear campos; <see cref="EditarAsync"/> lo vuelve a
    /// verificar por su cuenta, porque la pantalla puede quedar abierta
    /// mientras otro cajero cobra.
    /// </summary>
    public async Task<EdicionPermitida> ConsultarEdicionPermitidaAsync(long prestamoId,
        CancellationToken ct = default)
    {
        var cobros = await _prestamos.ContarCobrosAsync(prestamoId, ct);
        return cobros == 0 ? EdicionPermitida.Completa() : EdicionPermitida.Limitada(cobros);
    }

    /// <summary>
    /// Corrige un préstamo ya registrado (029 — pedido del cliente 2026-07-30:
    /// "así si se produce un error de digitación se pueda arreglar").
    ///
    /// DOS NIVELES, según si ya se cobró algo:
    ///  * SIN cobros → se corrige todo y la tabla de amortización se regenera
    ///    con los números nuevos, dentro de la misma transacción.
    ///  * CON cobros → solo garantía y notas. Los montos, el plazo y las fechas
    ///    quedan fijos: hay recibos impresos en manos del cliente que declaran
    ///    esos números, y recalcular por detrás los haría mentir. Si de verdad
    ///    hay que cambiar el dinero, lo correcto es cancelar y volver a prestar,
    ///    que deja rastro de ambas cosas.
    ///
    /// Lo que NUNCA se toca: el código (P-0001), el comprobante fiscal y el
    /// cliente. Los dos primeros ya se emitieron; mover el préstamo a otra
    /// persona no es corregir un tipeo, es otro préstamo.
    /// </summary>
    public async Task EditarAsync(EdicionPrestamo cambios, CancellationToken ct = default)
    {
        if (!SesionActual.EsAdmin && !SesionActual.TienePermiso(Permisos.PrestamosEditar))
            throw new UnauthorizedAccessException("No tenés permiso para corregir préstamos.");
        if (string.IsNullOrWhiteSpace(cambios.Motivo))
            throw new ArgumentException("Indicá por qué se corrige el préstamo: queda en el historial.",
                nameof(cambios));

        var prestamo = await _prestamos.ObtenerPorIdAsync(cambios.PrestamoId, ct)
            ?? throw new InvalidOperationException($"No existe el préstamo con id {cambios.PrestamoId}.");

        // Se relee acá y no se confía en lo que trajo la pantalla: entre que se
        // abrió el formulario y se guardó, otro usuario pudo registrar un cobro.
        var permitido = await ConsultarEdicionPermitidaAsync(cambios.PrestamoId, ct);

        // Los descriptivos van siempre. `Prestamo` es una clase, así que se
        // anota el detalle ANTES de tocarlo: después ya no queda el valor viejo.
        var detalle = new List<string>();
        if (prestamo.Garantia != cambios.Garantia) detalle.Add("garantía");
        if (prestamo.Notas != cambios.Notas) detalle.Add("notas");

        IReadOnlyList<CuotaCalculada>? tabla = null;
        if (permitido.Todo)
        {
            // Calcular ANTES de abrir la transacción: valida los parámetros y
            // deja lista la tabla definitiva (mismo criterio que CrearAsync).
            tabla = _amortizacion.Calcular(new ParametrosAmortizacion(
                cambios.MontoCapital, cambios.TasaInteresMensual, cambios.PlazoCuotas,
                cambios.Modalidad, cambios.Metodo, cambios.FechaPrimerPago,
                cambios.CuotaInicioCapital));

            if (prestamo.MontoCapital != cambios.MontoCapital)
                detalle.Add($"capital {prestamo.MontoCapital:N2} → {cambios.MontoCapital:N2}");
            if (prestamo.TasaInteres != cambios.TasaInteresMensual)
                detalle.Add($"tasa {prestamo.TasaInteres:0.##}% → {cambios.TasaInteresMensual:0.##}%");
            if (prestamo.PlazoCuotas != cambios.PlazoCuotas)
                detalle.Add($"plazo {prestamo.PlazoCuotas} → {cambios.PlazoCuotas} cuotas");
            if (prestamo.Modalidad != cambios.Modalidad)
                detalle.Add($"modalidad {prestamo.Modalidad} → {cambios.Modalidad}");
            if (prestamo.MetodoAmortizacion != cambios.Metodo)
                detalle.Add($"método {prestamo.MetodoAmortizacion} → {cambios.Metodo}");
            if (prestamo.FechaInicio != cambios.FechaPrimerPago)
                detalle.Add($"primer pago {prestamo.FechaInicio:dd/MM/yyyy} → {cambios.FechaPrimerPago:dd/MM/yyyy}");

            prestamo.MontoCapital = cambios.MontoCapital;
            prestamo.TasaInteres = cambios.TasaInteresMensual;
            prestamo.PlazoCuotas = cambios.PlazoCuotas;
            prestamo.Modalidad = cambios.Modalidad;
            prestamo.MetodoAmortizacion = cambios.Metodo;
            prestamo.CuotaInicioCapital = cambios.Metodo == MetodoAmortizacion.CapitalDiferido
                ? cambios.CuotaInicioCapital ??
                  AmortizacionService.CuotaInicioCapitalSugerida(cambios.PlazoCuotas)
                : null;
            prestamo.FechaInicio = cambios.FechaPrimerPago;
        }

        prestamo.Garantia = cambios.Garantia;
        prestamo.Notas = cambios.Notas;

        if (detalle.Count == 0)
            return;   // Nada cambió: no se ensucia la auditoría con un registro vacío

        using var conexion = await _factory.AbrirAsync(ct);
        using var transaccion = await conexion.BeginTransactionAsync(ct);
        try
        {
            await _prestamos.ActualizarDatosAsync(prestamo, conexion, transaccion, ct);

            if (tabla is not null)
            {
                // Sin cobros, la tabla es un cálculo derivado: se rehace entera.
                await _prestamos.BorrarCuotasAsync(cambios.PrestamoId, conexion, transaccion, ct);
                await _prestamos.InsertarCuotasAsync(cambios.PrestamoId, tabla, conexion, transaccion, ct);
            }

            await _auditoria.RegistrarEnTransaccionAsync(AccionAuditoria.Modificar, DbNames.Prestamo,
                cambios.PrestamoId,
                $"Préstamo {prestamo.Codigo} corregido ({string.Join(", ", detalle)}). " +
                $"Motivo: {cambios.Motivo.Trim()}" +
                (tabla is not null ? " — tabla de amortización regenerada" : " — sin tocar montos: ya tiene cobros"),
                conexion, transaccion, ct);

            await transaccion.CommitAsync(ct);
            Log.Information("Préstamo {Codigo} corregido por {Usuario}: {Detalle}. Motivo: {Motivo}",
                prestamo.Codigo, SesionActual.Username, string.Join(", ", detalle), cambios.Motivo);
        }
        catch
        {
            await transaccion.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    /// <summary>
    /// Cancela un préstamo activo: estado 'cancelado' + cuotas impagas → 'cancelada'
    /// + auditoría, todo en una transacción. Las cuotas jamás se borran.
    /// </summary>
    public async Task CancelarAsync(long prestamoId, string? motivo, CancellationToken ct = default)
    {
        var prestamo = await _prestamos.ObtenerPorIdAsync(prestamoId, ct)
            ?? throw new InvalidOperationException($"No existe el préstamo con id {prestamoId}.");
        if (prestamo.Estado != EstadoPrestamo.Activo)
            throw new InvalidOperationException($"Solo se puede cancelar un préstamo activo (actual: {prestamo.Estado}).");

        using var conexion = await _factory.AbrirAsync(ct);
        using var transaccion = await conexion.BeginTransactionAsync(ct);
        try
        {
            await _prestamos.ActualizarEstadoAsync(prestamoId, EstadoPrestamo.Cancelado, conexion, transaccion, ct);
            await _prestamos.CancelarCuotasImpagasAsync(prestamoId, conexion, transaccion, ct);
            await _auditoria.RegistrarEnTransaccionAsync(AccionAuditoria.Modificar, DbNames.Prestamo, prestamoId,
                $"Préstamo {prestamo.Codigo} cancelado. Motivo: {motivo ?? "no indicado"}",
                conexion, transaccion, ct);

            await transaccion.CommitAsync(ct);
            Log.Information("Préstamo {Codigo} cancelado. Motivo: {Motivo}", prestamo.Codigo, motivo);
        }
        catch
        {
            await transaccion.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public Task<IReadOnlyList<PrestamoResumen>> ObtenerResumenesAsync(
        bool? soloVehiculares = null, CancellationToken ct = default) =>
        _prestamos.ObtenerResumenesAsync(soloVehiculares, ct);

    /// <summary>Vehículos disponibles para financiar (AutoControl: picker de nueva venta).</summary>
    public Task<IReadOnlyList<VehiculoResumen>> ObtenerVehiculosDisponiblesAsync(CancellationToken ct = default) =>
        _vehiculos.ObtenerResumenesAsync(ct);

    /// <summary>
    /// Tabla de amortización SIN persistir, para las vistas previas (029). Es
    /// el mismo cálculo que usa <see cref="EditarAsync"/>, así lo que el usuario
    /// ve mientras tipea coincide exacto con lo que se va a guardar.
    /// </summary>
    public IReadOnlyList<CuotaCalculada> CalcularAmortizacion(ParametrosAmortizacion parametros) =>
        _amortizacion.Calcular(parametros);

    public Task<Prestamo?> ObtenerPorIdAsync(long id, CancellationToken ct = default) =>
        _prestamos.ObtenerPorIdAsync(id, ct);

    public Task<IReadOnlyList<Cuota>> ObtenerCuotasAsync(long prestamoId, CancellationToken ct = default) =>
        _prestamos.ObtenerCuotasAsync(prestamoId, ct);
}
