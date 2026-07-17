using MySqlConnector;
using FAControl.Common;
using FAControl.Models;

namespace FAControl.Data;

/// <summary>
/// Consultas del reporte "Ingresos por período". Solo lectura.
/// El día de negocio se obtiene restando 4 horas al UTC (RD, sin DST).
/// </summary>
public class ReporteRepository
{
    private readonly ConexionFactory _factory;

    public ReporteRepository(ConexionFactory factory) => _factory = factory;

    /// <summary>
    /// Cobros por día de negocio dentro del rango, con desglose interés/capital.
    /// Filtros opcionales por usuario que cobró y por cliente (cliente 2026-07-19).
    /// </summary>
    public async Task<IReadOnlyList<IngresoDiario>> ObtenerIngresosDiariosAsync(
        DateTime inicioUtc, DateTime finUtc, long? usuarioId = null, long? clienteId = null,
        CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        // El JOIN a cuota/prestamo solo hace falta para filtrar por cliente
        cmd.CommandText = $"""
            SELECT DATE(DATE_SUB(g.fecha_pago, INTERVAL 4 HOUR)) AS dia,
                   SUM(g.monto_interes) AS interes,
                   SUM(g.monto_capital) AS capital,
                   SUM(g.monto_pagado) AS total
            FROM {DbNames.Pago} g
            JOIN {DbNames.Cuota} q ON q.id = g.cuota_id
            JOIN {DbNames.Prestamo} p ON p.id = q.prestamo_id
            WHERE g.deleted_at IS NULL
              AND g.fecha_pago >= @inicio AND g.fecha_pago < @fin
              AND (@usuarioId IS NULL OR g.created_by = @usuarioId)
              AND (@clienteId IS NULL OR p.cliente_id = @clienteId)
            GROUP BY dia
            ORDER BY dia;
            """;
        cmd.Parameters.AddWithValue("@inicio", inicioUtc);
        cmd.Parameters.AddWithValue("@fin", finUtc);
        cmd.Parameters.AddWithValue("@usuarioId", (object?)usuarioId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@clienteId", (object?)clienteId ?? DBNull.Value);

        var dias = new List<IngresoDiario>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            dias.Add(new IngresoDiario(
                DateOnly.FromDateTime(reader.GetDateTime("dia")),
                reader.GetDecimal("interes"),
                reader.GetDecimal("capital"),
                reader.GetDecimal("total")));
        }
        return dias;
    }

    /// <summary>
    /// Cuotas cobradas (con al menos un abono en el rango) y cuotas programadas
    /// (vencen dentro del rango) — KPI "47 de 52 programadas" del mockup.
    /// </summary>
    public async Task<(int Cobradas, int Programadas)> ContarCuotasAsync(
        DateTime inicioUtc, DateTime finUtc, DateOnly desde, DateOnly hasta,
        long? usuarioId = null, long? clienteId = null, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        // Cobradas respeta usuario+cliente; programadas solo cliente (una cuota
        // programada no tiene "cobrador" hasta que se cobra).
        cmd.CommandText = $"""
            SELECT
              (SELECT COUNT(DISTINCT g.cuota_id)
               FROM {DbNames.Pago} g
               JOIN {DbNames.Cuota} q ON q.id = g.cuota_id
               JOIN {DbNames.Prestamo} p ON p.id = q.prestamo_id
               WHERE g.deleted_at IS NULL
                 AND g.fecha_pago >= @inicio AND g.fecha_pago < @fin
                 AND (@usuarioId IS NULL OR g.created_by = @usuarioId)
                 AND (@clienteId IS NULL OR p.cliente_id = @clienteId)) AS cobradas,
              (SELECT COUNT(*)
               FROM {DbNames.Cuota} q
               JOIN {DbNames.Prestamo} p ON p.id = q.prestamo_id
               WHERE p.estado <> 'cancelado'
                 AND q.estado <> 'cancelada'
                 AND (@clienteId IS NULL OR p.cliente_id = @clienteId)
                 AND q.fecha_vencimiento BETWEEN @desde AND @hasta) AS programadas;
            """;
        cmd.Parameters.AddWithValue("@inicio", inicioUtc);
        cmd.Parameters.AddWithValue("@fin", finUtc);
        cmd.Parameters.AddWithValue("@desde", desde.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@hasta", hasta.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@usuarioId", (object?)usuarioId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@clienteId", (object?)clienteId ?? DBNull.Value);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return (reader.GetInt32("cobradas"), reader.GetInt32("programadas"));
    }
}
