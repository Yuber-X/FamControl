using FAControl.Common;
using FAControl.Data;
using FAControl.Models;
using Serilog;

namespace FAControl.Services;

/// <summary>
/// Rent a car (DealerControl). Alquilar es atómico: código AL-0001 + alquiler +
/// marcar el vehículo 'alquilado' + auditoría. La devolución cierra el alquiler
/// y libera el vehículo ('disponible'). Días y monto se calculan en el Service.
/// </summary>
public class AlquilerService
{
    private readonly AlquilerRepository _alquileres;
    private readonly VehiculoRepository _vehiculos;
    private readonly ClienteRepository _clientes;
    private readonly ContadorRepository _contador;
    private readonly ConexionFactory _factory;
    private readonly AuditoriaService _auditoria;

    public AlquilerService(AlquilerRepository alquileres, VehiculoRepository vehiculos,
        ClienteRepository clientes, ContadorRepository contador, ConexionFactory factory,
        AuditoriaService auditoria)
    {
        _alquileres = alquileres;
        _vehiculos = vehiculos;
        _clientes = clientes;
        _contador = contador;
        _factory = factory;
        _auditoria = auditoria;
    }

    public Task<IReadOnlyList<AlquilerResumen>> ObtenerResumenesAsync(CancellationToken ct = default)
    {
        ExigirLectura();
        return _alquileres.ObtenerResumenesAsync(ct);
    }

    /// <summary>Días facturables entre inicio y fin (mínimo 1 día).</summary>
    public static int CalcularDias(DateOnly inicio, DateOnly fin) =>
        Math.Max(1, fin.DayNumber - inicio.DayNumber);

    /// <summary>Registra un alquiler. Devuelve id y código AL-0001.</summary>
    public async Task<(long Id, string Codigo)> RegistrarAsync(AlquilerDatos datos, CancellationToken ct = default)
    {
        ExigirEscritura();

        var vehiculo = await _vehiculos.ObtenerPorIdAsync(datos.VehiculoId, ct)
            ?? throw new InvalidOperationException("El vehículo no existe o fue eliminado.");
        if (vehiculo.Estado != EstadoVehiculo.Disponible)
            throw new InvalidOperationException(
                $"El vehículo {vehiculo.Codigo} no está disponible para alquilar (estado actual: {vehiculo.Estado}).");

        var cliente = await _clientes.ObtenerPorIdAsync(datos.ClienteId, ct)
            ?? throw new InvalidOperationException("El cliente no existe o fue eliminado.");

        if (datos.FechaFin < datos.FechaInicio)
            throw new ArgumentException("La fecha de devolución no puede ser anterior al inicio.");
        if (datos.TarifaDia <= 0m)
            throw new ArgumentException("La tarifa por día debe ser mayor que cero.");

        var dias = CalcularDias(datos.FechaInicio, datos.FechaFin);
        var tarifa = Math.Round(datos.TarifaDia, 2, MidpointRounding.AwayFromZero);
        var total = Math.Round(tarifa * dias, 2, MidpointRounding.AwayFromZero);

        using var conexion = await _factory.AbrirAsync(ct);
        using var transaccion = await conexion.BeginTransactionAsync(ct);
        try
        {
            var numero = await _contador.SiguienteAsync(ContadorRepository.Alquiler, conexion, transaccion, ct);
            var codigo = $"AL-{numero:D4}";

            var alquiler = new Alquiler
            {
                Codigo = codigo,
                VehiculoId = datos.VehiculoId,
                ClienteId = datos.ClienteId,
                FechaInicio = datos.FechaInicio,
                FechaFin = datos.FechaFin,
                TarifaDia = tarifa,
                Dias = dias,
                MontoTotal = total,
                Estado = EstadoAlquiler.Activo,
                Notas = string.IsNullOrWhiteSpace(datos.Notas) ? null : datos.Notas.Trim()
            };

            var id = await _alquileres.InsertarAsync(alquiler, conexion, transaccion, ct);
            await _vehiculos.CambiarEstadoAsync(datos.VehiculoId, EstadoVehiculo.Alquilado, conexion, transaccion, ct);
            await _auditoria.RegistrarEnTransaccionAsync(AccionAuditoria.Crear, DbNames.Alquiler, id,
                $"Alquiler {codigo}: {vehiculo.Descripcion} a {cliente.NombreCompleto}, " +
                $"{dias} día(s) × {tarifa:N2} = {total:N2} DOP", conexion, transaccion, ct);

            await transaccion.CommitAsync(ct);
            Log.Information("Alquiler {Codigo} (id {Id}) para cliente {Cliente}", codigo, id, cliente.Id);
            return (id, codigo);
        }
        catch
        {
            await transaccion.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    /// <summary>Cierra un alquiler activo: finalizado (devuelto) o cancelado, y libera el vehículo.</summary>
    public async Task CerrarAsync(long alquilerId, bool cancelado, CancellationToken ct = default)
    {
        ExigirEscritura();

        var alquiler = await _alquileres.ObtenerPorIdAsync(alquilerId, ct)
            ?? throw new InvalidOperationException("El alquiler no existe.");
        if (alquiler.Estado != EstadoAlquiler.Activo)
            throw new InvalidOperationException("Este alquiler ya está cerrado.");

        var nuevoEstado = cancelado ? EstadoAlquiler.Cancelado : EstadoAlquiler.Finalizado;
        var fechaDevolucion = cancelado ? (DateOnly?)null : FechaNegocio.Hoy;

        using var conexion = await _factory.AbrirAsync(ct);
        using var transaccion = await conexion.BeginTransactionAsync(ct);
        try
        {
            await _alquileres.CerrarAsync(alquilerId, nuevoEstado, fechaDevolucion, conexion, transaccion, ct);
            // El vehículo vuelve al inventario disponible.
            await _vehiculos.CambiarEstadoAsync(alquiler.VehiculoId, EstadoVehiculo.Disponible, conexion, transaccion, ct);
            await _auditoria.RegistrarEnTransaccionAsync(AccionAuditoria.Modificar, DbNames.Alquiler, alquilerId,
                $"Alquiler {alquiler.Codigo} {(cancelado ? "cancelado" : "finalizado (vehículo devuelto)")}",
                conexion, transaccion, ct);

            await transaccion.CommitAsync(ct);
            Log.Information("Alquiler {Id} cerrado ({Estado})", alquilerId, nuevoEstado);
        }
        catch
        {
            await transaccion.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static void ExigirLectura()
    {
        if (!SesionActual.TienePermiso(Permisos.Alquileres))
            throw new UnauthorizedAccessException("No tenés permiso para ver los alquileres.");
    }

    private static void ExigirEscritura()
    {
        if (!SesionActual.TienePermiso(Permisos.Alquileres))
            throw new UnauthorizedAccessException("No tenés permiso para gestionar alquileres.");
    }
}
