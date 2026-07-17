using FAControl.Common;
using FAControl.Data;
using FAControl.Models;
using Serilog;

namespace FAControl.Services;

/// <summary>
/// Lógica de negocio del inventario de vehículos (DealerControl). Valida datos,
/// asegura unicidad de VIN, reserva el código V-0001 de forma atómica y audita
/// toda mutación. La creación va en una transacción (código + insert + auditoría).
/// La escritura exige el permiso 'vehiculos_editar'; la lectura, 'vehiculos'.
/// </summary>
public class VehiculoService
{
    private readonly VehiculoRepository _vehiculos;
    private readonly ContadorRepository _contador;
    private readonly ConexionFactory _factory;
    private readonly AuditoriaService _auditoria;

    public VehiculoService(VehiculoRepository vehiculos, ContadorRepository contador,
        ConexionFactory factory, AuditoriaService auditoria)
    {
        _vehiculos = vehiculos;
        _contador = contador;
        _factory = factory;
        _auditoria = auditoria;
    }

    // ---------- Lecturas ----------

    public Task<IReadOnlyList<VehiculoResumen>> ObtenerResumenesAsync(CancellationToken ct = default)
    {
        ExigirLectura();
        return _vehiculos.ObtenerResumenesAsync(ct);
    }

    public Task<Vehiculo?> ObtenerPorIdAsync(long id, CancellationToken ct = default)
    {
        ExigirLectura();
        return _vehiculos.ObtenerPorIdAsync(id, ct);
    }

    public Task<InventarioMetricas> ObtenerMetricasAsync(CancellationToken ct = default)
    {
        ExigirLectura();
        return _vehiculos.ObtenerMetricasAsync(ct);
    }

    // ---------- Mutaciones (con auditoría) ----------

