using FAControl.Common;
using FAControl.Data;
using FAControl.Models;

namespace FAControl.Services;

/// <summary>
/// Reporte "Ingresos por período" (mockup Reportes): totales del rango,
/// cuotas cobradas vs programadas y desglose por semanas de 7 días
/// contadas desde la fecha inicial.
/// </summary>
public class ReporteService
{
    /// <summary>RD es UTC-4 fijo (sin horario de verano).</summary>
    private const int OffsetRdHoras = 4;

    private readonly ReporteRepository _repositorio;
    private readonly UsuarioRepository _usuarios;
    private readonly ClienteRepository _clientes;

    public ReporteService(ReporteRepository repositorio, UsuarioRepository usuarios,
        ClienteRepository clientes)
    {
        _repositorio = repositorio;
        _usuarios = usuarios;
        _clientes = clientes;
    }

    /// <summary>Usuarios para el filtro del reporte (todos, sin puerta de Admin).</summary>
    public Task<IReadOnlyList<Usuario>> ObtenerUsuariosAsync(CancellationToken ct = default) =>
        _usuarios.ObtenerTodosAsync(ct);

    /// <summary>Clientes activos (del modo activo) para el filtro del reporte.</summary>
    public Task<IReadOnlyList<Cliente>> ObtenerClientesAsync(CancellationToken ct = default) =>
        _clientes.ObtenerActivosAsync(SesionActual.Modo, ct);

    /// <summary>
    /// Totales por cliente en el período (para imprimir el reporte individual
    /// o global). Respeta los mismos filtros que el reporte en pantalla.
    /// </summary>
    public Task<IReadOnlyList<ReporteCliente>> ObtenerPorClienteAsync(DateOnly desde, DateOnly hasta,
        long? usuarioId = null, long? clienteId = null, CancellationToken ct = default)
    {
        if (hasta < desde)
            throw new ArgumentException("La fecha final no puede ser anterior a la inicial.");
        var inicioUtc = desde.ToDateTime(TimeOnly.MinValue).AddHours(OffsetRdHoras);
        var finUtc = hasta.AddDays(1).ToDateTime(TimeOnly.MinValue).AddHours(OffsetRdHoras);
        return _repositorio.ObtenerPorClienteAsync(inicioUtc, finUtc, usuarioId, clienteId,
            SesionActual.SoloVehicularesDelModo, ct);
    }

    public async Task<ReporteIngresos> ObtenerIngresosAsync(DateOnly desde, DateOnly hasta,
        long? usuarioId = null, long? clienteId = null, CancellationToken ct = default)
    {
        if (hasta < desde)
            throw new ArgumentException("La fecha final no puede ser anterior a la inicial.");

        // Rango local [desde, hasta] → instantes UTC [inicio, fin)
        var inicioUtc = desde.ToDateTime(TimeOnly.MinValue).AddHours(OffsetRdHoras);
        var finUtc = hasta.AddDays(1).ToDateTime(TimeOnly.MinValue).AddHours(OffsetRdHoras);

        var soloVehiculares = SesionActual.SoloVehicularesDelModo;
        var porDia = await _repositorio.ObtenerIngresosDiariosAsync(
            inicioUtc, finUtc, usuarioId, clienteId, soloVehiculares, ct);
        var (cobradas, programadas) = await _repositorio.ContarCuotasAsync(
            inicioUtc, finUtc, desde, hasta, usuarioId, clienteId, soloVehiculares, ct);

        return new ReporteIngresos(
            desde, hasta,
            porDia.Sum(d => d.Interes),
            porDia.Sum(d => d.Capital),
            porDia.Sum(d => d.Total),
            cobradas, programadas,
            porDia,
            AgruparPorSemana(porDia, desde, hasta));
    }

    /// <summary>Buckets de 7 días desde la fecha inicial (Sem. 1, Sem. 2, ...).</summary>
    public static List<IngresoSemanal> AgruparPorSemana(IReadOnlyList<IngresoDiario> porDia,
        DateOnly desde, DateOnly hasta)
    {
        var semanas = new List<IngresoSemanal>();
        var numero = 1;
        for (var inicio = desde; inicio <= hasta; inicio = inicio.AddDays(7))
        {
            var fin = inicio.AddDays(6) < hasta ? inicio.AddDays(6) : hasta;
            var delRango = porDia.Where(d => d.Fecha >= inicio && d.Fecha <= fin).ToList();
            semanas.Add(new IngresoSemanal(
                numero++, inicio, fin,
                delRango.Sum(d => d.Capital),
                delRango.Sum(d => d.Interes)));
        }
        return semanas;
    }
}
