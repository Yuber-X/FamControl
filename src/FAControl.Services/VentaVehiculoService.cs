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
    private readonly VentaPlazoRepository _plazos;

    public VentaVehiculoService(VentaVehiculoRepository ventas, VehiculoRepository vehiculos,
        ClienteRepository clientes, ContadorRepository contador, ConexionFactory factory,
        AuditoriaService auditoria, VentaPlazoRepository plazos)
    {
        _ventas = ventas;
        _vehiculos = vehiculos;
        _clientes = clientes;
        _contador = contador;
        _factory = factory;
        _auditoria = auditoria;
        _plazos = plazos;
    }

    public Task<IReadOnlyList<VentaResumen>> ObtenerResumenesAsync(CancellationToken ct = default)
    {
        ExigirLectura();
        return _ventas.ObtenerResumenesAsync(ct);
    }

    /// <summary>Datos completos de la venta para su factura (pedido 2026-07-25).</summary>
    public async Task<FacturaVentaDatos> ObtenerFacturaAsync(long ventaId, CancellationToken ct = default)
    {
        ExigirLectura();
        return await _ventas.ObtenerFacturaAsync(ventaId, ct)
            ?? throw new InvalidOperationException("La venta no existe.");
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

        // Financiamiento del dealer (016): el plan se calcula ANTES de abrir la
        // transacción, así una inicial o un plazo inválido no deja nada a medias.
        List<VentaPlazo> plazos = [];
        var inicial = 0m;
        DateOnly? fechaLimite = null;

        switch (datos.TipoVenta)
        {
            case TipoVenta.Plazos:
                var plan = datos.Plan
                    ?? throw new ArgumentException("Una venta por plazos necesita su plan de pagos.");
                plazos = VentaPlazoService.CalcularPlazos(precio, plan);
                inicial = Math.Round(plan.Inicial, 2, MidpointRounding.AwayFromZero);
                break;

            case TipoVenta.Separacion:
                // Reserva: el cliente aparta el vehículo y tiene N días de derecho
                if (datos.DiasSeparacion < 1)
                    throw new ArgumentException("Los días de separación deben ser al menos 1.");
                inicial = Math.Round(datos.AdelantoSeparacion, 2, MidpointRounding.AwayFromZero);
                if (inicial <= 0m)
                    throw new ArgumentException("Una separación necesita el adelanto que dejó el cliente.");
                if (inicial > precio)
                    throw new ArgumentException("El adelanto no puede ser mayor que el precio de venta.");
                fechaLimite = FechaNegocio.Hoy.AddDays(datos.DiasSeparacion);
                break;
        }

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
                TipoVenta = datos.TipoVenta,
                Inicial = inicial,
                FechaLimite = fechaLimite,
                MetodoPago = datos.MetodoPago,
                Notas = string.IsNullOrWhiteSpace(datos.Notas) ? null : datos.Notas.Trim()
            };

            var id = await _ventas.InsertarAsync(venta, conexion, transaccion, ct);
            if (plazos.Count > 0)
                await _plazos.InsertarPlazosAsync(id, plazos, conexion, transaccion, ct);

            // Una separación RESERVA el vehículo (sigue siendo del dealer hasta
            // completar el pago); contado y plazos lo dan por vendido.
            var estadoVehiculo = datos.TipoVenta == TipoVenta.Separacion
                ? EstadoVehiculo.Reservado
                : EstadoVehiculo.Vendido;
            await _vehiculos.CambiarEstadoAsync(datos.VehiculoId, estadoVehiculo, conexion, transaccion, ct);

            var detalle = datos.TipoVenta switch
            {
                TipoVenta.Plazos => $" — financiada: inicial {inicial:N2} + {plazos.Count} plazo(s)",
                TipoVenta.Separacion => $" — separación: adelanto {inicial:N2}, vence {fechaLimite:dd/MM/yyyy}",
                _ => string.Empty
            };
            await _auditoria.RegistrarEnTransaccionAsync(AccionAuditoria.Crear, DbNames.VentaVehiculo, id,
                $"Venta {codigo}: {vehiculo.Descripcion} a {cliente.NombreCompleto} por {precio:N2} DOP{detalle}",
                conexion, transaccion, ct);

            await transaccion.CommitAsync(ct);
            Log.Information("Venta {Codigo} (id {Id}, {Tipo}): vehículo {Vehiculo} a cliente {Cliente}",
                codigo, id, datos.TipoVenta, vehiculo.Codigo, cliente.Id);
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
        if (!SesionActual.TienePermiso(Permisos.Ventas))
            throw new UnauthorizedAccessException("No tienes permiso para ver las ventas de vehículos.");
    }

    /// <summary>Cómo quedó una venta cancelada (028). Null si sigue activa.</summary>
    public Task<(string Motivo, decimal Porcentaje, decimal Retenido, decimal Devuelto)?>
        ObtenerCancelacionAsync(long ventaId, CancellationToken ct = default) =>
        _ventas.ObtenerCancelacionAsync(ventaId, ct);

    private static void ExigirEscritura()
    {
        if (!SesionActual.TienePermiso(Permisos.Ventas))
            throw new UnauthorizedAccessException("No tienes permiso para registrar ventas de vehículos.");
    }
}
