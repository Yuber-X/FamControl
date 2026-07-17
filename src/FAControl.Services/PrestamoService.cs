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

    public PrestamoService(ConexionFactory factory, PrestamoRepository prestamos,
        ContadorRepository contador, AmortizacionService amortizacion, AuditoriaService auditoria)
    {
        _factory = factory;
        _prestamos = prestamos;
        _contador = contador;
        _amortizacion = amortizacion;
        _auditoria = auditoria;
    }

    /// <summary>
    /// Crea el préstamo completo de forma atómica:
    /// contador (FOR UPDATE) → prestamo → N cuotas → auditoría → COMMIT.
    /// Devuelve el id y el código visible (P-0001).
    /// </summary>
    public async Task<(long Id, string Codigo)> CrearAsync(NuevoPrestamo solicitud, CancellationToken ct = default)
    {
        // Calcular ANTES de abrir la transacción: valida los parámetros y
        // produce la tabla definitiva que se persiste tal cual se mostró en el preview.
        var tabla = _amortizacion.Calcular(new ParametrosAmortizacion(
            solicitud.MontoCapital,
            solicitud.TasaInteresMensual,
            solicitud.PlazoCuotas,
            solicitud.Modalidad,
            solicitud.Metodo,
            solicitud.FechaPrimerPago));

        using var conexion = await _factory.AbrirAsync(ct);
        using var transaccion = await conexion.BeginTransactionAsync(ct);
        try
        {
            var numero = await _contador.SiguienteAsync(ContadorRepository.Prestamo, conexion, transaccion, ct);
            var codigo = $"P-{numero:D4}";

            var prestamo = new Prestamo
            {
                Codigo = codigo,
                ClienteId = solicitud.ClienteId,
                MontoCapital = solicitud.MontoCapital,
                TasaInteres = solicitud.TasaInteresMensual,
                PlazoCuotas = solicitud.PlazoCuotas,
                Modalidad = solicitud.Modalidad,
                MetodoAmortizacion = solicitud.Metodo,
                FechaInicio = solicitud.FechaPrimerPago,
                Garantia = solicitud.Garantia,
                Notas = solicitud.Notas
            };

            var id = await _prestamos.InsertarAsync(prestamo, conexion, transaccion, ct);
            await _prestamos.InsertarCuotasAsync(id, tabla, conexion, transaccion, ct);
            await _auditoria.RegistrarEnTransaccionAsync(AccionAuditoria.Crear, DbNames.Prestamo, id,
                $"Préstamo {codigo}: capital {solicitud.MontoCapital:N2} DOP, " +
                $"{solicitud.PlazoCuotas} cuotas {solicitud.Modalidad}, tasa {solicitud.TasaInteresMensual}% mensual",
                conexion, transaccion, ct);

            await transaccion.CommitAsync(ct);
            Log.Information("Préstamo {Codigo} creado (id {Id}) para cliente {ClienteId}",
                codigo, id, solicitud.ClienteId);
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

    public Task<IReadOnlyList<PrestamoResumen>> ObtenerResumenesAsync(CancellationToken ct = default) =>
        _prestamos.ObtenerResumenesAsync(ct);

    public Task<Prestamo?> ObtenerPorIdAsync(long id, CancellationToken ct = default) =>
        _prestamos.ObtenerPorIdAsync(id, ct);

    public Task<IReadOnlyList<Cuota>> ObtenerCuotasAsync(long prestamoId, CancellationToken ct = default) =>
        _prestamos.ObtenerCuotasAsync(prestamoId, ct);
}
