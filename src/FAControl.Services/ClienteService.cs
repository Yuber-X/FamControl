using FAControl.Common;
using FAControl.Data;
using FAControl.Models;
using Serilog;

namespace FAControl.Services;

/// <summary>
/// Lógica de negocio de clientes: validación de datos, unicidad de cédula,
/// soft delete protegido (no se elimina un cliente con préstamos activos)
/// y auditoría de toda mutación.
/// </summary>
public class ClienteService
{
    private readonly ClienteRepository _clientes;
    private readonly AuditoriaService _auditoria;

    public ClienteService(ClienteRepository clientes, AuditoriaService auditoria)
    {
        _clientes = clientes;
        _auditoria = auditoria;
    }

    // ---------- Lecturas ----------

    // Todas las lecturas se AÍSLAN al modo activo de la sesión (SesionActual.Modo):
    // un cliente de PrestControl nunca aparece al operar en Dealer/Auto y viceversa.

    public Task<IReadOnlyList<Cliente>> ObtenerActivosAsync(CancellationToken ct = default) =>
        _clientes.ObtenerActivosAsync(SesionActual.Modo, ct);

    public Task<Cliente?> ObtenerPorIdAsync(long id, CancellationToken ct = default) =>
        _clientes.ObtenerPorIdAsync(id, ct);

    public Task<IReadOnlyList<ClienteResumen>> ObtenerResumenesAsync(CancellationToken ct = default) =>
        _clientes.ObtenerResumenesAsync(SesionActual.Modo, ct);

    public Task<ClienteMetricas> ObtenerMetricasAsync(long clienteId, CancellationToken ct = default) =>
        _clientes.ObtenerMetricasAsync(clienteId, FechaNegocio.Hoy, ct);

    /// <summary>Historial de buena conducta del cliente (pedido 2026-08-06).</summary>
    public Task<ClienteConducta> ObtenerConductaAsync(long clienteId, CancellationToken ct = default) =>
        _clientes.ObtenerConductaAsync(clienteId, FechaNegocio.Hoy, ct);

    /// <summary>Clientes que se pasaron de su fecha de pago (notificador de vencimientos).</summary>
    public Task<IReadOnlyList<ClienteVencido>> ObtenerClientesConVencidasAsync(CancellationToken ct = default) =>
        _clientes.ObtenerClientesConVencidasAsync(FechaNegocio.Hoy, SesionActual.Modo, ct);

    // ---------- Mutaciones (con auditoría) ----------

    public async Task<long> CrearAsync(ClienteDatos datos, CancellationToken ct = default)
    {
        var normalizados = await ValidarAsync(datos, excluirId: null, ct);
        var id = await _clientes.CrearAsync(normalizados, SesionActual.Modo, ct);
        await _auditoria.RegistrarAsync(AccionAuditoria.Crear, DbNames.Cliente, id,
            $"Cliente {normalizados.Nombre} {normalizados.Apellido} ({normalizados.Cedula})", ct);
        Log.Information("Cliente {Id} creado: {Nombre} {Apellido}", id, normalizados.Nombre, normalizados.Apellido);
        return id;
    }

    public async Task ActualizarAsync(long id, ClienteDatos datos, CancellationToken ct = default)
    {
        var existente = await _clientes.ObtenerPorIdAsync(id, ct)
            ?? throw new InvalidOperationException("El cliente no existe o fue eliminado.");

        var normalizados = await ValidarAsync(datos, excluirId: id, ct);
        await _clientes.ActualizarAsync(id, normalizados, ct);
        await _auditoria.RegistrarAsync(AccionAuditoria.Modificar, DbNames.Cliente, id,
            $"Cliente actualizado: {existente.NombreCompleto} → {normalizados.Nombre} {normalizados.Apellido} ({normalizados.Cedula})", ct);
        Log.Information("Cliente {Id} actualizado", id);
    }

    /// <summary>Soft delete. Bloqueado si el cliente tiene préstamos activos.</summary>
    public async Task EliminarAsync(long id, CancellationToken ct = default)
    {
        var cliente = await _clientes.ObtenerPorIdAsync(id, ct)
            ?? throw new InvalidOperationException("El cliente no existe o ya fue eliminado.");

        var activos = await _clientes.ContarPrestamosActivosAsync(id, ct);
        if (activos > 0)
            throw new InvalidOperationException(
                $"{cliente.NombreCompleto} tiene {activos} préstamo(s) activo(s). " +
                "Cobrá o cancelá sus préstamos antes de eliminarlo.");

        await _clientes.EliminarAsync(id, ct);
        await _auditoria.RegistrarAsync(AccionAuditoria.Eliminar, DbNames.Cliente, id,
            $"Cliente eliminado (soft delete): {cliente.NombreCompleto} ({cliente.Cedula})", ct);
        Log.Information("Cliente {Id} eliminado (soft delete)", id);
    }

    // ---------- Validación ----------

    /// <summary>
    /// Valida y normaliza los datos del formulario. La cédula dominicana
    /// (11 dígitos) se formatea a 000-0000000-0; otros documentos (pasaporte)
    /// se aceptan tal cual hasta 13 caracteres.
    /// </summary>
    public async Task<ClienteDatos> ValidarAsync(ClienteDatos datos, long? excluirId, CancellationToken ct = default)
    {
        var nombre = datos.Nombre.Trim();
        var apellido = datos.Apellido.Trim();
        if (nombre.Length == 0)
            throw new ArgumentException("El nombre es obligatorio.");
        if (apellido.Length == 0)
            throw new ArgumentException("El apellido es obligatorio.");

        var cedula = NormalizarCedula(datos.Cedula);
        if (await _clientes.ExisteCedulaAsync(cedula, SesionActual.Modo, excluirId, ct))
            throw new ArgumentException($"Ya existe un cliente con la cédula {cedula} en esta estancia.");

        return datos with
        {
            Cedula = cedula,
            Nombre = nombre,
            Apellido = apellido,
            Telefono = Limpiar(datos.Telefono),
            Direccion = Limpiar(datos.Direccion),
            Email = Limpiar(datos.Email),
            Notas = Limpiar(datos.Notas)
        };
    }

    /// <summary>11 dígitos (con o sin guiones) → 000-0000000-0; otros documentos, tal cual.</summary>
    public static string NormalizarCedula(string cedula)
    {
        var texto = cedula.Trim();
        if (texto.Length == 0)
            throw new ArgumentException("La cédula es obligatoria.");

        var compacto = texto.Replace("-", string.Empty).Replace(" ", string.Empty);
        if (compacto.Length == 11 && compacto.All(char.IsAsciiDigit))
            return $"{compacto[..3]}-{compacto[3..10]}-{compacto[10..]}";

        if (texto.Length > 13)
            throw new ArgumentException("El documento no puede superar 13 caracteres.");
        return texto;
    }

    private static string? Limpiar(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}
