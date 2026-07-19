using System.Globalization;
using System.Text;
using FAControl.Common;
using FAControl.Data;
using FAControl.Models;
using Serilog;

namespace FAControl.Services;

/// <summary>Resultado de una tanda de recordatorios.</summary>
public record ResultadoRecordatorios(
    int ClientesConCuotas,
    int CorreosACliente,
    int SinEmail,
    bool ResumenAlDueno,
    string Detalle);

/// <summary>
/// Recordatorios de cuota por correo (cliente 2026-07-19). Envía DOS cosas:
///  - a cada CLIENTE con cuota por vencer/vencida y email: su recordatorio;
///  - al DUEÑO: un resumen con todos.
/// Nunca tumba la app: los fallos se registran y se informan en el resultado.
/// </summary>
public class RecordatorioService
{
    private static readonly CultureInfo CulturaRd = CultureInfo.GetCultureInfo("es-DO");

    private readonly ClienteRepository _clientes;
    private readonly EmailService _email;
    private readonly AjustesLocales _ajustes;

    public RecordatorioService(ClienteRepository clientes, EmailService email, AjustesLocales ajustes)
    {
        _clientes = clientes;
        _email = email;
        _ajustes = ajustes;
    }

    /// <summary>
    /// Envía los recordatorios. Devuelve un resumen de qué se hizo.
    /// El llamador valida antes que el correo esté configurado.
    /// </summary>
    public async Task<ResultadoRecordatorios> EnviarAsync(CancellationToken ct = default)
    {
        if (!_email.EstaConfigurado)
            throw new InvalidOperationException(
                "El correo no está configurado. Completá la cuenta de Gmail en Configuración.");

        var hoy = FechaNegocio.Hoy;
        var clientes = await _clientes.ObtenerRecordatoriosAsync(
            hoy, _ajustes.RecordatorioDiasAntes, SesionActual.Modo, ct);

        var enviados = 0;
        var sinEmail = 0;
        var errores = new StringBuilder();

        foreach (var cliente in clientes)
        {
            if (string.IsNullOrWhiteSpace(cliente.Email))
            {
                sinEmail++;
                continue;
            }
            try
            {
                await _email.EnviarAsync(cliente.Email!,
                    AsuntoCliente(cliente), CuerpoCliente(cliente, hoy), ct);
                enviados++;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "No se pudo enviar el recordatorio a {Cliente}", cliente.NombreCompleto);
                errores.AppendLine($"• {cliente.NombreCompleto}: {ex.Message}");
            }
        }

