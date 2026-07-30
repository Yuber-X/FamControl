// Portado de POS-500 el 2026-07-30 al integrar el punto de venta a la suite.
// Cambios respecto del original: usa ConexionPos500 (base pos500_db, aparte de
// facontrol_db) y el SesionActual / la auditoria compartidos de FAControl.
using FAControl.Common;
using FAControl.Data.Pos;
// Solo el enum de la auditoria compartida: importar todo FAControl.Models
// chocaria con Cliente/ClienteDatos, que en el POS son otra cosa.
using AccionAuditoria = FAControl.Models.AccionAuditoria;
using FAControl.Models.Pos;

namespace FAControl.Services.Pos;

/// <summary>
/// Reglas de negocio de clientes. Cedula OPCIONAL (retail): si viene, se
/// normaliza (000-0000000-0) y se exige única. Requiere permiso
/// clientes_editar para mutaciones (el Cajero solo consulta).
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

    public Task<List<Cliente>> ObtenerTodosAsync(CancellationToken ct = default) =>
        _clientes.ObtenerTodosAsync(ct);

    public Task<Cliente?> ObtenerPorIdAsync(long id, CancellationToken ct = default) =>
        _clientes.ObtenerPorIdAsync(id, ct);

    public async Task<long> CrearAsync(ClienteDatos datos, CancellationToken ct = default)
    {
        var limpios = await ValidarAsync(datos, exceptoId: null, ct);
        var id = await _clientes.InsertarAsync(limpios, ct);
        await _auditoria.RegistrarAsync(AccionAuditoria.Crear, DbNamesPos.Cliente, id,
            $"Cliente creado: {limpios.Nombre}", ct);
        return id;
    }

    public async Task ActualizarAsync(long id, ClienteDatos datos, CancellationToken ct = default)
    {
        var limpios = await ValidarAsync(datos, exceptoId: id, ct);
        await _clientes.ActualizarAsync(id, limpios, ct);
        await _auditoria.RegistrarAsync(AccionAuditoria.Modificar, DbNamesPos.Cliente, id,
            $"Cliente modificado: {limpios.Nombre}", ct);
    }

    public async Task EliminarAsync(long id, CancellationToken ct = default)
    {
        var cliente = await _clientes.ObtenerPorIdAsync(id, ct)
            ?? throw new InvalidOperationException("El cliente no existe o ya fue eliminado.");
        await _clientes.EliminarAsync(id, ct);
        await _auditoria.RegistrarAsync(AccionAuditoria.Eliminar, DbNamesPos.Cliente, id,
            $"Cliente eliminado: {cliente.Nombre}", ct);
    }

    private async Task<ClienteDatos> ValidarAsync(ClienteDatos datos, long? exceptoId, CancellationToken ct)
    {
        ValidarPermisoEdicion();

        if (string.IsNullOrWhiteSpace(datos.Nombre))
            throw new ArgumentException("El nombre del cliente es obligatorio.");

        var cedula = string.IsNullOrWhiteSpace(datos.Cedula) ? null : NormalizarCedula(datos.Cedula);
        if (cedula is not null && await _clientes.ExisteCedulaAsync(cedula, exceptoId, ct))
            throw new ArgumentException($"Ya existe un cliente con la cédula {cedula}.");

        return datos with
        {
            Cedula = cedula,
            Nombre = datos.Nombre.Trim(),
            Telefono = Limpiar(datos.Telefono),
            Direccion = Limpiar(datos.Direccion),
            Notas = Limpiar(datos.Notas)
        };
    }

    private static void ValidarPermisoEdicion()
    {
        if (!SesionActual.TienePermiso("clientes_editar"))
            throw new InvalidOperationException("No tienes permiso para crear o editar clientes.");
    }

    private static string? Limpiar(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    /// <summary>
    /// 11 dígitos → formato 000-0000000-0; otra cosa (pasaporte) se acepta
    /// tal cual hasta 20 caracteres. Patrón heredado de PrestControl.
    /// </summary>
    public static string NormalizarCedula(string cedula)
    {
        var limpia = cedula.Trim();
        var digitos = new string(limpia.Where(char.IsDigit).ToArray());

        // Cédula dominicana: 11 dígitos (admitiendo espacios/guiones de relleno).
        // Si hay letras u otros símbolos es un pasaporte y se respeta tal cual.
        var soloRelleno = limpia.All(c => char.IsDigit(c) || c == '-' || c == ' ');
        if (digitos.Length == 11 && soloRelleno)
            return $"{digitos[..3]}-{digitos[3..10]}-{digitos[10..]}";

        if (limpia.Length > 20)
            throw new ArgumentException("La cédula o pasaporte no puede superar 20 caracteres.");
        return limpia;
    }
}
