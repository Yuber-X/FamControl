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
    // Separadores ESCAPADOS ('/'): sin escapar, "/" es un comodín que .NET
    // reemplaza por el separador de la cultura de Windows, y la misma app
    // mostraría 17/07 en un binding XAML y 17-07 acá. Ver Textos.FormatoFechaHora.
    public string FechaTexto =>
        FechaNegocio.AUtcLocal(Entrada.TimestampUtc).ToString(Textos.FormatoFechaHoraSegundos, Textos.CulturaRd);
    /// <summary>Quién hizo la acción (multiusuario, pedido del cliente 2026-07-16).</summary>
    public string UsuarioTexto => Entrada.UsuarioNombre;
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
    /// <summary>
    /// Estancia donde se hizo (025). Las lineas anteriores a esa migracion no la
    /// tienen: se muestran con guion en vez de inventarles una.
    /// </summary>
    public string ModoTexto => Entrada.Modo is { } clave
        && IdentidadModo.Todos.FirstOrDefault(m => m.Modo.ClaveDb() == clave) is { } identidad
            ? identidad.Nombre
            : "—";

    public string EntidadTexto => Entrada.Entidad;
    public string EntidadIdTexto => Entrada.EntidadId?.ToString() ?? "—";
    public string DescripcionTexto => Entrada.Descripcion ?? "—";
}

/// <summary>Fila del panel de actividad: un usuario y su tiempo en el sistema.</summary>
public record ActividadFila(ActividadUsuario Actividad)
{
    public string Nombre => Actividad.Nombre;
    public string RolTexto => Actividad.RolNombre;
    public string TiempoActivoTexto => Actividad.TiempoActivoTexto;
    public int Sesiones => Actividad.Sesiones;
    public int Operaciones => Actividad.Operaciones;
    public bool EnLinea => Actividad.EnLinea;
    public string EstadoTexto => Actividad.EnLinea ? "En línea" : "Desconectado";
    public string UltimoAccesoTexto => Actividad.UltimoAccesoUtc is { } fecha
        ? FechaNegocio.AUtcLocal(fecha).ToString(Textos.FormatoFechaHora, Textos.CulturaRd)
        : "—";
}

