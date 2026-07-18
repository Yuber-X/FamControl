using MySqlConnector;
using FAControl.Common;
using FAControl.Models;

namespace FAControl.Data;

/// <summary>
/// Acceso a la tabla vehiculo (inventario del dealer). Soft delete: eliminar
/// marca deleted_at y toda lectura de lista filtra deleted_at IS NULL.
/// La creación reserva el código V-0001 con un contador atómico, así que va
/// dentro de una transacción que orquesta el Service.
/// </summary>
public class VehiculoRepository
{
    private readonly ConexionFactory _factory;

    public VehiculoRepository(ConexionFactory factory) => _factory = factory;

    // ============================================================
    // Escrituras
    // ============================================================

    /// <summary>Inserta el vehículo (dentro de la transacción del Service, que ya reservó el código).</summary>
    public async Task<long> InsertarAsync(Vehiculo vehiculo, MySqlConnection conexion,
        MySqlTransaction transaccion, CancellationToken ct = default)
    {
        using var cmd = conexion.CreateCommand();
        cmd.Transaction = transaccion;
        cmd.CommandText = $"""
            INSERT INTO {DbNames.Vehiculo}
              (codigo, vin, marca, modelo, anio, color, placa, tipo, kilometraje,
               costo_adquisicion, gastos_importacion, precio_venta, estado, notas)
            VALUES
              (@codigo, @vin, @marca, @modelo, @anio, @color, @placa, @tipo, @kilometraje,
               @costoAdquisicion, @gastosImportacion, @precioVenta, @estado, @notas);
            SELECT LAST_INSERT_ID();
            """;
        cmd.Parameters.AddWithValue("@codigo", vehiculo.Codigo);
        AgregarParametrosDatos(cmd, vehiculo);
        cmd.Parameters.AddWithValue("@estado", EnumMap.ADb(vehiculo.Estado));
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task ActualizarAsync(long id, VehiculoDatos datos, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {DbNames.Vehiculo}
            SET vin = @vin, marca = @marca, modelo = @modelo, anio = @anio, color = @color,
                placa = @placa, tipo = @tipo, kilometraje = @kilometraje,
                costo_adquisicion = @costoAdquisicion, gastos_importacion = @gastosImportacion,
                precio_venta = @precioVenta, notas = @notas, updated_at = UTC_TIMESTAMP()
            WHERE id = @id AND deleted_at IS NULL;
            """;
        AgregarParametrosDatos(cmd, datos);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Cambia solo el estado (reservar, vender, dar de baja). El estado no se toca desde el formulario.</summary>
    public async Task CambiarEstadoAsync(long id, EstadoVehiculo estado, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {DbNames.Vehiculo}
            SET estado = @estado, updated_at = UTC_TIMESTAMP()
            WHERE id = @id AND deleted_at IS NULL;
            """;
        cmd.Parameters.AddWithValue("@estado", EnumMap.ADb(estado));
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Variante transaccional: cambia el estado dentro de una operación multi-paso (venta/alquiler).</summary>
    public async Task CambiarEstadoAsync(long id, EstadoVehiculo estado, MySqlConnection conexion,
        MySqlTransaction transaccion, CancellationToken ct = default)
    {
        using var cmd = conexion.CreateCommand();
        cmd.Transaction = transaccion;
        cmd.CommandText = $"""
            UPDATE {DbNames.Vehiculo}
            SET estado = @estado, updated_at = UTC_TIMESTAMP()
            WHERE id = @id AND deleted_at IS NULL;
            """;
        cmd.Parameters.AddWithValue("@estado", EnumMap.ADb(estado));
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Actualiza gastos_importacion = suma del ledger de gastos (dentro de una transacción).</summary>
    public async Task FijarGastosImportacionAsync(long id, decimal total, MySqlConnection conexion,
        MySqlTransaction transaccion, CancellationToken ct = default)
    {
        using var cmd = conexion.CreateCommand();
        cmd.Transaction = transaccion;
        cmd.CommandText = $"""
            UPDATE {DbNames.Vehiculo}
            SET gastos_importacion = @total, updated_at = UTC_TIMESTAMP()
            WHERE id = @id AND deleted_at IS NULL;
            """;
        cmd.Parameters.AddWithValue("@total", total);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Soft delete (nunca DELETE físico — AutoControl puede referenciarlo por FK).</summary>
    public async Task EliminarAsync(long id, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {DbNames.Vehiculo}
            SET deleted_at = UTC_TIMESTAMP()
            WHERE id = @id AND deleted_at IS NULL;
            """;
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>True si otro vehículo activo ya usa ese VIN (unicidad amigable antes de guardar).</summary>
    public async Task<bool> ExisteVinAsync(string vin, long? excluirId = null, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT COUNT(*) FROM {DbNames.Vehiculo}
            WHERE vin = @vin AND deleted_at IS NULL
              AND (@excluirId IS NULL OR id <> @excluirId);
            """;
        cmd.Parameters.AddWithValue("@vin", vin);
        cmd.Parameters.AddWithValue("@excluirId", (object?)excluirId ?? DBNull.Value);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct)) > 0;
    }

    // ============================================================
    // Lecturas
    // ============================================================

    /// <summary>Lista del inventario para la pantalla Vehículos.</summary>
    public async Task<IReadOnlyList<VehiculoResumen>> ObtenerResumenesAsync(CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT id, codigo, marca, modelo, anio, tipo, placa,
                   (costo_adquisicion + gastos_importacion) AS costo_total,
                   precio_venta, estado
            FROM {DbNames.Vehiculo}
            WHERE deleted_at IS NULL
            ORDER BY codigo;
            """;

        var lista = new List<VehiculoResumen>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var anioOrd = reader.GetOrdinal("anio");
            var placaOrd = reader.GetOrdinal("placa");
            int? anio = reader.IsDBNull(anioOrd) ? null : reader.GetInt32(anioOrd);
            var marca = reader.GetString("marca");
            var modelo = reader.GetString("modelo");
            var descripcion = $"{marca} {modelo}{(anio is { } a ? $" {a}" : string.Empty)}".Trim();
            lista.Add(new VehiculoResumen(
                reader.GetInt64("id"),
                reader.GetString("codigo"),
                descripcion,
                EnumMap.TipoVehiculoDeDb(reader.GetString("tipo")),
                anio,
                reader.IsDBNull(placaOrd) ? null : reader.GetString("placa"),
                reader.GetDecimal("costo_total"),
                reader.GetDecimal("precio_venta"),
                EnumMap.EstadoVehiculoDeDb(reader.GetString("estado"))));
        }
        return lista;
    }

    public async Task<Vehiculo?> ObtenerPorIdAsync(long id, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT id, codigo, vin, marca, modelo, anio, color, placa, tipo, kilometraje,
                   costo_adquisicion, gastos_importacion, precio_venta, estado, notas,
                   created_at, updated_at
            FROM {DbNames.Vehiculo}
            WHERE id = @id AND deleted_at IS NULL;
            """;
        cmd.Parameters.AddWithValue("@id", id);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Mapear(reader) : null;
    }

    /// <summary>Métricas del inventario (panel del dealer).</summary>
    public async Task<InventarioMetricas> ObtenerMetricasAsync(CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT COUNT(*) AS total,
                   COALESCE(SUM(estado = 'disponible'), 0) AS disponibles,
                   COALESCE(SUM(CASE WHEN estado NOT IN ('vendido','baja')
                                     THEN costo_adquisicion + gastos_importacion END), 0) AS capital_invertido,
                   COALESCE(SUM(CASE WHEN estado NOT IN ('vendido','baja')
                                     THEN precio_venta END), 0) AS valor_inventario
            FROM {DbNames.Vehiculo}
            WHERE deleted_at IS NULL;
            """;

        using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return new InventarioMetricas(
            Convert.ToInt32(reader["total"]),
            Convert.ToInt32(reader["disponibles"]),
            reader.GetDecimal("capital_invertido"),
            reader.GetDecimal("valor_inventario"));
    }

    private static void AgregarParametrosDatos(MySqlCommand cmd, VehiculoDatos d)
    {
        cmd.Parameters.AddWithValue("@vin", (object?)d.Vin ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@marca", d.Marca);
        cmd.Parameters.AddWithValue("@modelo", d.Modelo);
        cmd.Parameters.AddWithValue("@anio", (object?)d.Anio ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@color", (object?)d.Color ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@placa", (object?)d.Placa ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@tipo", EnumMap.ADb(d.Tipo));
        cmd.Parameters.AddWithValue("@kilometraje", (object?)d.Kilometraje ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@costoAdquisicion", d.CostoAdquisicion);
        cmd.Parameters.AddWithValue("@gastosImportacion", d.GastosImportacion);
        cmd.Parameters.AddWithValue("@precioVenta", d.PrecioVenta);
        cmd.Parameters.AddWithValue("@notas", (object?)d.Notas ?? DBNull.Value);
    }

    // Sobrecarga para InsertarAsync, que recibe la entidad ya con estado.
    private static void AgregarParametrosDatos(MySqlCommand cmd, Vehiculo v)
    {
        cmd.Parameters.AddWithValue("@vin", (object?)v.Vin ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@marca", v.Marca);
        cmd.Parameters.AddWithValue("@modelo", v.Modelo);
        cmd.Parameters.AddWithValue("@anio", (object?)v.Anio ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@color", (object?)v.Color ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@placa", (object?)v.Placa ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@tipo", EnumMap.ADb(v.Tipo));
        cmd.Parameters.AddWithValue("@kilometraje", (object?)v.Kilometraje ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@costoAdquisicion", v.CostoAdquisicion);
        cmd.Parameters.AddWithValue("@gastosImportacion", v.GastosImportacion);
        cmd.Parameters.AddWithValue("@precioVenta", v.PrecioVenta);
        cmd.Parameters.AddWithValue("@notas", (object?)v.Notas ?? DBNull.Value);
    }

    private static Vehiculo Mapear(MySqlDataReader reader) => new()
    {
        Id = reader.GetInt64("id"),
        Codigo = reader.GetString("codigo"),
        Vin = reader.IsDBNull(reader.GetOrdinal("vin")) ? null : reader.GetString("vin"),
        Marca = reader.GetString("marca"),
        Modelo = reader.GetString("modelo"),
        Anio = reader.IsDBNull(reader.GetOrdinal("anio")) ? null : reader.GetInt32(reader.GetOrdinal("anio")),
        Color = reader.IsDBNull(reader.GetOrdinal("color")) ? null : reader.GetString("color"),
        Placa = reader.IsDBNull(reader.GetOrdinal("placa")) ? null : reader.GetString("placa"),
        Tipo = EnumMap.TipoVehiculoDeDb(reader.GetString("tipo")),
        Kilometraje = reader.IsDBNull(reader.GetOrdinal("kilometraje")) ? null : reader.GetInt32(reader.GetOrdinal("kilometraje")),
        CostoAdquisicion = reader.GetDecimal("costo_adquisicion"),
        GastosImportacion = reader.GetDecimal("gastos_importacion"),
        PrecioVenta = reader.GetDecimal("precio_venta"),
        Estado = EnumMap.EstadoVehiculoDeDb(reader.GetString("estado")),
        Notas = reader.IsDBNull(reader.GetOrdinal("notas")) ? null : reader.GetString("notas"),
        CreatedAtUtc = DateTime.SpecifyKind(reader.GetDateTime("created_at"), DateTimeKind.Utc),
        UpdatedAtUtc = reader.IsDBNull(reader.GetOrdinal("updated_at"))
            ? null
            : DateTime.SpecifyKind(reader.GetDateTime("updated_at"), DateTimeKind.Utc)
    };
}
