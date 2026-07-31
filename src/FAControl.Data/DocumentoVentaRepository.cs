using MySqlConnector;
using FAControl.Common;
using FAControl.Models;

namespace FAControl.Data;

/// <summary>
/// Fichas del expediente digital (018). Solo la BD: quien mueve los archivos
/// en el disco es ExpedienteService, para que las dos cosas no se mezclen.
/// </summary>
public class DocumentoVentaRepository
{
    private readonly ConexionFactory _factory;

    public DocumentoVentaRepository(ConexionFactory factory) => _factory = factory;

    private const string Columnas = """
        d.id, d.venta_id, d.prestamo_id, d.nombre, d.ruta_relativa, d.extension, d.tamano_bytes,
        d.tipo, d.notas, d.created_at,
        TRIM(CONCAT(COALESCE(u.nombre, ''), ' ', COALESCE(u.apellido, ''))) AS subido_por
        """;

    public async Task<IReadOnlyList<DocumentoVenta>> ObtenerDeAsync(DuenoExpediente dueno,
        CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT {Columnas}
            FROM {DbNames.Documento} d
            LEFT JOIN {DbNames.Usuario} u ON u.id = d.created_by
            WHERE (d.venta_id <=> @venta) AND (d.prestamo_id <=> @prestamo) AND d.deleted_at IS NULL
            ORDER BY d.created_at DESC;
            """;
        AgregarDueno(cmd, dueno);

        var lista = new List<DocumentoVenta>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            lista.Add(Mapear(reader));
        return lista;
    }

    public async Task<DocumentoVenta?> ObtenerPorIdAsync(long id, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT {Columnas}
            FROM {DbNames.Documento} d
            LEFT JOIN {DbNames.Usuario} u ON u.id = d.created_by
            WHERE d.id = @id AND d.deleted_at IS NULL;
            """;
        cmd.Parameters.AddWithValue("@id", id);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Mapear(reader) : null;
    }

    /// <summary>Cuántos documentos tiene cada venta (para el expediente de contratos).</summary>
    public async Task<int> ContarDeAsync(DuenoExpediente dueno, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT COUNT(*) FROM {DbNames.Documento}
            WHERE (venta_id <=> @venta) AND (prestamo_id <=> @prestamo) AND deleted_at IS NULL;
            """;
        AgregarDueno(cmd, dueno);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    /// <summary>
    /// Inserta la ficha y devuelve su id. La ruta se arma DESPUÉS con ese id
    /// (el nombre del archivo en disco lo lleva delante), así que se guarda
    /// provisoria y se corrige con <see cref="ActualizarRutaAsync"/>.
    /// </summary>
    public async Task<long> CrearAsync(DuenoExpediente dueno, string nombre, string rutaRelativa,
        string extension, long tamanoBytes, TipoDocumento tipo, string? notas,
        long? usuarioId, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {DbNames.Documento}
              (venta_id, prestamo_id, nombre, ruta_relativa, extension, tamano_bytes, tipo, notas, created_by)
            VALUES (@venta, @nombre, @ruta, @ext, @tamano, @tipo, @notas, @usuario);
            SELECT LAST_INSERT_ID();
            """;
        AgregarDueno(cmd, dueno);
        cmd.Parameters.AddWithValue("@nombre", nombre);
        cmd.Parameters.AddWithValue("@ruta", rutaRelativa);
        cmd.Parameters.AddWithValue("@ext", extension);
        cmd.Parameters.AddWithValue("@tamano", tamanoBytes);
        cmd.Parameters.AddWithValue("@tipo", EnumMap.ADb(tipo));
        cmd.Parameters.AddWithValue("@notas", (object?)notas ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@usuario", (object?)usuarioId ?? DBNull.Value);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task ActualizarRutaAsync(long id, string rutaRelativa, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"UPDATE {DbNames.Documento} SET ruta_relativa = @ruta WHERE id = @id;";
        cmd.Parameters.AddWithValue("@ruta", rutaRelativa);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Re-ubicar: el documento pasa al expediente de otra venta.</summary>
    public async Task MoverAsync(long id, DuenoExpediente destino, string rutaRelativa,
        CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {DbNames.Documento}
            SET venta_id = @venta, prestamo_id = @prestamo, ruta_relativa = @ruta
            WHERE id = @id;
            """;
        AgregarDueno(cmd, destino);
        cmd.Parameters.AddWithValue("@ruta", rutaRelativa);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Soft delete: la ficha queda, con su rastro de quién la subió.</summary>
    public async Task EliminarAsync(long id, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"UPDATE {DbNames.Documento} SET deleted_at = UTC_TIMESTAMP() WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Los dos parámetros del dueño. Van SIEMPRE los dos, uno en NULL: las
    /// consultas comparan con &lt;=&gt; (igualdad que trata NULL como valor), así
    /// una sola consulta sirve para ventas y para préstamos.
    /// </summary>
    private static void AgregarDueno(MySqlCommand cmd, DuenoExpediente dueno)
    {
        cmd.Parameters.AddWithValue("@venta",
            dueno.Tipo == TipoExpediente.Venta ? dueno.Id : DBNull.Value);
        cmd.Parameters.AddWithValue("@prestamo",
            dueno.Tipo == TipoExpediente.Prestamo ? dueno.Id : DBNull.Value);
    }

    private static DocumentoVenta Mapear(MySqlConnector.MySqlDataReader reader) => new()
    {
        Id = reader.GetInt64("id"),
        VentaId = reader.IsDBNull(reader.GetOrdinal("venta_id")) ? null : reader.GetInt64("venta_id"),
        PrestamoId = reader.IsDBNull(reader.GetOrdinal("prestamo_id")) ? null : reader.GetInt64("prestamo_id"),
        Nombre = reader.GetString("nombre"),
        RutaRelativa = reader.GetString("ruta_relativa"),
        Extension = reader.GetString("extension"),
        TamanoBytes = reader.GetInt64("tamano_bytes"),
        Tipo = EnumMap.TipoDocumentoDeDb(reader.GetString("tipo")),
        Notas = reader.IsDBNull(reader.GetOrdinal("notas")) ? null : reader.GetString("notas"),
        CreatedAtUtc = DateTime.SpecifyKind(reader.GetDateTime("created_at"), DateTimeKind.Utc),
        SubidoPor = reader.IsDBNull(reader.GetOrdinal("subido_por")) ? null : reader.GetString("subido_por")
    };
}
