using MySqlConnector;
using FAControl.Common;
using FAControl.Models;

namespace FAControl.Data;

/// <summary>
/// Consultas agregadas del panel de control. Solo lectura.
/// Los límites de mes llegan ya convertidos a UTC (RD = UTC-4 sin DST),
/// y el "día de negocio" del gráfico se obtiene restando 4 horas al UTC.
/// </summary>
public class DashboardRepository
{
    private readonly ConexionFactory _factory;

    public DashboardRepository(ConexionFactory factory) => _factory = factory;

    public async Task<DashboardDatos> ObtenerAsync(
        DateOnly hoy,
        DateTime inicioMesUtc,
        DateTime inicioMesSiguienteUtc,
        DateTime inicioMesAnteriorUtc,
        CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);

        // ---------- KPIs ----------
        decimal capitalColocado, morosidad, cobrosDelMes, cobrosMesAnterior;
        int clientesActivos, prestamosActivos;

        using (var cmd = conexion.CreateCommand())
        {
            cmd.CommandText = $"""
                SELECT
                  (SELECT COALESCE(SUM(q.monto_total - q.monto_pagado), 0)
                   FROM {DbNames.Cuota} q
                   JOIN {DbNames.Prestamo} p ON p.id = q.prestamo_id
                   WHERE p.estado = 'activo'
                     AND q.estado IN ('pendiente', 'vencida', 'en_mora')) AS capital_colocado,
                  (SELECT COALESCE(SUM(q.monto_total - q.monto_pagado), 0)
                   FROM {DbNames.Cuota} q
                   JOIN {DbNames.Prestamo} p ON p.id = q.prestamo_id
                   WHERE p.estado = 'activo'
                     AND q.estado IN ('pendiente', 'vencida', 'en_mora')
                     AND q.fecha_vencimiento < @hoy) AS morosidad,
                  (SELECT COALESCE(SUM(g.monto_pagado), 0)
                   FROM {DbNames.Pago} g
                   WHERE g.deleted_at IS NULL
                     AND g.fecha_pago >= @inicioMes AND g.fecha_pago < @inicioMesSiguiente) AS cobros_mes,
                  (SELECT COALESCE(SUM(g.monto_pagado), 0)
                   FROM {DbNames.Pago} g
                   WHERE g.deleted_at IS NULL
                     AND g.fecha_pago >= @inicioMesAnterior AND g.fecha_pago < @inicioMes) AS cobros_mes_anterior,
                  (SELECT COUNT(DISTINCT p.cliente_id)
                   FROM {DbNames.Prestamo} p
                   WHERE p.estado = 'activo') AS clientes_activos,
                  (SELECT COUNT(*)
                   FROM {DbNames.Prestamo} p
                   WHERE p.estado = 'activo') AS prestamos_activos;
                """;
            cmd.Parameters.AddWithValue("@hoy", hoy.ToDateTime(TimeOnly.MinValue));
            cmd.Parameters.AddWithValue("@inicioMes", inicioMesUtc);
            cmd.Parameters.AddWithValue("@inicioMesSiguiente", inicioMesSiguienteUtc);
            cmd.Parameters.AddWithValue("@inicioMesAnterior", inicioMesAnteriorUtc);

            using var reader = await cmd.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            capitalColocado = reader.GetDecimal("capital_colocado");
            morosidad = reader.GetDecimal("morosidad");
            cobrosDelMes = reader.GetDecimal("cobros_mes");
            cobrosMesAnterior = reader.GetDecimal("cobros_mes_anterior");
            clientesActivos = reader.GetInt32("clientes_activos");
            prestamosActivos = reader.GetInt32("prestamos_activos");
        }

        // ---------- Cobros por día del mes en curso (gráfico) ----------
        var cobrosPorDia = new List<CobroDiario>();
        using (var cmd = conexion.CreateCommand())
        {
            cmd.CommandText = $"""
                SELECT DATE(DATE_SUB(g.fecha_pago, INTERVAL 4 HOUR)) AS dia,
                       SUM(g.monto_pagado) AS total
                FROM {DbNames.Pago} g
                WHERE g.deleted_at IS NULL
                  AND g.fecha_pago >= @inicioMes AND g.fecha_pago < @inicioMesSiguiente
                GROUP BY dia
                ORDER BY dia;
                """;
            cmd.Parameters.AddWithValue("@inicioMes", inicioMesUtc);
            cmd.Parameters.AddWithValue("@inicioMesSiguiente", inicioMesSiguienteUtc);

            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                cobrosPorDia.Add(new CobroDiario(
                    DateOnly.FromDateTime(reader.GetDateTime("dia")),
                    reader.GetDecimal("total")));
            }
        }

        // ---------- Alertas: cuotas vencidas o que vencen en ≤ 7 días ----------
        var alertas = new List<AlertaCobro>();
        using (var cmd = conexion.CreateCommand())
        {
            cmd.CommandText = $"""
                SELECT p.id AS prestamo_id, p.codigo,
                       CONCAT(c.nombre, ' ', c.apellido) AS cliente_nombre,
                       q.numero_cuota, q.fecha_vencimiento, q.monto_total, q.monto_pagado
                FROM {DbNames.Cuota} q
                JOIN {DbNames.Prestamo} p ON p.id = q.prestamo_id
                JOIN {DbNames.Cliente} c ON c.id = p.cliente_id
                WHERE p.estado = 'activo'
                  AND q.estado IN ('pendiente', 'vencida', 'en_mora')
                  AND q.fecha_vencimiento <= @limite
                ORDER BY q.fecha_vencimiento, p.id
                LIMIT 50;
                """;
            cmd.Parameters.AddWithValue("@limite", hoy.AddDays(7).ToDateTime(TimeOnly.MinValue));

            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                alertas.Add(new AlertaCobro(
                    reader.GetInt64("prestamo_id"),
                    reader.GetString("codigo"),
                    reader.GetString("cliente_nombre"),
                    reader.GetInt32("numero_cuota"),
                    DateOnly.FromDateTime(reader.GetDateTime("fecha_vencimiento")),
                    reader.GetDecimal("monto_total"),
                    reader.GetDecimal("monto_pagado")));
            }
        }

        return new DashboardDatos(capitalColocado, cobrosDelMes, cobrosMesAnterior,
            clientesActivos, prestamosActivos, morosidad, cobrosPorDia, alertas);
    }
}
