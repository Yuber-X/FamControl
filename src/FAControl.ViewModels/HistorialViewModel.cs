using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAControl.Common;
using FAControl.Models;
using FAControl.Services;
using Serilog;

namespace FAControl.ViewModels;

/// <summary>Fila del visor de auditoría.</summary>
public record AuditoriaFila(Auditoria Entrada)
{
    public string FechaTexto => FechaNegocio.AUtcLocal(Entrada.TimestampUtc).ToString("dd/MM/yyyy hh:mm:ss tt");
    public string AccionTexto => Entrada.Accion switch
    {
        AccionAuditoria.Crear => "Crear",
        AccionAuditoria.Modificar => "Modificar",
        AccionAuditoria.Eliminar => "Eliminar",
        AccionAuditoria.Consultar => "Consultar",
        AccionAuditoria.Login => "Inicio de sesión",
        AccionAuditoria.Logout => "Cierre de sesión",
        _ => Entrada.Accion.ToString()
    };
    public string EntidadTexto => Entrada.Entidad;
    public string EntidadIdTexto => Entrada.EntidadId?.ToString() ?? "—";
    public string DescripcionTexto => Entrada.Descripcion ?? "—";
}

/// <summary>
/// Historial: visor de solo lectura de la auditoría con filtros por fecha,
/// entidad y acción. Nada se puede editar ni borrar desde aquí.
/// </summary>
public partial class HistorialViewModel : ObservableObject
{
    private readonly AuditoriaService _auditoria;
    private readonly IDialogService _dialogos;

    public HistorialViewModel(AuditoriaService auditoria, IDialogService dialogos)
    {
        _auditoria = auditoria;
        _dialogos = dialogos;

        Entidades =
        [
            new Opcion<string?>(null, "Todas las entidades"),
            new Opcion<string?>(DbNames.Cliente, "Clientes"),
            new Opcion<string?>(DbNames.Prestamo, "Préstamos"),
            new Opcion<string?>(DbNames.Cuota, "Cuotas"),
            new Opcion<string?>(DbNames.Pago, "Pagos"),
            new Opcion<string?>(DbNames.Usuario, "Usuario")
        ];
        Acciones =
        [
            new Opcion<AccionAuditoria?>(null, "Todas las acciones"),
            new Opcion<AccionAuditoria?>(AccionAuditoria.Crear, "Crear"),
            new Opcion<AccionAuditoria?>(AccionAuditoria.Modificar, "Modificar"),
            new Opcion<AccionAuditoria?>(AccionAuditoria.Eliminar, "Eliminar"),
            new Opcion<AccionAuditoria?>(AccionAuditoria.Login, "Inicio de sesión"),
            new Opcion<AccionAuditoria?>(AccionAuditoria.Logout, "Cierre de sesión")
        ];
        _entidadSeleccionada = Entidades[0];
        _accionSeleccionada = Acciones[0];
    }

    public ObservableCollection<AuditoriaFila> Filas { get; } = [];
    public IReadOnlyList<Opcion<string?>> Entidades { get; }
    public IReadOnlyList<Opcion<AccionAuditoria?>> Acciones { get; }

    [ObservableProperty] private DateTime? _desde;
    [ObservableProperty] private DateTime? _hasta;
    [ObservableProperty] private Opcion<string?> _entidadSeleccionada;
    [ObservableProperty] private Opcion<AccionAuditoria?> _accionSeleccionada;
    [ObservableProperty] private string _contadorTexto = string.Empty;

    partial void OnEntidadSeleccionadaChanged(Opcion<string?> value) => _ = BuscarAsync();
    partial void OnAccionSeleccionadaChanged(Opcion<AccionAuditoria?> value) => _ = BuscarAsync();
    partial void OnDesdeChanged(DateTime? value) => _ = BuscarAsync();
    partial void OnHastaChanged(DateTime? value) => _ = BuscarAsync();

    public Task CargarAsync() => BuscarAsync();

    [RelayCommand]
    private async Task BuscarAsync()
    {
        try
        {
            var filtro = new FiltroAuditoria(
                Desde is { } d ? DateOnly.FromDateTime(d) : null,
                Hasta is { } h ? DateOnly.FromDateTime(h) : null,
                EntidadSeleccionada.Valor,
                AccionSeleccionada.Valor);

            var entradas = await _auditoria.BuscarAsync(filtro);
            Filas.Clear();
            foreach (var entrada in entradas)
                Filas.Add(new AuditoriaFila(entrada));

            ContadorTexto = entradas.Count >= filtro.Limite
                ? $"Mostrando los {filtro.Limite} registros más recientes (ajustá los filtros para ver más atrás)"
                : $"{entradas.Count} registro(s)";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cargando el historial de auditoría");
            _dialogos.MostrarError("Historial", $"No se pudo cargar el historial.\n\n{ex.Message}");
        }
    }

    [RelayCommand]
    private void LimpiarFiltros()
    {
        Desde = null;
        Hasta = null;
        EntidadSeleccionada = Entidades[0];
        AccionSeleccionada = Acciones[0];
    }
}
