using FAControl.Common;
using FAControl.Data;
using FAControl.Models;
using Serilog;

namespace FAControl.Services;

/// <summary>
/// Gestión de importación: ledger de gastos por vehículo (aduana, flete, etc.).
/// Agregar o quitar un gasto recalcula vehiculo.gastos_importacion = SUMA del
/// ledger, todo en una transacción. Así el costo total del vehículo queda al día.
/// </summary>
public class VehiculoGastoService
{
    private readonly VehiculoGastoRepository _gastos;
    private readonly VehiculoRepository _vehiculos;
    private readonly ConexionFactory _factory;
    private readonly AuditoriaService _auditoria;

    public VehiculoGastoService(VehiculoGastoRepository gastos, VehiculoRepository vehiculos,
        ConexionFactory factory, AuditoriaService auditoria)
    {
        _gastos = gastos;
        _vehiculos = vehiculos;
        _factory = factory;
        _auditoria = auditoria;
    }

    public Task<IReadOnlyList<VehiculoGasto>> ObtenerPorVehiculoAsync(long vehiculoId, CancellationToken ct = default)
    {
        ExigirLectura();
        return _gastos.ObtenerPorVehiculoAsync(vehiculoId, ct);
    }

    public async Task<long> AgregarAsync(VehiculoGastoDatos datos, CancellationToken ct = default)
    {
        ExigirEscritura();
        var concepto = datos.Concepto.Trim();
        if (concepto.Length == 0)
            throw new ArgumentException("El concepto es obligatorio.");
        if (datos.Monto <= 0m)
            throw new ArgumentException("El monto debe ser mayor que cero.");

        var vehiculo = await _vehiculos.ObtenerPorIdAsync(datos.VehiculoId, ct)
            ?? throw new InvalidOperationException("El vehículo no existe o fue eliminado.");

        var gasto = new VehiculoGasto
        {
            VehiculoId = datos.VehiculoId,
            Concepto = concepto,
            Monto = Math.Round(datos.Monto, 2, MidpointRounding.AwayFromZero),
            Fecha = datos.Fecha
        };

        using var conexion = await _factory.AbrirAsync(ct);
        using var transaccion = await conexion.BeginTransactionAsync(ct);
        try
        {
            var id = await _gastos.InsertarAsync(gasto, conexion, transaccion, ct);
            var total = await _gastos.SumarAsync(datos.VehiculoId, conexion, transaccion, ct);
            await _vehiculos.FijarGastosImportacionAsync(datos.VehiculoId, total, conexion, transaccion, ct);
            await _auditoria.RegistrarEnTransaccionAsync(AccionAuditoria.Crear, DbNames.VehiculoGasto, id,
                $"Gasto de {vehiculo.Codigo}: {concepto} {gasto.Monto:N2} DOP (total importación {total:N2})",
                conexion, transaccion, ct);

            await transaccion.CommitAsync(ct);
            Log.Information("Gasto {Id} agregado al vehículo {Vehiculo}", id, vehiculo.Codigo);
            return id;
        }
        catch
        {
            await transaccion.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task EliminarAsync(long gastoId, CancellationToken ct = default)
    {
        ExigirEscritura();
        var vehiculoId = await _gastos.ObtenerVehiculoIdAsync(gastoId, ct)
            ?? throw new InvalidOperationException("El gasto no existe.");

        using var conexion = await _factory.AbrirAsync(ct);
        using var transaccion = await conexion.BeginTransactionAsync(ct);
        try
        {
            await _gastos.EliminarAsync(gastoId, conexion, transaccion, ct);
            var total = await _gastos.SumarAsync(vehiculoId, conexion, transaccion, ct);
            await _vehiculos.FijarGastosImportacionAsync(vehiculoId, total, conexion, transaccion, ct);
            await _auditoria.RegistrarEnTransaccionAsync(AccionAuditoria.Eliminar, DbNames.VehiculoGasto, gastoId,
                $"Gasto eliminado (total importación recalculado a {total:N2} DOP)",
                conexion, transaccion, ct);

            await transaccion.CommitAsync(ct);
            Log.Information("Gasto {Id} eliminado del vehículo {Vehiculo}", gastoId, vehiculoId);
        }
        catch
        {
            await transaccion.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static void ExigirLectura()
    {
        if (!SesionActual.TienePermiso(Permisos.Gastos))
            throw new UnauthorizedAccessException("No tienes permiso para ver los gastos de vehículos.");
    }

    private static void ExigirEscritura()
    {
        if (!SesionActual.TienePermiso(Permisos.Gastos))
            throw new UnauthorizedAccessException("No tienes permiso para gestionar gastos de vehículos.");
    }
}
