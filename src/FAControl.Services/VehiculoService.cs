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
    private readonly VehiculoReparacionRepository _reparaciones;
    private readonly VentaVehiculoRepository _ventas;
    private readonly VehiculoGastoRepository _gastos;
    private readonly PrestamoRepository _prestamos;

    public VehiculoService(VehiculoRepository vehiculos, ContadorRepository contador,
        ConexionFactory factory, AuditoriaService auditoria,
        VehiculoReparacionRepository reparaciones, VentaVehiculoRepository ventas,
        PrestamoRepository prestamos, VehiculoGastoRepository gastos)
    {
        _vehiculos = vehiculos;
        _gastos = gastos;
        _contador = contador;
        _factory = factory;
        _auditoria = auditoria;
        _reparaciones = reparaciones;
        _ventas = ventas;
        _prestamos = prestamos;
    }

    // ---------- Ficha completa (pedido 2026-07-25) ----------

    /// <summary>
    /// Ficha del vehículo: datos completos + comprador (venta al contado o
    /// crédito de AutoControl) + historial de reparaciones.
    /// </summary>
    public async Task<FichaVehiculo> ObtenerFichaAsync(long id, CancellationToken ct = default)
    {
        ExigirLectura();
        var vehiculo = await _vehiculos.ObtenerPorIdAsync(id, ct)
            ?? throw new InvalidOperationException("El vehículo no existe o fue eliminado.");
        var venta = await _ventas.ObtenerDeVehiculoAsync(id, ct);
        var credito = await _prestamos.ObtenerCreditoDeVehiculoAsync(id, ct);
        var reparaciones = await _reparaciones.ObtenerDeVehiculoAsync(id, ct);
        return new FichaVehiculo(vehiculo, venta, credito?.Codigo, credito?.ClienteNombre, reparaciones);
    }

    /// <summary>Registra una reparación/mantenimiento del vehículo (con auditoría).</summary>
    public async Task AgregarReparacionAsync(long vehiculoId, DateOnly fecha, string detalle,
        decimal costo, CancellationToken ct = default)
    {
        ExigirEscritura();
        if (string.IsNullOrWhiteSpace(detalle))
            throw new ArgumentException("Describe la reparación (qué se hizo).");
        if (costo < 0m)
            throw new ArgumentException("El costo no puede ser negativo.");

        var reparacion = new VehiculoReparacion
        {
            VehiculoId = vehiculoId,
            Fecha = fecha,
            Detalle = detalle.Trim(),
            Costo = costo
        };
        var id = await _reparaciones.InsertarAsync(reparacion, SesionActual.Id, ct);
        await _auditoria.RegistrarAsync(AccionAuditoria.Crear, DbNames.VehiculoReparacion, id,
            $"Reparación del vehículo #{vehiculoId}: {reparacion.Detalle} — {costo:N2} DOP ({fecha:dd/MM/yyyy})", ct);
        Log.Information("Reparación registrada al vehículo {VehiculoId}: {Detalle}", vehiculoId, reparacion.Detalle);
    }

    /// <summary>Elimina (soft) una reparación registrada por error.</summary>
    public async Task EliminarReparacionAsync(long reparacionId, CancellationToken ct = default)
    {
        ExigirEscritura();
        await _reparaciones.EliminarAsync(reparacionId, ct);
        await _auditoria.RegistrarAsync(AccionAuditoria.Eliminar, DbNames.VehiculoReparacion,
            reparacionId, "Reparación eliminada", ct);
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
                // La matrícula se OLVIDABA aquí (bug reportado 2026-07-31: "al
                // crear no muestra lo digitado en matrícula, pero al editar sí").
                // El formulario la pedía, el repositorio la guardaba y la
                // consulta la traía; el único punto donde se perdía era este,
                // al armar la entidad del alta.
                Matricula = normalizados.Matricula,
                Tipo = normalizados.Tipo,
                Kilometraje = normalizados.Kilometraje,
                CostoAdquisicion = normalizados.CostoAdquisicion,
                GastosImportacion = normalizados.GastosImportacion,
                PrecioVenta = normalizados.PrecioVenta,
                Estado = EstadoVehiculo.Disponible,
                Notas = normalizados.Notas
            };

            var id = await _vehiculos.InsertarAsync(vehiculo, conexion, transaccion, ct);

            // Los gastos que se escriben en el alta van también al LIBRO de gastos
            // (pedido de Yuber 2026-07-31: los cargaba en el vehículo y no los veía
            // en Importación/gastos). Antes el total vivía suelto en la ficha y el
            // libro quedaba vacío: dos fuentes de verdad para el mismo número.
            if (vehiculo.GastosImportacion > 0m)
            {
                await _gastos.InsertarAsync(new VehiculoGasto
                {
                    VehiculoId = id,
                    Concepto = "Gastos de importación (cargados al registrar el vehículo)",
                    Monto = vehiculo.GastosImportacion,
                    Fecha = FechaNegocio.Hoy
                }, conexion, transaccion, ct);
            }

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
            // La matrícula también se limpia: es un número de documento y un
            // espacio al final la hace distinta de sí misma al buscarla.
            Matricula = Limpiar(datos.Matricula),
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
        if (!SesionActual.TienePermiso(Permisos.Inventario))
            throw new UnauthorizedAccessException("No tienes permiso para ver el inventario de vehículos.");
    }

    private static void ExigirEscritura()
    {
        if (!SesionActual.TienePermiso(Permisos.InventarioEditar))
            throw new UnauthorizedAccessException("No tienes permiso para modificar el inventario de vehículos.");
    }

    private static string? Limpiar(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}
