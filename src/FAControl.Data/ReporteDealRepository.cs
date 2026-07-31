using FAControl.Common;
using FAControl.Models;

namespace FAControl.Data;

/// <summary>
/// Consultas del expediente de contratos y del reporte propio de DealControl
/// (pedido 2026-07-25). Solo lectura y SOLO tablas del dealer: nunca toca
/// prestamo/cuota/pago — el aislamiento con PrestControl es total.
/// El día de negocio se obtiene restando 4 horas al UTC (RD, sin DST).
/// </summary>
public class ReporteDealRepository
{
    private const int OffsetRdHoras = 4;

    private readonly ConexionFactory _factory;

    public ReporteDealRepository(ConexionFactory factory) => _factory = factory;

    /// <summary>
    /// Expediente de contratos: una fila por venta con su cliente, vendedor,
    /// matrícula y estado de los plazos.
    /// </summary>
    public async Task<IReadOnlyList<ContratoDealFila>> ObtenerContratosAsync(DateOnly hoy,
        CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT vv.id, vv.codigo, vv.fecha_venta, vv.precio, vv.tipo_venta, vv.inicial,
                   TRIM(CONCAT(c.nombre, ' ', COALESCE(c.apellido, ''))) AS cliente,
                   COALESCE(u.nombre, '—') AS vendedor,
                   TRIM(CONCAT(v.marca, ' ', v.modelo, COALESCE(CONCAT(' ', v.anio), ''))) AS vehiculo,
                   v.matricula, v.placa,
                   (SELECT COUNT(*) FROM {DbNames.VentaPlazo} z
                    WHERE z.venta_id = vv.id AND z.estado <> 'cancelado') AS plazos_totales,
                   (SELECT COUNT(*) FROM {DbNames.VentaPlazo} z
                    WHERE z.venta_id = vv.id AND z.estado = 'pagado') AS plazos_pagados,
                   (SELECT COUNT(*) FROM {DbNames.VentaPlazo} z
                    WHERE z.venta_id = vv.id AND z.estado = 'pendiente'
                      AND z.fecha_vencimiento < @hoy
                      AND z.monto_pagado < z.monto) AS plazos_atrasados,
                   (SELECT COALESCE(SUM(z.monto - z.monto_pagado), 0) FROM {DbNames.VentaPlazo} z
                    WHERE z.venta_id = vv.id AND z.estado <> 'cancelado') AS pendiente,
                   (SELECT COUNT(*) FROM {DbNames.Documento} d
                    WHERE d.venta_id = vv.id AND d.deleted_at IS NULL) AS adjuntos
            FROM {DbNames.VentaVehiculo} vv
            JOIN {DbNames.Cliente} c ON c.id = vv.cliente_id
            JOIN {DbNames.Vehiculo} v ON v.id = vv.vehiculo_id
            LEFT JOIN {DbNames.Usuario} u ON u.id = vv.created_by
            ORDER BY vv.fecha_venta DESC;
            """;
        cmd.Parameters.AddWithValue("@hoy", hoy.ToDateTime(TimeOnly.MinValue));

        var lista = new List<ContratoDealFila>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var tipo = EnumMap.TipoVentaDeDb(reader.GetString("tipo_venta"));
            // Una separación no tiene plazos: lo pendiente es precio − adelanto
            var pendiente = tipo == TipoVenta.Separacion
                ? reader.GetDecimal("precio") - reader.GetDecimal("inicial")
                : reader.GetDecimal("pendiente");

            lista.Add(new ContratoDealFila(
                reader.GetInt64("id"),
                reader.GetString("codigo"),
                DateTime.SpecifyKind(reader.GetDateTime("fecha_venta"), DateTimeKind.Utc),
                reader.GetString("cliente"),
                reader.GetString("vendedor"),
                reader.GetString("vehiculo"),
                reader.IsDBNull(reader.GetOrdinal("matricula")) ? null : reader.GetString("matricula"),
                reader.IsDBNull(reader.GetOrdinal("placa")) ? null : reader.GetString("placa"),
                reader.GetDecimal("precio"),
                tipo,
                Convert.ToInt32(reader["plazos_totales"]),
                Convert.ToInt32(reader["plazos_pagados"]),
                Convert.ToInt32(reader["plazos_atrasados"]),
                pendiente,
                Convert.ToInt32(reader["adjuntos"])));
        }
        return lista;
    }

    /// <summary>
    /// Los ALQUILERES como contratos del dealer (032 — pedido del cliente:
    /// "tambien debe mostrarse en contratos").
    ///
    /// Va aparte de la consulta de ventas a proposito. Los dos son contratos y
    /// se muestran en la misma pantalla, pero viven en tablas distintas y no
    /// comparten forma: un alquiler no tiene plazos ni factura. Forzarlos en un
    /// UNION obligaria a rellenar media docena de columnas con ceros que no
    /// significan nada, y esos ceros terminan mostrandose en pantalla.
    /// </summary>
    public async Task<IReadOnlyList<ContratoDealFila>> ObtenerContratosDeAlquilerAsync(
        CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT a.id, a.codigo, a.created_at, a.monto_total, a.monto_final, a.estado,
                   TRIM(CONCAT(c.nombre, ' ', COALESCE(c.apellido, ''))) AS cliente,
                   COALESCE(u.nombre, '—') AS usuario,
                   TRIM(CONCAT(v.marca, ' ', v.modelo, COALESCE(CONCAT(' ', v.anio), ''))) AS vehiculo,
                   v.matricula, v.placa,
                   (SELECT COUNT(*) FROM {DbNames.Documento} d
                    WHERE d.alquiler_id = a.id AND d.deleted_at IS NULL) AS adjuntos
            FROM {DbNames.Alquiler} a
            JOIN {DbNames.Cliente} c ON c.id = a.cliente_id
            JOIN {DbNames.Vehiculo} v ON v.id = a.vehiculo_id
            -- LEFT: el contrato no desaparece del listado si se borro el usuario
            LEFT JOIN {DbNames.Usuario} u ON u.id = a.created_by
            ORDER BY a.created_at DESC;
            """;

        var lista = new List<ContratoDealFila>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var estado = reader.GetString("estado");
            // Cerrado: vale lo que realmente correspondio cobrar. Abierto: lo pactado.
            var monto = reader.IsDBNull(reader.GetOrdinal("monto_final"))
                ? reader.GetDecimal("monto_total")
                : reader.GetDecimal("monto_final");

            lista.Add(new ContratoDealFila(
                reader.GetInt64("id"),
                reader.GetString("codigo"),
                DateTime.SpecifyKind(reader.GetDateTime("created_at"), DateTimeKind.Utc),
                reader.GetString("cliente"),
                reader.GetString("usuario"),
                reader.GetString("vehiculo"),
                reader.IsDBNull(reader.GetOrdinal("matricula")) ? null : reader.GetString("matricula"),
                reader.IsDBNull(reader.GetOrdinal("placa")) ? null : reader.GetString("placa"),
                monto,
                TipoVenta.Contado,      // no aplica a un alquiler; manda EsAlquiler
                PlazosTotales: 0,
                PlazosPagados: 0,
                PlazosAtrasados: 0,
                Pendiente: 0m,
                DocumentosAdjuntos: Convert.ToInt32(reader["adjuntos"]),
                EsAlquiler: true,
                EstadoAlquilerTexto: estado switch
                {
                    "activo" => "Activo",
                    "finalizado" => "Finalizado",
                    _ => "Cancelado"
                }));
        }
        return lista;
    }

    /// <summary>
    /// Reporte del dealer en un rango. <paramref name="porcentajeComision"/> lo
    /// define el negocio en Configuración (la app no inventa la tasa).
    /// </summary>
    /// <summary>
    /// Usuarios que registraron ventas o alquileres, para el combo de filtro.
    ///
    /// Sale de las OPERACIONES y no de la tabla usuario: un combo con todos los
    /// usuarios del sistema llenaria la lista de cajeros y cobradores que nunca
    /// tocaron el dealer, y elegirlos daria siempre un reporte vacio.
    /// </summary>
    public async Task<IReadOnlyList<OpcionFiltroReporte>> ObtenerUsuariosDelDealerAsync(
        CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT u.id, TRIM(CONCAT(u.nombre, ' ', COALESCE(u.apellido, ''))) AS nombre
            FROM {DbNames.Usuario} u
            WHERE EXISTS (SELECT 1 FROM {DbNames.VentaVehiculo} vv WHERE vv.created_by = u.id)
               OR EXISTS (SELECT 1 FROM {DbNames.Alquiler} a WHERE a.created_by = u.id)
            ORDER BY nombre;
            """;

        var lista = new List<OpcionFiltroReporte>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            lista.Add(new OpcionFiltroReporte(reader.GetInt64("id"), reader.GetString("nombre")));
        return lista;
    }

    /// <summary>
    /// Clientes con operaciones en el dealer, para el combo de filtro. Mismo
    /// criterio que los usuarios: solo los que aparecen en alguna operacion.
    /// </summary>
    public async Task<IReadOnlyList<OpcionFiltroReporte>> ObtenerClientesDelDealerAsync(
        CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT c.id, TRIM(CONCAT(c.nombre, ' ', COALESCE(c.apellido, ''))) AS nombre
            FROM {DbNames.Cliente} c
            WHERE c.deleted_at IS NULL
              AND (EXISTS (SELECT 1 FROM {DbNames.VentaVehiculo} vv WHERE vv.cliente_id = c.id)
                OR EXISTS (SELECT 1 FROM {DbNames.Alquiler} a WHERE a.cliente_id = c.id))
            ORDER BY nombre;
            """;

        var lista = new List<OpcionFiltroReporte>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            lista.Add(new OpcionFiltroReporte(reader.GetInt64("id"), reader.GetString("nombre")));
        return lista;
    }

    public async Task<ReporteDeal> ObtenerReporteAsync(DateOnly desde, DateOnly hasta,
        decimal porcentajeComision, long? usuarioId = null, long? clienteId = null,
        CancellationToken ct = default)
    {
        // Rango local [desde, hasta] → instantes UTC [inicio, fin)
        var inicioUtc = desde.ToDateTime(TimeOnly.MinValue).AddHours(OffsetRdHoras);
        var finUtc = hasta.AddDays(1).ToDateTime(TimeOnly.MinValue).AddHours(OffsetRdHoras);

        using var conexion = await _factory.AbrirAsync(ct);

        int ventas = 0, alquileres = 0, disponibles = 0;
        decimal montoVendido = 0m, ganancia = 0m, ingresosAlquiler = 0m,
                capitalInvertido = 0m, pendienteDeCobro = 0m;

        using (var cmd = conexion.CreateCommand())
        {
            cmd.CommandText = $"""
                SELECT
                  (SELECT COUNT(*) FROM {DbNames.VentaVehiculo}
                   WHERE fecha_venta >= @inicio AND fecha_venta < @fin
                     AND (@usuario IS NULL OR created_by = @usuario)
                     AND (@cliente IS NULL OR cliente_id = @cliente)) AS ventas,
                  (SELECT COALESCE(SUM(precio), 0) FROM {DbNames.VentaVehiculo}
                   WHERE fecha_venta >= @inicio AND fecha_venta < @fin
                     AND (@usuario IS NULL OR created_by = @usuario)
                     AND (@cliente IS NULL OR cliente_id = @cliente)) AS monto_vendido,
                  (SELECT COALESCE(SUM(vv.precio - (v.costo_adquisicion + v.gastos_importacion)), 0)
                   FROM {DbNames.VentaVehiculo} vv
                   JOIN {DbNames.Vehiculo} v ON v.id = vv.vehiculo_id
                   WHERE vv.fecha_venta >= @inicio AND vv.fecha_venta < @fin
                     AND (@usuario IS NULL OR vv.created_by = @usuario)
                     AND (@cliente IS NULL OR vv.cliente_id = @cliente)) AS ganancia,
                  (SELECT COUNT(*) FROM {DbNames.Alquiler}
                   WHERE estado <> 'cancelado' AND created_at >= @inicio AND created_at < @fin
                     AND (@usuario IS NULL OR created_by = @usuario)
                     AND (@cliente IS NULL OR cliente_id = @cliente)) AS alquileres,
                  -- Cerrado: vale lo que realmente correspondio cobrar (031).
                  -- Abierto: lo pactado, que es lo mejor que se sabe todavia.
                  (SELECT COALESCE(SUM(COALESCE(monto_final, monto_total)), 0) FROM {DbNames.Alquiler}
                   WHERE estado <> 'cancelado' AND created_at >= @inicio AND created_at < @fin
                     AND (@usuario IS NULL OR created_by = @usuario)
                     AND (@cliente IS NULL OR cliente_id = @cliente)) AS ingresos_alquiler,
                  (SELECT COUNT(*) FROM {DbNames.Vehiculo}
                   WHERE deleted_at IS NULL AND estado = 'disponible') AS disponibles,
                  (SELECT COALESCE(SUM(costo_adquisicion + gastos_importacion), 0) FROM {DbNames.Vehiculo}
                   WHERE deleted_at IS NULL AND estado IN ('disponible','reservado','alquilado')) AS capital_invertido,
                  -- Lo que falta cobrar SI se filtra por cliente y por quien vendio:
                  -- "cuanto me debe este cliente" es justo lo que se quiere ver.
                  (SELECT COALESCE(SUM(z.monto - z.monto_pagado), 0)
                   FROM {DbNames.VentaPlazo} z
                   JOIN {DbNames.VentaVehiculo} vv ON vv.id = z.venta_id
                   WHERE z.estado = 'pendiente'
                     AND (@usuario IS NULL OR vv.created_by = @usuario)
                     AND (@cliente IS NULL OR vv.cliente_id = @cliente)) AS pendiente_cobro;
                """;
            cmd.Parameters.AddWithValue("@inicio", inicioUtc);
            cmd.Parameters.AddWithValue("@fin", finUtc);
            cmd.Parameters.AddWithValue("@usuario", (object?)usuarioId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@cliente", (object?)clienteId ?? DBNull.Value);

            using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                ventas = Convert.ToInt32(reader["ventas"]);
                montoVendido = reader.GetDecimal("monto_vendido");
                ganancia = reader.GetDecimal("ganancia");
                alquileres = Convert.ToInt32(reader["alquileres"]);
                ingresosAlquiler = reader.GetDecimal("ingresos_alquiler");
                disponibles = Convert.ToInt32(reader["disponibles"]);
                capitalInvertido = reader.GetDecimal("capital_invertido");
                pendienteDeCobro = reader.GetDecimal("pendiente_cobro");
            }
        }

        var porVendedor = new List<ComisionVendedor>();
        using (var cmd = conexion.CreateCommand())
        {
            cmd.CommandText = $"""
                SELECT COALESCE(u.nombre, 'Sin vendedor') AS vendedor,
                       COUNT(*) AS cantidad,
                       COALESCE(SUM(vv.precio), 0) AS monto
                FROM {DbNames.VentaVehiculo} vv
                LEFT JOIN {DbNames.Usuario} u ON u.id = vv.created_by
                WHERE vv.fecha_venta >= @inicio AND vv.fecha_venta < @fin
                  AND (@usuario IS NULL OR vv.created_by = @usuario)
                  AND (@cliente IS NULL OR vv.cliente_id = @cliente)
                GROUP BY vendedor
                ORDER BY monto DESC;
                """;
            cmd.Parameters.AddWithValue("@inicio", inicioUtc);
            cmd.Parameters.AddWithValue("@fin", finUtc);
            cmd.Parameters.AddWithValue("@usuario", (object?)usuarioId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@cliente", (object?)clienteId ?? DBNull.Value);

            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var monto = reader.GetDecimal("monto");
                porVendedor.Add(new ComisionVendedor(
                    reader.GetString("vendedor"),
                    Convert.ToInt32(reader["cantidad"]),
                    monto,
                    // La comisión se redondea al final del cálculo, no antes
                    Math.Round(monto * porcentajeComision / 100m, 2, MidpointRounding.AwayFromZero)));
            }
        }

        return new ReporteDeal(
            desde, hasta,
            ventas, montoVendido, ganancia,
            alquileres, ingresosAlquiler,
            disponibles, capitalInvertido,
            pendienteDeCobro,
            porVendedor,
            // El inventario es del NEGOCIO, no de un usuario ni de un cliente:
            // se calcula sin filtrar. La pantalla lo aclara, si no el dueño ve
            // "capital invertido" al lado de "cliente: Juan" y lo lee como de Juan.
            HayFiltro: usuarioId is not null || clienteId is not null);
    }
}
