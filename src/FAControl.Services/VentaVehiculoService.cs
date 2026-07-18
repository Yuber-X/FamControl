using FAControl.Common;
using FAControl.Data;
using FAControl.Models;
using Serilog;

namespace FAControl.Services;

/// <summary>
/// Venta al contado de vehículos (DealerControl). Registrar una venta es atómico:
/// reserva el código VC-0001, inserta la venta, marca el vehículo 'vendido' y
/// audita — todo en una transacción. Solo vehículos disponibles/reservados se venden.
/// </summary>
public class VentaVehiculoService
{
    private readonly VentaVehiculoRepository _ventas;
    private readonly VehiculoRepository _vehiculos;
    private readonly ClienteRepository _clientes;
    private readonly ContadorRepository _contador;
    private readonly ConexionFactory _factory;
    private readonly AuditoriaService _auditoria;

    public VentaVehiculoService(VentaVehiculoRepository ventas, VehiculoRepository vehiculos,
        ClienteRepository clientes, ContadorRepository contador, ConexionFactory factory,
        AuditoriaService auditoria)
    {
        _ventas = ventas;
        _vehiculos = vehiculos;
        _clientes = clientes;
        _contador = contador;
        _factory = factory;
        _auditoria = auditoria;
    }

    public Task<IReadOnlyList<VentaResumen>> ObtenerResumenesAsync(CancellationToken ct = default)
    {
        ExigirLectura();
        return _ventas.ObtenerResumenesAsync(ct);
    }

    /// <summary>Registra la venta al contado. Devuelve id y código VC-0001.</summary>
    public async Task<(long Id, string Codigo)> RegistrarAsync(VentaVehiculoDatos datos, CancellationToken ct = default)
    {
        ExigirEscritura();

        var vehiculo = await _vehiculos.ObtenerPorIdAsync(datos.VehiculoId, ct)
            ?? throw new InvalidOperationException("El vehículo no existe o fue eliminado.");
        if (vehiculo.Estado is EstadoVehiculo.Vendido)
            throw new InvalidOperationException($"El vehículo {vehiculo.Codigo} ya está vendido.");
        if (vehiculo.Estado is EstadoVehiculo.Alquilado)
            throw new InvalidOperationException($"El vehículo {vehiculo.Codigo} está alquilado; no se puede vender.");

        var cliente = await _clientes.ObtenerPorIdAsync(datos.ClienteId, ct)
            ?? throw new InvalidOperationException("El cliente no existe o fue eliminado.");

        if (datos.Precio <= 0m)
            throw new ArgumentException("El precio de venta debe ser mayor que cero.");
        var precio = Math.Round(datos.Precio, 2, MidpointRounding.AwayFromZero);

        using var conexion = await _factory.AbrirAsync(ct);
        using var transaccion = await conexion.BeginTransactionAsync(ct);
        try
        {
            var numero = await _contador.SiguienteAsync(ContadorRepository.Venta, conexion, transaccion, ct);
            var codigo = $"VC-{numero:D4}";

            var venta = new VentaVehiculo
            {
                Codigo = codigo,
                VehiculoId = datos.VehiculoId,
                ClienteId = datos.ClienteId,
                Precio = precio,
                MetodoPago = datos.MetodoPago,
                Notas = string.IsNullOrWhiteSpace(datos.Notas) ? null : datos.Notas.Trim()
            };

            var id = await _ventas.InsertarAsync(venta, conexion, transaccion, ct);
            await _vehiculos.CambiarEstadoAsync(datos.VehiculoId, EstadoVehiculo.Vendido, conexion, transaccion, ct);
            await _auditoria.RegistrarEnTransaccionAsync(AccionAuditoria.Crear, DbNames.VentaVehiculo, id,
                $"Venta {codigo}: {vehiculo.Descripcion} a {cliente.NombreCompleto} por {precio:N2} DOP",
                conexion, transaccion, ct);

            await transaccion.CommitAsync(ct);
            Log.Information("Venta {Codigo} (id {Id}): vehículo {Vehiculo} vendido a cliente {Cliente}",
                codigo, id, vehiculo.Codigo, cliente.Id);
            return (id, codigo);
        }
        catch
        {
            await transaccion.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static void ExigirLectura()
    {
        if (!SesionActual.TienePermiso(Permisos.Vehiculos))
            throw new UnauthorizedAccessException("No tenés permiso para ver las ventas de vehículos.");
    }

    private static void ExigirEscritura()
    {
        if (!SesionActual.TienePermiso(Permisos.VehiculosEditar))
            throw new UnauthorizedAccessException("No tenés permiso para registrar ventas de vehículos.");
    }
}
