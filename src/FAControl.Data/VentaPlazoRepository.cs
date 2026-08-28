using MySqlConnector;
using FAControl.Common;
using FAControl.Models;

namespace FAControl.Data;

/// <summary>
/// Calendario de plazos de las ventas financiadas del dealer (016).
/// Las escrituras que forman parte de una operación multi-paso (crear la venta
/// con su plan, cobrar un plazo) reciben la conexión/transacción del Service.
/// </summary>
public class VentaPlazoRepository
{
    private readonly ConexionFactory _factory;

    public VentaPlazoRepository(ConexionFactory factory) => _factory = factory;

    /// <summary>Inserta el calendario completo dentro de la transacción de la venta.</summary>
    public async Task InsertarPlazosAsync(long ventaId, IReadOnlyList<VentaPlazo> plazos,
        MySqlConnection conexion, MySqlTransaction transaccion, CancellationToken ct = default)
    {
        foreach (var plazo in plazos)
        {
            using var cmd = conexion.CreateCommand();
            cmd.Transaction = transaccion;
            cmd.CommandText = $"""
                INSERT INTO {DbNames.VentaPlazo}
                  (venta_id, numero, fecha_vencimiento, monto)
                VALUES
                  (@ventaId, @numero, @vencimiento, @monto);
                """;
            cmd.Parameters.AddWithValue("@ventaId", ventaId);
            cmd.Parameters.AddWithValue("@numero", plazo.Numero);
            cmd.Parameters.AddWithValue("@vencimiento", plazo.FechaVencimiento.ToDateTime(TimeOnly.MinValue));
            cmd.Parameters.AddWithValue("@monto", plazo.Monto);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    /// <summary>Plazos de una venta, ordenados por número.</summary>
    public async Task<IReadOnlyList<VentaPlazo>> ObtenerDeVentaAsync(long ventaId,
        CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT id, venta_id, numero, fecha_vencimiento, monto, monto_pagado, estado
            FROM {DbNames.VentaPlazo}
            WHERE venta_id = @ventaId
            ORDER BY numero;
            """;
        cmd.Parameters.AddWithValue("@ventaId", ventaId);

        var lista = new List<VentaPlazo>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            lista.Add(Mapear(reader));
        return lista;
    }

    /// <summary>
    /// Plazo bloqueado FOR UPDATE dentro de la transacción del cobro: sin esto,
    /// dos cobros simultáneos podrían sobrepasar el monto del plazo.
    /// </summary>
    public async Task<VentaPlazo?> ObtenerParaCobroAsync(long plazoId, MySqlConnection conexion,
        MySqlTransaction transaccion, CancellationToken ct = default)
    {
        using var cmd = conexion.CreateCommand();
        cmd.Transaction = transaccion;
        cmd.CommandText = $"""
            SELECT id, venta_id, numero, fecha_vencimiento, monto, monto_pagado, estado
            FROM {DbNames.VentaPlazo}
            WHERE id = @id
            FOR UPDATE;
            """;
        cmd.Parameters.AddWithValue("@id", plazoId);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Mapear(reader) : null;
    }

    /// <summary>
    /// Plazos pendientes de la venta desde un número en adelante, BLOQUEADOS
    /// para el cobro (FOR UPDATE). Los necesita el abono que se pasa del plazo
    /// actual: el excedente baja al siguiente, y al siguiente, en orden.
    ///
    /// Se bloquean todos de una: si se tomaran de a uno, otro cajero podría
    /// cobrar el plazo 3 entre medio y el excedente se aplicaría dos veces.
    /// </summary>
    public async Task<IReadOnlyList<VentaPlazo>> ObtenerPendientesDesdeAsync(long ventaId, int numero,
        MySqlConnection conexion, MySqlTransaction transaccion, CancellationToken ct = default)
    {
        using var cmd = conexion.CreateCommand();
        cmd.Transaction = transaccion;
        cmd.CommandText = $"""
            SELECT id, venta_id, numero, fecha_vencimiento, monto, monto_pagado, estado
            FROM {DbNames.VentaPlazo}
            WHERE venta_id = @venta AND numero >= @numero AND estado = 'pendiente'
            ORDER BY numero
            FOR UPDATE;
            """;
        cmd.Parameters.AddWithValue("@venta", ventaId);
        cmd.Parameters.AddWithValue("@numero", numero);

        var lista = new List<VentaPlazo>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            lista.Add(Mapear(reader));
        return lista;
    }

    /// <summary>
    /// Marca como 'cancelado' los plazos que todavía se debían (028). Los
    /// PAGADOS no se tocan: ya se cobraron y su recibo existe.
    /// </summary>
    public async Task<int> CancelarPendientesAsync(long ventaId, MySqlConnection conexion,
        MySqlTransaction transaccion, CancellationToken ct = default)
    {
        using var cmd = conexion.CreateCommand();
        cmd.Transaction = transaccion;
        cmd.CommandText = $"""
            UPDATE {DbNames.VentaPlazo}
            SET estado = 'cancelado'
            WHERE venta_id = @venta AND estado = 'pendiente';
            """;
        cmd.Parameters.AddWithValue("@venta", ventaId);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Deja la venta como cancelada con su motivo y el reparto de la plata (028).
    /// Los montos se guardan CALCULADOS: si mañana cambia el porcentaje por
    /// defecto, esta cancelación tiene que seguir contando lo mismo.
    /// </summary>
    public async Task MarcarVentaCanceladaAsync(long ventaId, string motivo,
        decimal porcentaje, decimal retenido, decimal devuelto,
        MySqlConnection conexion, MySqlTransaction transaccion, CancellationToken ct = default)
    {
        using var cmd = conexion.CreateCommand();
        cmd.Transaction = transaccion;
        cmd.CommandText = $"""
            UPDATE {DbNames.VentaVehiculo}
            SET estado = 'cancelada', cancelada_at = UTC_TIMESTAMP(), cancelada_motivo = @motivo,
                retencion_porcentaje = @porcentaje, retenido = @retenido, devuelto = @devuelto
            WHERE id = @id AND estado = 'activa';
            """;
        cmd.Parameters.AddWithValue("@motivo", motivo);
        cmd.Parameters.AddWithValue("@porcentaje", porcentaje);
        cmd.Parameters.AddWithValue("@retenido", retenido);
        cmd.Parameters.AddWithValue("@devuelto", devuelto);
        cmd.Parameters.AddWithValue("@id", ventaId);
        if (await cmd.ExecuteNonQueryAsync(ct) == 0)
            throw new InvalidOperationException("La venta no existe o ya estaba cancelada.");
    }

    /// <summary>Vehículo de una venta, para devolverlo al inventario al cancelar.</summary>
    public async Task<long> ObtenerVehiculoDeVentaAsync(long ventaId, MySqlConnection conexion,
        MySqlTransaction transaccion, CancellationToken ct = default)
    {
        using var cmd = conexion.CreateCommand();
        cmd.Transaction = transaccion;
        cmd.CommandText = $"SELECT vehiculo_id FROM {DbNames.VentaVehiculo} WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", ventaId);
        var valor = await cmd.ExecuteScalarAsync(ct)
            ?? throw new InvalidOperationException("La venta no existe.");
        return Convert.ToInt64(valor);
    }

    /// <summary>Actualiza lo acumulado y el estado del plazo tras un abono.</summary>
    public async Task ActualizarTrasPagoAsync(long plazoId, decimal montoPagado, EstadoPlazo estado,
        MySqlConnection conexion, MySqlTransaction transaccion, CancellationToken ct = default)
    {
        using var cmd = conexion.CreateCommand();
        cmd.Transaction = transaccion;
        cmd.CommandText = $"""
            UPDATE {DbNames.VentaPlazo}
            SET monto_pagado = @montoPagado, estado = @estado, updated_at = UTC_TIMESTAMP()
            WHERE id = @id;
            """;
        cmd.Parameters.AddWithValue("@montoPagado", montoPagado);
        cmd.Parameters.AddWithValue("@estado", EnumMap.ADb(estado));
        cmd.Parameters.AddWithValue("@id", plazoId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Registra el abono con su recibo dentro de la transacción del cobro.</summary>
    public async Task<long> InsertarPagoAsync(VentaPlazoPago pago, MySqlConnection conexion,
        MySqlTransaction transaccion, CancellationToken ct = default)
    {
        using var cmd = conexion.CreateCommand();
        cmd.Transaction = transaccion;
        cmd.CommandText = $"""
            INSERT INTO {DbNames.VentaPlazoPago}
              (plazo_id, numero_recibo, fecha_pago, monto, metodo_pago, ncf, notas, created_by)
            VALUES
              (@plazoId, @recibo, @fecha, @monto, @metodoPago, @ncf, @notas, @createdBy);
            SELECT LAST_INSERT_ID();
            """;
        cmd.Parameters.AddWithValue("@plazoId", pago.PlazoId);
        cmd.Parameters.AddWithValue("@recibo", pago.NumeroRecibo);
        cmd.Parameters.AddWithValue("@fecha", pago.FechaPagoUtc);
        cmd.Parameters.AddWithValue("@monto", pago.Monto);
        cmd.Parameters.AddWithValue("@metodoPago", EnumMap.ADb(pago.MetodoPago));
        // Solo la primera fila del abono lleva comprobante (uq_plazo_pago_ncf)
        cmd.Parameters.AddWithValue("@ncf", (object?)pago.Ncf ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@notas", (object?)pago.Notas ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@createdBy",
            SesionActual.HaySesionActiva ? SesionActual.Id : (object)DBNull.Value);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
    }

    /// <summary>Abonos registrados a los plazos de una venta (historial).</summary>
    public async Task<IReadOnlyList<VentaPlazoPago>> ObtenerPagosDeVentaAsync(long ventaId,
        CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT p.id, p.plazo_id, p.numero_recibo, p.fecha_pago, p.monto, p.metodo_pago,
                   p.ncf, p.notas
            FROM {DbNames.VentaPlazoPago} p
            JOIN {DbNames.VentaPlazo} z ON z.id = p.plazo_id
            WHERE z.venta_id = @ventaId AND p.deleted_at IS NULL
            ORDER BY p.fecha_pago DESC, p.id DESC;
            """;
        cmd.Parameters.AddWithValue("@ventaId", ventaId);

        var lista = new List<VentaPlazoPago>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            lista.Add(new VentaPlazoPago
            {
                Id = reader.GetInt64("id"),
                PlazoId = reader.GetInt64("plazo_id"),
                NumeroRecibo = reader.GetString("numero_recibo"),
                FechaPagoUtc = DateTime.SpecifyKind(reader.GetDateTime("fecha_pago"), DateTimeKind.Utc),
                Monto = reader.GetDecimal("monto"),
                MetodoPago = EnumMap.MetodoPagoDeDb(reader.GetString("metodo_pago")),
                Ncf = reader.IsDBNull(reader.GetOrdinal("ncf")) ? null : reader.GetString("ncf"),
                Notas = reader.IsDBNull(reader.GetOrdinal("notas")) ? null : reader.GetString("notas")
            });
        return lista;
    }

    private static VentaPlazo Mapear(MySqlDataReader reader) => new()
    {
        Id = reader.GetInt64("id"),
        VentaId = reader.GetInt64("venta_id"),
        Numero = reader.GetInt32("numero"),
        FechaVencimiento = DateOnly.FromDateTime(reader.GetDateTime("fecha_vencimiento")),
        Monto = reader.GetDecimal("monto"),
        MontoPagado = reader.GetDecimal("monto_pagado"),
        Estado = EnumMap.EstadoPlazoDeDb(reader.GetString("estado"))
    };

    /// <summary>
    /// Cuantos abonos tiene la venta (033): decide hasta donde se puede
    /// corregir. La inicial NO cuenta como abono — se recibe al firmar y no
    /// emite recibo numerado.
    /// </summary>
    public async Task<int> ContarAbonosAsync(long ventaId, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT COUNT(*) FROM {DbNames.VentaPlazoPago} p
            JOIN {DbNames.VentaPlazo} z ON z.id = p.plazo_id
            WHERE z.venta_id = @venta;
            """;
        cmd.Parameters.AddWithValue("@venta", ventaId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    /// <summary>
    /// Borra el calendario de plazos para regenerarlo tras una correccion.
    ///
    /// El servicio solo llega aca cuando NO hay ningun abono, asi que ningun
    /// plazo tiene recibo colgando: regenerar es recalcular, no borrar historia.
    /// La FK de venta_plazo_pago es la ultima red — con un pago, MySQL rechaza
    /// el DELETE y la transaccion se revierte.
    /// </summary>
    public async Task BorrarPlazosAsync(long ventaId, MySqlConnection conexion,
        MySqlTransaction transaccion, CancellationToken ct = default)
    {
        using var cmd = conexion.CreateCommand();
        cmd.Transaction = transaccion;
        cmd.CommandText = $"DELETE FROM {DbNames.VentaPlazo} WHERE venta_id = @venta;";
        cmd.Parameters.AddWithValue("@venta", ventaId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ---------- Correccion de una venta con cobros ya hechos (035) ----------

    /// <summary>
    /// Corre los numeros de los plazos actuales fuera del rango util para
    /// poder insertar el plan nuevo sin chocar con uq_venta_plazo(venta_id,
    /// numero).
    ///
    /// Es un paso intermedio DENTRO de la transaccion: al terminar, estos
    /// plazos ya no tienen pagos apuntandolos y se borran. Si algo falla, el
    /// rollback los devuelve a su numeracion original.
    ///
    /// El desplazamiento es grande (100000) para que no pueda solaparse con un
    /// plan nuevo: el tope de plazos que acepta el sistema son 240.
    /// </summary>
    public async Task ApartarPlazosAsync(long ventaId, MySqlConnection conexion,
        MySqlTransaction transaccion, CancellationToken ct = default)
    {
        using var cmd = conexion.CreateCommand();
        cmd.Transaction = transaccion;
        // Descendente: renumerar de mayor a menor evita chocar consigo mismo
        // mientras se recorre.
        cmd.CommandText = $"""
            UPDATE {DbNames.VentaPlazo}
            SET numero = numero + 100000
            WHERE venta_id = @venta
            ORDER BY numero DESC;
            """;
        cmd.Parameters.AddWithValue("@venta", ventaId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Los plazos apartados (numero &gt;= 100000), para poder borrarlos al final.</summary>
    public async Task BorrarPlazosApartadosAsync(long ventaId, MySqlConnection conexion,
        MySqlTransaction transaccion, CancellationToken ct = default)
    {
        using var cmd = conexion.CreateCommand();
        cmd.Transaction = transaccion;
        cmd.CommandText = $"""
            DELETE FROM {DbNames.VentaPlazo}
            WHERE venta_id = @venta AND numero >= 100000;
            """;
        cmd.Parameters.AddWithValue("@venta", ventaId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Los pagos de la venta con su id y su monto, en orden cronologico, para
    /// re-imputarlos al plan nuevo. Se leen DENTRO de la transaccion.
    /// </summary>
    public async Task<IReadOnlyList<(long Id, decimal Monto)>> ObtenerPagosParaReimputarAsync(
        long ventaId, MySqlConnection conexion, MySqlTransaction transaccion,
        CancellationToken ct = default)
    {
        using var cmd = conexion.CreateCommand();
        cmd.Transaction = transaccion;
        cmd.CommandText = $"""
            SELECT g.id, g.monto
            FROM {DbNames.VentaPlazoPago} g
            JOIN {DbNames.VentaPlazo} z ON z.id = g.plazo_id
            WHERE z.venta_id = @venta AND g.deleted_at IS NULL
            ORDER BY g.fecha_pago, g.id;
            """;
        cmd.Parameters.AddWithValue("@venta", ventaId);

        var lista = new List<(long, decimal)>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            lista.Add((reader.GetInt64("id"), reader.GetDecimal("monto")));
        return lista;
    }

    /// <summary>
    /// Reapunta un pago al plazo que le toca en el plan nuevo. El recibo NO se
    /// toca: su numero, su fecha, su monto y quien lo cobro quedan como estan.
    /// Lo unico que cambia es a que plazo se imputa.
    /// </summary>
    public async Task ReapuntarPagoAsync(long pagoId, long plazoId, MySqlConnection conexion,
        MySqlTransaction transaccion, CancellationToken ct = default)
    {
        using var cmd = conexion.CreateCommand();
        cmd.Transaction = transaccion;
        cmd.CommandText = $"UPDATE {DbNames.VentaPlazoPago} SET plazo_id = @plazo WHERE id = @id;";
        cmd.Parameters.AddWithValue("@plazo", plazoId);
        cmd.Parameters.AddWithValue("@id", pagoId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Ids y numeros de los plazos de una venta (tras insertar el plan nuevo).</summary>
    public async Task<IReadOnlyList<(long Id, int Numero, decimal Monto)>> ObtenerPlazosNuevosAsync(
        long ventaId, MySqlConnection conexion, MySqlTransaction transaccion,
        CancellationToken ct = default)
    {
        using var cmd = conexion.CreateCommand();
        cmd.Transaction = transaccion;
        cmd.CommandText = $"""
            SELECT id, numero, monto FROM {DbNames.VentaPlazo}
            WHERE venta_id = @venta AND numero < 100000
            ORDER BY numero;
            """;
        cmd.Parameters.AddWithValue("@venta", ventaId);

        var lista = new List<(long, int, decimal)>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            lista.Add((reader.GetInt64("id"), reader.GetInt32("numero"), reader.GetDecimal("monto")));
        return lista;
    }
}