        // Resumen al dueño (si hay correo del dueño y hubo clientes)
        var resumenEnviado = false;
        if (!string.IsNullOrWhiteSpace(_ajustes.CorreoDueno) && clientes.Count > 0)
        {
            try
            {
                await _email.EnviarAsync(_ajustes.CorreoDueno,
                    $"Cuotas por vencer — {clientes.Count} cliente(s)", CuerpoDueno(clientes, hoy), ct);
                resumenEnviado = true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "No se pudo enviar el resumen al dueño");
                errores.AppendLine($"• Resumen al dueño: {ex.Message}");
            }
        }

        _ajustes.UltimoRecordatorioUtc = DateTime.UtcNow;
        _ajustes.Guardar();

        var detalle = errores.Length > 0
            ? "Con errores:\n" + errores
            : "Todo enviado correctamente.";
        return new ResultadoRecordatorios(clientes.Count, enviados, sinEmail, resumenEnviado, detalle);
    }

    /// <summary>
    /// Envía el recordatorio a UN cliente puntual (botón en el detalle del
    /// préstamo). Devuelve un mensaje para mostrarle al usuario. No lanza por
    /// falta de datos: informa amablemente (sin correo, sin cuotas por vencer…).
    /// </summary>
    public async Task<string> EnviarAClienteAsync(long clienteId, CancellationToken ct = default)
    {
        if (!_email.EstaConfigurado)
            throw new InvalidOperationException(
                "El correo no está configurado. Completá la cuenta de Gmail en Configuración.");

        var hoy = FechaNegocio.Hoy;
        var clientes = await _clientes.ObtenerRecordatoriosAsync(
            hoy, _ajustes.RecordatorioDiasAntes, SesionActual.Modo, ct);
        var cliente = clientes.FirstOrDefault(c => c.ClienteId == clienteId);

        if (cliente is null)
            return "Este cliente no tiene cuotas por vencer ni vencidas en la ventana configurada.";
        if (string.IsNullOrWhiteSpace(cliente.Email))
            return $"{cliente.NombreCompleto} no tiene correo registrado. Agregalo en su ficha.";

        await _email.EnviarAsync(cliente.Email!, AsuntoCliente(cliente), CuerpoCliente(cliente, hoy), ct);
        _ajustes.UltimoRecordatorioUtc = DateTime.UtcNow;
        _ajustes.Guardar();
        return $"Recordatorio enviado a {cliente.Email}.";
    }

    /// <summary>Envío automático al arrancar (una vez por día), si está activo.</summary>
    public async Task EjecutarAutomaticoSiTocaAsync()
    {
        try
        {
            if (!_ajustes.RecordatoriosAutomaticos || !_email.EstaConfigurado)
                return;
            // Una vez por día de negocio
            if (_ajustes.UltimoRecordatorioUtc is { } ultimo &&
                (DateTime.UtcNow - ultimo).TotalHours < 20)
                return;

            var r = await EnviarAsync();
            Log.Information("Recordatorios automáticos: {Enviados} a clientes, resumen dueño={Resumen}",
                r.CorreosACliente, r.ResumenAlDueno);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Falló el envío automático de recordatorios");
        }
    }

    private static string AsuntoCliente(RecordatorioCliente c) =>
        c.HayVencidas ? "Tu cuota está vencida — Familia Almonte" : "Recordatorio de pago — Familia Almonte";

    private string CuerpoCliente(RecordatorioCliente c, DateOnly hoy)
    {
        var estado = c.HayVencidas
            ? $"tenés cuota(s) VENCIDA(S) desde el {c.ProximoVencimiento.ToString(@"dd'/'MM'/'yyyy", CulturaRd)}"
            : $"tu próxima cuota vence el {c.ProximoVencimiento.ToString(@"dd'/'MM'/'yyyy", CulturaRd)}";

        return $"""
            Hola {c.NombreCompleto},

            Te recordamos que {estado}.
            Monto pendiente: RD$ {c.MontoPendiente.ToString("N2", CulturaRd)}.

            Por favor acercate a pagar a la brevedad para mantener tu préstamo al día.
            Si ya pagaste, ignorá este mensaje.

            Gracias,
            {_ajustes.NombreNegocio}
            {_ajustes.TelefonoNegocio}
            """;
    }

    private string CuerpoDueno(IReadOnlyList<RecordatorioCliente> clientes, DateOnly hoy)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Resumen de cuotas por vencer o vencidas al {hoy.ToString(@"dd'/'MM'/'yyyy", CulturaRd)}:");
        sb.AppendLine();
        foreach (var c in clientes.OrderByDescending(x => x.HayVencidas).ThenBy(x => x.ProximoVencimiento))
        {
            var marca = c.HayVencidas ? "[VENCIDA]" : "[por vencer]";
            var email = string.IsNullOrWhiteSpace(c.Email) ? "sin email" : c.Email;
            sb.AppendLine($"{marca} {c.NombreCompleto} — vence {c.ProximoVencimiento.ToString(@"dd'/'MM'/'yyyy", CulturaRd)} — " +
                          $"RD$ {c.MontoPendiente.ToString("N2", CulturaRd)} — {email}");
        }
        sb.AppendLine();
        sb.AppendLine($"Total: {clientes.Count} cliente(s).");
        return sb.ToString();
    }
}