    /// <summary>Crea un vehículo con código V-0001 atómico. Devuelve id y código.</summary>
    public async Task<(long Id, string Codigo)> CrearAsync(VehiculoDatos datos, CancellationToken ct = default)
    {
        ExigirEscritura();
        var normalizados = await ValidarAsync(datos, excluirId: null, ct);

        using var conexion = await _factory.AbrirAsync(ct);
        using var transaccion = await conexion.BeginTransactionAsync(ct);
        try
        {
            var numero = await _contador.SiguienteAsync(ContadorRepository.Vehiculo, conexion, transaccion, ct);
            var codigo = $"V-{numero:D4}";

            var vehiculo = new Vehiculo
            {
                Codigo = codigo,
                Vin = normalizados.Vin,
                Marca = normalizados.Marca,
                Modelo = normalizados.Modelo,
                Anio = normalizados.Anio,
                Color = normalizados.Color,
                Placa = normalizados.Placa,
                Tipo = normalizados.Tipo,
                Kilometraje = normalizados.Kilometraje,
                CostoAdquisicion = normalizados.CostoAdquisicion,
                GastosImportacion = normalizados.GastosImportacion,
                PrecioVenta = normalizados.PrecioVenta,
                Estado = EstadoVehiculo.Disponible,
                Notas = normalizados.Notas
            };

            var id = await _vehiculos.InsertarAsync(vehiculo, conexion, transaccion, ct);
            await _auditoria.RegistrarEnTransaccionAsync(AccionAuditoria.Crear, DbNames.Vehiculo, id,
                $"Vehículo {codigo}: {vehiculo.Descripcion} — costo total {vehiculo.CostoTotal:N2} DOP, " +
                $"precio {vehiculo.PrecioVenta:N2} DOP", conexion, transaccion, ct);

            await transaccion.CommitAsync(ct);
            Log.Information("Vehículo {Codigo} creado (id {Id}): {Descripcion}", codigo, id, vehiculo.Descripcion);
            return (id, codigo);
        }
        catch
        {
            await transaccion.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task ActualizarAsync(long id, VehiculoDatos datos, CancellationToken ct = default)
    {
        ExigirEscritura();
        var existente = await _vehiculos.ObtenerPorIdAsync(id, ct)
            ?? throw new InvalidOperationException("El vehículo no existe o fue eliminado.");

        var normalizados = await ValidarAsync(datos, excluirId: id, ct);
        await _vehiculos.ActualizarAsync(id, normalizados, ct);
        await _auditoria.RegistrarAsync(AccionAuditoria.Modificar, DbNames.Vehiculo, id,
            $"Vehículo {existente.Codigo} actualizado: {normalizados.Marca} {normalizados.Modelo}", ct);
        Log.Information("Vehículo {Id} actualizado", id);
    }

    /// <summary>
    /// Cambia el estado (reservar, dar de baja, liberar). Vender/alquilar lo hacen
    /// los módulos que consumen el vehículo (AutoControl, venta al contado, rent a car).
    /// </summary>
    public async Task CambiarEstadoAsync(long id, EstadoVehiculo estado, CancellationToken ct = default)
    {
        ExigirEscritura();
        var vehiculo = await _vehiculos.ObtenerPorIdAsync(id, ct)
            ?? throw new InvalidOperationException("El vehículo no existe o fue eliminado.");

        await _vehiculos.CambiarEstadoAsync(id, estado, ct);
        await _auditoria.RegistrarAsync(AccionAuditoria.Modificar, DbNames.Vehiculo, id,
            $"Vehículo {vehiculo.Codigo}: estado {EstadoTexto(vehiculo.Estado)} → {EstadoTexto(estado)}", ct);
        Log.Information("Vehículo {Id} cambió de estado a {Estado}", id, estado);
    }

    /// <summary>Soft delete. Bloqueado si el vehículo está vendido o alquilado (lo referencia otra operación).</summary>
    public async Task EliminarAsync(long id, CancellationToken ct = default)
    {
        ExigirEscritura();
        var vehiculo = await _vehiculos.ObtenerPorIdAsync(id, ct)
            ?? throw new InvalidOperationException("El vehículo no existe o ya fue eliminado.");

        if (vehiculo.Estado is EstadoVehiculo.Vendido or EstadoVehiculo.Alquilado)
            throw new InvalidOperationException(
                $"El vehículo {vehiculo.Codigo} está {EstadoTexto(vehiculo.Estado).ToLowerInvariant()} " +
                "y no se puede eliminar. Está referenciado por una venta o alquiler.");

        await _vehiculos.EliminarAsync(id, ct);
        await _auditoria.RegistrarAsync(AccionAuditoria.Eliminar, DbNames.Vehiculo, id,
            $"Vehículo eliminado (soft delete): {vehiculo.Codigo} — {vehiculo.Descripcion}", ct);
        Log.Information("Vehículo {Id} eliminado (soft delete)", id);
    }

    // ---------- Validación ----------

    /// <summary>
    /// Valida y normaliza los datos, y verifica la unicidad de VIN contra la BD.
    /// La parte pura (obligatorios, rangos, redondeo) vive en <see cref="Normalizar"/>.
    /// </summary>
    public async Task<VehiculoDatos> ValidarAsync(VehiculoDatos datos, long? excluirId, CancellationToken ct = default)
    {
        var normalizados = Normalizar(datos);
        if (normalizados.Vin is { } vin && await _vehiculos.ExisteVinAsync(vin, excluirId, ct))
            throw new ArgumentException($"Ya existe un vehículo con el VIN {vin}.");
        return normalizados;
    }

    /// <summary>
    /// Validación y normalización PURAS (sin BD): obligatorios, rango de año,
    /// montos no negativos, VIN ≤ 17 en mayúsculas, y redondeo de dinero.
    /// </summary>
    public static VehiculoDatos Normalizar(VehiculoDatos datos)
    {
        var marca = datos.Marca.Trim();
        var modelo = datos.Modelo.Trim();
        if (marca.Length == 0)
            throw new ArgumentException("La marca es obligatoria.");
        if (modelo.Length == 0)
            throw new ArgumentException("El modelo es obligatorio.");

        if (datos.Anio is { } anio && (anio < 1900 || anio > FechaNegocio.Hoy.Year + 1))
            throw new ArgumentException($"El año {anio} no es válido.");

        if (datos.CostoAdquisicion < 0 || datos.GastosImportacion < 0 || datos.PrecioVenta < 0)
            throw new ArgumentException("Los montos no pueden ser negativos.");

        var vin = Limpiar(datos.Vin)?.ToUpperInvariant();
        if (vin is { Length: > 17 })
            throw new ArgumentException("El VIN no puede superar 17 caracteres.");

        return datos with
        {
            Vin = vin,
            Marca = marca,
            Modelo = modelo,
            Color = Limpiar(datos.Color),
            Placa = Limpiar(datos.Placa)?.ToUpperInvariant(),
            Notas = Limpiar(datos.Notas),
            CostoAdquisicion = Math.Round(datos.CostoAdquisicion, 2, MidpointRounding.AwayFromZero),
            GastosImportacion = Math.Round(datos.GastosImportacion, 2, MidpointRounding.AwayFromZero),
            PrecioVenta = Math.Round(datos.PrecioVenta, 2, MidpointRounding.AwayFromZero)
        };
    }

    /// <summary>Texto en español del estado, para los mensajes de auditoría (Services no ve Textos de la capa UI).</summary>
    private static string EstadoTexto(EstadoVehiculo e) => e switch
    {
        EstadoVehiculo.Disponible => "Disponible",
        EstadoVehiculo.Reservado => "Reservado",
        EstadoVehiculo.Vendido => "Vendido",
        EstadoVehiculo.Alquilado => "Alquilado",
        EstadoVehiculo.Baja => "Baja",
        _ => e.ToString()
    };

    private static void ExigirLectura()
    {
        if (!SesionActual.TienePermiso(Permisos.Vehiculos))
            throw new UnauthorizedAccessException("No tenés permiso para ver el inventario de vehículos.");
    }

    private static void ExigirEscritura()
    {
        if (!SesionActual.TienePermiso(Permisos.VehiculosEditar))
            throw new UnauthorizedAccessException("No tenés permiso para modificar el inventario de vehículos.");
    }

    private static string? Limpiar(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}
