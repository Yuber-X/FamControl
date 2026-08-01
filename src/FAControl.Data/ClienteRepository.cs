using MySqlConnector;
using FAControl.Common;
using FAControl.Models;

namespace FAControl.Data;

/// <summary>
/// Acceso a la tabla cliente. Soft delete: eliminar marca deleted_at y toda
/// lectura de lista filtra deleted_at IS NULL (los préstamos históricos de un
/// cliente eliminado siguen mostrando su nombre vía JOIN sin ese filtro).
/// </summary>
public class ClienteRepository
{
    private readonly ConexionFactory _factory;

    public ClienteRepository(ConexionFactory factory) => _factory = factory;

    public async Task<long> CrearAsync(ClienteDatos datos, ModoApp ambito, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {DbNames.Cliente} (ambito, cedula, nombre, apellido, telefono, direccion, email, notas)
            VALUES (@ambito, @cedula, @nombre, @apellido, @telefono, @direccion, @email, @notas);
            SELECT LAST_INSERT_ID();
            """;
        AgregarParametrosDatos(cmd, datos);
        cmd.Parameters.AddWithValue("@ambito", ambito.ClaveDb());
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task ActualizarAsync(long id, ClienteDatos datos, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {DbNames.Cliente}
            SET cedula = @cedula, nombre = @nombre, apellido = @apellido, telefono = @telefono,
                direccion = @direccion, email = @email, notas = @notas, updated_at = UTC_TIMESTAMP()
            WHERE id = @id AND deleted_at IS NULL;
            """;
        AgregarParametrosDatos(cmd, datos);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Soft delete (nunca DELETE físico — los préstamos históricos lo referencian).</summary>
    public async Task EliminarAsync(long id, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {DbNames.Cliente}
            SET deleted_at = UTC_TIMESTAMP()
            WHERE id = @id AND deleted_at IS NULL;
            """;
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// True si otra ficha activa DEL MISMO ámbito ya usa esa cédula (unicidad
    /// amigable antes del UNIQUE(ambito,cedula)). La misma persona puede existir
    /// en dos estancias distintas sin chocar.
    /// </summary>
    public async Task<bool> ExisteCedulaAsync(string cedula, ModoApp ambito,
        long? excluirId = null, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT COUNT(*) FROM {DbNames.Cliente}
            WHERE cedula = @cedula AND ambito = @ambito AND deleted_at IS NULL
              AND (@excluirId IS NULL OR id <> @excluirId);
            """;
        cmd.Parameters.AddWithValue("@cedula", cedula);
        cmd.Parameters.AddWithValue("@ambito", ambito.ClaveDb());
        cmd.Parameters.AddWithValue("@excluirId", (object?)excluirId ?? DBNull.Value);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct)) > 0;
    }

    /// <summary>Cantidad de préstamos ACTIVOS del cliente (bloquea la eliminación).</summary>
    public async Task<int> ContarPrestamosActivosAsync(long clienteId, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT COUNT(*) FROM {DbNames.Prestamo}
            WHERE cliente_id = @clienteId AND estado = 'activo';
            """;
        cmd.Parameters.AddWithValue("@clienteId", clienteId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    /// <summary>Lista para la pantalla Clientes con agregados de préstamos (scoped al ámbito).</summary>
    public async Task<IReadOnlyList<ClienteResumen>> ObtenerResumenesAsync(ModoApp ambito, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();

        // El dealer NO presta: contar "préstamos activos" ahí daba siempre 0 y
        // ocupaba dos columnas con nada (reportado por Yuber 2026-07-31). Cada
        // estancia mira lo suyo, con subconsultas en vez de JOINs para que
        // ventas y alquileres no se multipliquen entre sí.
        cmd.CommandText = ambito == ModoApp.DealerControl
            ? $"""
            SELECT c.id, c.cedula, c.nombre, c.apellido, c.telefono,
                   (SELECT COUNT(*) FROM {DbNames.VentaVehiculo} vv
                    WHERE vv.cliente_id = c.id) AS contratos,
                   (SELECT COUNT(*) FROM {DbNames.Alquiler} a
                    WHERE a.cliente_id = c.id) AS alquileres,
                   (SELECT COALESCE(SUM(z.monto - z.monto_pagado), 0)
                    FROM {DbNames.VentaPlazo} z
                    JOIN {DbNames.VentaVehiculo} vv ON vv.id = z.venta_id
                    WHERE vv.cliente_id = c.id AND z.estado = 'pendiente') AS saldo_pendiente
            FROM {DbNames.Cliente} c
            WHERE c.deleted_at IS NULL AND c.ambito = @ambito
            ORDER BY c.nombre, c.apellido;
            """
            : $"""
            SELECT c.id, c.cedula, c.nombre, c.apellido, c.telefono,
                   COALESCE(SUM(p.estado = 'activo'), 0) AS contratos,
                   0 AS alquileres,
                   COALESCE(SUM(CASE WHEN p.estado = 'activo'
                                     THEN q.monto_total - q.monto_pagado END), 0) AS saldo_pendiente
            FROM {DbNames.Cliente} c
            LEFT JOIN {DbNames.Prestamo} p ON p.cliente_id = c.id
            LEFT JOIN {DbNames.Cuota} q ON q.prestamo_id = p.id
                 AND q.estado IN ('pendiente', 'vencida', 'en_mora')
            WHERE c.deleted_at IS NULL AND c.ambito = @ambito
            GROUP BY c.id, c.cedula, c.nombre, c.apellido, c.telefono
            ORDER BY c.nombre, c.apellido;
            """;
        cmd.Parameters.AddWithValue("@ambito", ambito.ClaveDb());

        var resumenes = new List<ClienteResumen>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            resumenes.Add(new ClienteResumen(
                reader.GetInt64("id"),
                reader.GetString("cedula"),
                reader.GetString("nombre"),
                reader.GetString("apellido"),
                reader.IsDBNull(reader.GetOrdinal("telefono")) ? null : reader.GetString("telefono"),
                Convert.ToInt32(reader["contratos"]),
                reader.GetDecimal("saldo_pendiente"),
                Convert.ToInt32(reader["alquileres"])));
        }
        return resumenes;
    }

    /// <summary>
    /// Clientes con cuotas ya vencidas sin cubrir (préstamos activos), para el
    /// notificador de vencimientos. Ordenados por antigüedad del vencimiento.
    /// </summary>
    public async Task<IReadOnlyList<ClienteVencido>> ObtenerClientesConVencidasAsync(
        DateOnly hoy, ModoApp ambito, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT c.id, CONCAT(c.nombre, ' ', c.apellido) AS nombre_completo,
                   COUNT(*) AS cuotas_vencidas,
                   SUM(q.monto_total - q.monto_pagado) AS monto_vencido,
                   MIN(q.fecha_vencimiento) AS primer_vencimiento
            FROM {DbNames.Cuota} q
            JOIN {DbNames.Prestamo} p ON p.id = q.prestamo_id
            JOIN {DbNames.Cliente} c ON c.id = p.cliente_id
            WHERE p.estado = 'activo'
              AND q.estado IN ('pendiente', 'vencida', 'en_mora')
              AND q.fecha_vencimiento < @hoy
              AND c.deleted_at IS NULL
              AND c.ambito = @ambito
            GROUP BY c.id, nombre_completo
            ORDER BY primer_vencimiento;
            """;
        cmd.Parameters.AddWithValue("@hoy", hoy.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@ambito", ambito.ClaveDb());

        var vencidos = new List<ClienteVencido>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            vencidos.Add(new ClienteVencido(
                reader.GetInt64("id"),
                reader.GetString("nombre_completo"),
                reader.GetInt32("cuotas_vencidas"),
                reader.GetDecimal("monto_vencido"),
                DateOnly.FromDateTime(reader.GetDateTime("primer_vencimiento"))));
        }
        return vencidos;
    }

    /// <summary>
    /// Clientes con cuotas VENCIDAS o POR VENCER dentro de los próximos
    /// <paramref name="diasAntes"/> días, para el recordatorio por correo
    /// (cliente 2026-07-19). Incluye el email del cliente y la cuota más próxima.
    /// </summary>
    public async Task<IReadOnlyList<RecordatorioCliente>> ObtenerRecordatoriosAsync(
        DateOnly hoy, int diasAntes, ModoApp ambito, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT c.id, TRIM(CONCAT(c.nombre, ' ', COALESCE(c.apellido, ''))) AS nombre_completo,
                   c.email,
                   MIN(q.fecha_vencimiento) AS proximo_venc,
                   SUM(q.monto_total - q.monto_pagado) AS monto_pendiente,
                   COUNT(*) AS cuotas_ventana,
                   MAX(q.fecha_vencimiento < @hoy) AS hay_vencidas
            FROM {DbNames.Cuota} q
            JOIN {DbNames.Prestamo} p ON p.id = q.prestamo_id
            JOIN {DbNames.Cliente} c ON c.id = p.cliente_id
            WHERE p.estado = 'activo'
              AND q.estado IN ('pendiente', 'vencida', 'en_mora')
              AND q.fecha_vencimiento <= @limite
              AND c.deleted_at IS NULL
              AND c.ambito = @ambito
            GROUP BY c.id, nombre_completo, c.email
            ORDER BY proximo_venc;
            """;
        cmd.Parameters.AddWithValue("@hoy", hoy.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@limite", hoy.AddDays(diasAntes).ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@ambito", ambito.ClaveDb());

        var lista = new List<RecordatorioCliente>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            lista.Add(new RecordatorioCliente(
                reader.GetInt64("id"),
                reader.GetString("nombre_completo"),
                reader.IsDBNull(reader.GetOrdinal("email")) ? null : reader.GetString("email"),
                DateOnly.FromDateTime(reader.GetDateTime("proximo_venc")),
                reader.GetDecimal("monto_pendiente"),
                Convert.ToInt32(reader["cuotas_ventana"]),
                Convert.ToBoolean(reader["hay_vencidas"])));
        }
        return lista;
    }

    /// <summary>Métricas de la ficha (mockup 3). hoy = fecha de negocio para contar vencidas.</summary>
    public async Task<ClienteMetricas> ObtenerMetricasAsync(long clienteId, DateOnly hoy, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT COALESCE(SUM(DISTINCT_CAPITAL.monto_capital), 0) AS total_prestado,
                   COALESCE(SUM(DISTINCT_CAPITAL.total_pagado), 0)  AS total_cobrado,
                   COALESCE(SUM(DISTINCT_CAPITAL.saldo_activo), 0)  AS saldo_pendiente,
                   COALESCE(SUM(DISTINCT_CAPITAL.es_activo), 0)     AS prestamos_activos,
                   COALESCE(SUM(DISTINCT_CAPITAL.cuotas_vencidas), 0) AS cuotas_vencidas
            FROM (
                SELECT p.monto_capital,
                       (p.estado = 'activo') AS es_activo,
                       COALESCE(SUM(q.monto_pagado), 0) AS total_pagado,
                       CASE WHEN p.estado = 'activo'
                            THEN COALESCE(SUM(CASE WHEN q.estado IN ('pendiente','vencida','en_mora')
                                                   THEN q.monto_total - q.monto_pagado END), 0)
                            ELSE 0 END AS saldo_activo,
                       COALESCE(SUM(q.estado IN ('pendiente','vencida','en_mora')
                                    AND q.fecha_vencimiento < @hoy), 0) AS cuotas_vencidas
                FROM {DbNames.Prestamo} p
                LEFT JOIN {DbNames.Cuota} q ON q.prestamo_id = p.id
                WHERE p.cliente_id = @clienteId
                GROUP BY p.id, p.monto_capital, p.estado
            ) AS DISTINCT_CAPITAL;
            """;
        cmd.Parameters.AddWithValue("@clienteId", clienteId);
        cmd.Parameters.AddWithValue("@hoy", hoy.ToDateTime(TimeOnly.MinValue));

        using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return new ClienteMetricas(
            reader.GetDecimal("total_prestado"),
            reader.GetDecimal("total_cobrado"),
            reader.GetDecimal("saldo_pendiente"),
            reader.GetInt32("prestamos_activos"),
            reader.GetInt32("cuotas_vencidas"));
    }

    private static void AgregarParametrosDatos(MySqlCommand cmd, ClienteDatos datos)
    {
        cmd.Parameters.AddWithValue("@cedula", datos.Cedula);
        cmd.Parameters.AddWithValue("@nombre", datos.Nombre);
        cmd.Parameters.AddWithValue("@apellido", datos.Apellido);
        cmd.Parameters.AddWithValue("@telefono", (object?)datos.Telefono ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@direccion", (object?)datos.Direccion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@email", (object?)datos.Email ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@notas", (object?)datos.Notas ?? DBNull.Value);
    }

    public async Task<IReadOnlyList<Cliente>> ObtenerActivosAsync(ModoApp ambito, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT id, cedula, nombre, apellido, telefono, direccion, email, notas, created_at, updated_at
            FROM {DbNames.Cliente}
            WHERE deleted_at IS NULL AND ambito = @ambito
            ORDER BY nombre, apellido;
            """;
        cmd.Parameters.AddWithValue("@ambito", ambito.ClaveDb());

        var clientes = new List<Cliente>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            clientes.Add(Mapear(reader));
        return clientes;
    }

    public async Task<Cliente?> ObtenerPorIdAsync(long id, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT id, cedula, nombre, apellido, telefono, direccion, email, notas, created_at, updated_at
            FROM {DbNames.Cliente}
            WHERE id = @id AND deleted_at IS NULL;
            """;
        cmd.Parameters.AddWithValue("@id", id);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Mapear(reader) : null;
    }

    private static Cliente Mapear(MySqlDataReader reader) => new()
    {
        Id = reader.GetInt64("id"),
        Cedula = reader.GetString("cedula"),
        Nombre = reader.GetString("nombre"),
        Apellido = reader.GetString("apellido"),
        Telefono = reader.IsDBNull(reader.GetOrdinal("telefono")) ? null : reader.GetString("telefono"),
        Direccion = reader.IsDBNull(reader.GetOrdinal("direccion")) ? null : reader.GetString("direccion"),
        Email = reader.IsDBNull(reader.GetOrdinal("email")) ? null : reader.GetString("email"),
        Notas = reader.IsDBNull(reader.GetOrdinal("notas")) ? null : reader.GetString("notas"),
        CreatedAtUtc = DateTime.SpecifyKind(reader.GetDateTime("created_at"), DateTimeKind.Utc),
        UpdatedAtUtc = reader.IsDBNull(reader.GetOrdinal("updated_at"))
            ? null
            : DateTime.SpecifyKind(reader.GetDateTime("updated_at"), DateTimeKind.Utc)
    };
}