/// <summary>
/// Historial: visor de solo lectura de la auditoría con filtros por fecha,
/// entidad, acción y USUARIO. Nada se puede editar ni borrar desde aquí.
///
/// Además muestra la actividad por usuario (sesiones y tiempo activo) del
/// mismo rango — pedido del cliente 2026-07-16.
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
            new Opcion<string?>(DbNames.Usuario, "Usuario"),
            // Punto de venta (024): sus tablas van con prefijo pos_
            new Opcion<string?>(DbNamesPos.Producto, "Productos (POS)"),
            new Opcion<string?>(DbNamesPos.Factura, "Facturas (POS)"),
            new Opcion<string?>(DbNamesPos.CuadreCaja, "Cuadre de caja (POS)")
        ];
        // Estancias (025). El historial arranca acotado a la estancia activa
        // —que es lo que el usuario está mirando— y desde acá se puede abrir
        // a todas cuando hace falta ver el conjunto.
        Modos =
        [
            new Opcion<string?>(null, "Todos los modos"),
            .. IdentidadModo.Todos.Select(m => new Opcion<string?>(m.Modo.ClaveDb(), m.Nombre))
        ];
        Acciones =
        [
            new Opcion<AccionAuditoria?>(null, "Todas las acciones"),
            new Opcion<AccionAuditoria?>(AccionAuditoria.Crear, "Crear"),
            new Opcion<AccionAuditoria?>(AccionAuditoria.Modificar, "Modificar"),
            new Opcion<AccionAuditoria?>(AccionAuditoria.Eliminar, "Eliminar"),
            new Opcion<AccionAuditoria?>(AccionAuditoria.Login, "Inicio de sesión"),
            new Opcion<AccionAuditoria?>(AccionAuditoria.Logout, "Cierre de sesión"),
            new Opcion<AccionAuditoria?>(AccionAuditoria.Anular, "Anular")
        ];
        _entidadSeleccionada = Entidades[0];
        _modoSeleccionado = Modos[0];
        _accionSeleccionada = Acciones[0];
        _usuarioSeleccionado = new Opcion<long?>(null, "Todos los usuarios");
        Usuarios.Add(_usuarioSeleccionado);
    }

    /// <summary>
    /// Acota el historial a la estancia activa. Lo llama el shell al entrar al
    /// modo: lo primero que el usuario ve es lo que pasó DONDE está parado, y si
    /// quiere el conjunto lo abre desde el filtro.
    /// </summary>
    public void SincronizarModo()
    {
        var clave = SesionActual.Modo.ClaveDb();
        ModoSeleccionado = Modos.FirstOrDefault(m => m.Valor == clave) ?? Modos[0];
    }

    public ObservableCollection<AuditoriaFila> Filas { get; } = [];
    /// <summary>Actividad por usuario del rango consultado (sesiones + tiempo activo).</summary>
    public ObservableCollection<ActividadFila> Actividad { get; } = [];
    public ObservableCollection<Opcion<long?>> Usuarios { get; } = [];
    public IReadOnlyList<Opcion<string?>> Entidades { get; }
    public IReadOnlyList<Opcion<AccionAuditoria?>> Acciones { get; }
    public IReadOnlyList<Opcion<string?>> Modos { get; }

    [ObservableProperty] private DateTime? _desde;
    [ObservableProperty] private DateTime? _hasta;
    [ObservableProperty] private Opcion<string?> _entidadSeleccionada;
    [ObservableProperty] private Opcion<AccionAuditoria?> _accionSeleccionada;
    [ObservableProperty] private Opcion<long?> _usuarioSeleccionado;
    [ObservableProperty] private Opcion<string?> _modoSeleccionado;
    [ObservableProperty] private string _contadorTexto = string.Empty;
    [ObservableProperty] private string _actividadTexto = string.Empty;

    partial void OnEntidadSeleccionadaChanged(Opcion<string?> value) => _ = BuscarAsync();
    partial void OnAccionSeleccionadaChanged(Opcion<AccionAuditoria?> value) => _ = BuscarAsync();
    partial void OnUsuarioSeleccionadoChanged(Opcion<long?> value) => _ = BuscarAsync();
    partial void OnModoSeleccionadoChanged(Opcion<string?> value) => _ = BuscarAsync();
    partial void OnDesdeChanged(DateTime? value) => _ = BuscarAsync();
    partial void OnHastaChanged(DateTime? value) => _ = BuscarAsync();

    public async Task CargarAsync()
    {
        await CargarUsuariosAsync();
        await BuscarAsync();
    }

    /// <summary>Llena el combo de usuarios una sola vez.</summary>
    private async Task CargarUsuariosAsync()
    {
        if (Usuarios.Count > 1)
            return;

        try
        {
            foreach (var usuario in await _auditoria.ObtenerUsuariosAsync())
                Usuarios.Add(new Opcion<long?>(usuario.Id, usuario.NombreCompleto));
        }
        catch (Exception ex)
        {
            // El filtro por usuario es un extra: si falla, el historial igual sirve
            Log.Warning(ex, "No se pudo cargar la lista de usuarios del filtro");
        }
    }

    [RelayCommand]
    private async Task BuscarAsync()
    {
        try
        {
            var desde = Desde is { } d ? DateOnly.FromDateTime(d) : (DateOnly?)null;
            var hasta = Hasta is { } h ? DateOnly.FromDateTime(h) : (DateOnly?)null;

            var filtro = new FiltroAuditoria(desde, hasta,
                EntidadSeleccionada.Valor, AccionSeleccionada.Valor, UsuarioSeleccionado.Valor,
                ModoSeleccionado.Valor);

            var entradas = await _auditoria.BuscarAsync(filtro);
            Filas.Clear();
            foreach (var entrada in entradas)
                Filas.Add(new AuditoriaFila(entrada));

            ContadorTexto = entradas.Count >= filtro.Limite
                ? $"Mostrando los {filtro.Limite} registros más recientes (ajustá los filtros para ver más atrás)"
                : $"{entradas.Count} registro(s)";

            await CargarActividadAsync(desde, hasta);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cargando el historial de auditoría");
            _dialogos.MostrarError("Historial", $"No se pudo cargar el historial.\n\n{ex.Message}");
        }
    }

    /// <summary>
    /// Actividad del MISMO rango de fechas que el listado. No se filtra por
    /// usuario a propósito: el panel sirve para comparar entre empleados.
    /// </summary>
    private async Task CargarActividadAsync(DateOnly? desde, DateOnly? hasta)
    {
        try
        {
            var actividad = await _auditoria.ObtenerActividadAsync(desde, hasta);
            Actividad.Clear();
            foreach (var fila in actividad)
                Actividad.Add(new ActividadFila(fila));

            var enLinea = actividad.Count(a => a.EnLinea);
            ActividadTexto = actividad.Count == 0
                ? "Sin sesiones registradas en este rango."
                : $"{actividad.Count} usuario(s) con actividad" +
                  (enLinea > 0 ? $" · {enLinea} en línea ahora" : string.Empty);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cargando la actividad de usuarios");
            ActividadTexto = "No se pudo calcular la actividad.";
        }
    }

    [RelayCommand]
    private void LimpiarFiltros()
    {
        Desde = null;
        Hasta = null;
        EntidadSeleccionada = Entidades[0];
        SincronizarModo();
        AccionSeleccionada = Acciones[0];
        UsuarioSeleccionado = Usuarios[0];
    }
}
