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

    public PrestamoService(ConexionFactory factory, PrestamoRepository prestamos,
        ContadorRepository contador, AmortizacionService amortizacion, AuditoriaService auditoria,
        VehiculoRepository vehiculos, NcfRepository ncf)
    {
        _factory = factory;
        _prestamos = prestamos;
        _contador = contador;
        _amortizacion = amortizacion;
        _auditoria = auditoria;
        _vehiculos = vehiculos;
        _ncf = ncf;
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
            solicitud.FechaPrimerPago));

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
                ? await _ncf.ReservarSiguienteAsync(conexion, transaccion, FechaNegocio.Hoy, ct)
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
                FechaInicio = solicitud.FechaPrimerPago,
                // Garantía: la del formulario o, en un crédito vehicular, el propio vehículo.
                Garantia = string.IsNullOrWhiteSpace(solicitud.Garantia) && vehiculo is not null
                    ? $"Vehículo {vehiculo.Codigo} — {vehiculo.Descripcion}"
                    : solicitud.Garantia,
                Notas = solicitud.Notas
            };

            var id = await _prestamos.InsertarAsync(prestamo, conexion, transaccion, ct);
            await _prestamos.InsertarCuotasAsync(id, tabla, conexion, transaccion, ct);

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
            await _auditoria.RegistrarEnTransaccionAsync(AccionAuditoria.Crear, DbNames.Prestamo, id,
                $"Préstamo {codigo}: capital {solicitud.MontoCapital:N2} DOP, " +
                $"{solicitud.PlazoCuotas} cuotas {solicitud.Modalidad}, " +
                $"tasa {solicitud.TasaInteresMensual}% mensual — {quienAutorizo}{detalleVehiculo}{detalleNcf}",
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

    public Task<Prestamo?> ObtenerPorIdAsync(long id, CancellationToken ct = default) =>
        _prestamos.ObtenerPorIdAsync(id, ct);

    public Task<IReadOnlyList<Cuota>> ObtenerCuotasAsync(long prestamoId, CancellationToken ct = default) =>
        _prestamos.ObtenerCuotasAsync(prestamoId, ct);
}
