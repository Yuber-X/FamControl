using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAControl.Common;
using FAControl.Models;
using FAControl.Services;
using Serilog;

namespace FAControl.ViewModels;

/// <summary>Fila de la lista de usuarios.</summary>
public record UsuarioFila(Usuario Usuario)
{
    public long Id => Usuario.Id;
    public string Username => Usuario.Username;
    public string NombreCompleto => Usuario.NombreCompleto;
    public string RolTexto => Usuario.RolNombre == Roles.Admin ? "Administrador"
        : string.IsNullOrEmpty(Usuario.RolNombre) ? "Por modo" : Usuario.RolNombre;
    /// <summary>Cuenta del desarrollador (017): solo la ve otro Programador.</summary>
    public bool EsCuentaProgramador => Usuario.RolNombre == Roles.Programador;
    public string EstadoTexto => Usuario.Activo ? "Activo" : "Inactivo";
    public string UltimoAccesoTexto => Usuario.LastLoginAtUtc is { } fecha
        ? FechaNegocio.AUtcLocal(fecha).ToString(Textos.FormatoFechaHora, Textos.CulturaRd)
        : "Nunca";
    /// <summary>El usuario de la sesión actual: la UI lo marca para evitar accidentes.</summary>
    public bool EsElActual => Usuario.Id == SesionActual.Id;
}

/// <summary>Checkbox de un permiso por pantalla (013, cliente 2026-07-25).</summary>
public partial class PermisoCheck : ObservableObject
{
    public PermisoCheck(Permiso permiso, bool marcado)
    {
        Id = permiso.Id;
        Nombre = permiso.Nombre;
        Descripcion = permiso.Descripcion ?? permiso.Nombre;
        _marcado = marcado;
    }

    public int Id { get; }
    public string Nombre { get; }
    public string Descripcion { get; }
    [ObservableProperty] private bool _marcado;
}

/// <summary>
/// Admin de Usuarios — SOLO Admin. Modelo de ROLES POR MODO (Yuber 2026-07-18):
/// se marca "administrador" (acceso global) o se elige UN rol por cada modo
/// (PrestControl / DealControl / AutoControl), con opción "Sin acceso".
/// PERMISOS POR PANTALLA (013, cliente 2026-07-25): el rol elegido precarga
/// los checkboxes de ese modo y el Admin ajusta fino; lo guardado es el set
/// marcado, nunca mezclado entre modos.
/// </summary>
public partial class UsuariosViewModel : ObservableObject
{
    private readonly UsuarioService _usuarios;
    private readonly IDialogService _dialogos;

    private long? _editandoId;          // null = alta nueva

    public event Action? PasswordDebeLimpiarse;

    public UsuariosViewModel(UsuarioService usuarios, IDialogService dialogos)
    {
        _usuarios = usuarios;
        _dialogos = dialogos;
    }

    public ObservableCollection<UsuarioFila> Filas { get; } = [];
    public ObservableCollection<Opcion<int?>> RolesPrest { get; } = [];
    public ObservableCollection<Opcion<int?>> RolesDealer { get; } = [];
    public ObservableCollection<Opcion<int?>> RolesAuto { get; } = [];
    // Permisos por pantalla de cada modo (013): catálogo fijo, se marca/desmarca
    public ObservableCollection<PermisoCheck> PermisosPrest { get; } = [];
    public ObservableCollection<PermisoCheck> PermisosDealer { get; } = [];
    public ObservableCollection<PermisoCheck> PermisosAuto { get; } = [];

    /// <summary>Evita recargar defaults del rol mientras se abre un usuario existente.</summary>
    private bool _cargandoUsuario;

    [ObservableProperty] private bool _formularioVisible;
    [ObservableProperty] private string _tituloFormulario = "Nuevo usuario";
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _nombre = string.Empty;
    [ObservableProperty] private string _apellido = string.Empty;
    [ObservableProperty] private bool _activo = true;
    [ObservableProperty] private bool _esNuevo = true;
    [ObservableProperty] private string _mensajeError = string.Empty;
    [ObservableProperty] private string _mensajeExito = string.Empty;
    [ObservableProperty] private bool _ocupado;

    /// <summary>Administrador global: entra a todo. Al marcarlo, los roles por modo se ignoran.</summary>
    [ObservableProperty] private bool _esAdministrador;

    /// <summary>
    /// Rol PROGRAMADOR (017): autoridad total e intocable. La casilla solo se
    /// MUESTRA si quien está logueado ya es Programador; el servicio vuelve a
    /// verificarlo, así que la UI es comodidad, no la barrera.
    /// </summary>
    [ObservableProperty] private bool _esProgramador;

    /// <summary>Solo un Programador ve y marca la casilla de Programador.</summary>
    public bool PuedeAsignarProgramador => SesionActual.EsProgramador;

    [ObservableProperty] private Opcion<int?>? _rolPrestSeleccionado;
    [ObservableProperty] private Opcion<int?>? _rolDealerSeleccionado;
    [ObservableProperty] private Opcion<int?>? _rolAutoSeleccionado;

    /// <summary>True cuando NO es un rol global: habilita los selectores por modo.</summary>
    public bool RolesPorModoHabilitados => !EsAdministrador && !EsProgramador;
    partial void OnEsAdministradorChanged(bool value) => RefrescarHabilitados();
    partial void OnEsProgramadorChanged(bool value) => RefrescarHabilitados();

    private void RefrescarHabilitados()
    {
        OnPropertyChanged(nameof(RolesPorModoHabilitados));
        OnPropertyChanged(nameof(PermisosPrestHabilitados));
        OnPropertyChanged(nameof(PermisosDealerHabilitados));
        OnPropertyChanged(nameof(PermisosAutoHabilitados));
    }

    // Los checkboxes de un modo se habilitan solo con un rol elegido (≠ Sin acceso)
    public bool PermisosPrestHabilitados => RolesPorModoHabilitados && RolPrestSeleccionado?.Valor is not null;
    public bool PermisosDealerHabilitados => RolesPorModoHabilitados && RolDealerSeleccionado?.Valor is not null;
    public bool PermisosAutoHabilitados => RolesPorModoHabilitados && RolAutoSeleccionado?.Valor is not null;

    partial void OnRolPrestSeleccionadoChanged(Opcion<int?>? value)
    {
        OnPropertyChanged(nameof(PermisosPrestHabilitados));
        if (!_cargandoUsuario)
            _ = PrecargarPermisosDeRolAsync(PermisosPrest, value?.Valor);
    }

    partial void OnRolDealerSeleccionadoChanged(Opcion<int?>? value)
    {
        OnPropertyChanged(nameof(PermisosDealerHabilitados));
        if (!_cargandoUsuario)
            _ = PrecargarPermisosDeRolAsync(PermisosDealer, value?.Valor);
    }

    partial void OnRolAutoSeleccionadoChanged(Opcion<int?>? value)
    {
        OnPropertyChanged(nameof(PermisosAutoHabilitados));
        if (!_cargandoUsuario)
            _ = PrecargarPermisosDeRolAsync(PermisosAuto, value?.Valor);
    }

    /// <summary>Al elegir un rol, sus permisos precargan los checkboxes del modo.</summary>
    private async Task PrecargarPermisosDeRolAsync(ObservableCollection<PermisoCheck> destino, int? rolId)
    {
        try
        {
            if (rolId is not { } rid)
            {
                foreach (var p in destino)
                    p.Marcado = false;
                return;
            }
            var delRol = await _usuarios.ObtenerPermisoIdsDeRolAsync(rid);
            foreach (var p in destino)
                p.Marcado = delRol.Contains(p.Id);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error precargando permisos del rol {RolId}", rolId);
        }
    }

    public string PasswordNueva { get; set; } = string.Empty;

    public string TextoAyudaPassword => EsNuevo
        ? $"Mínimo {UsuarioService.MinLargoPassword} caracteres."
        : "Dejalo en blanco para no cambiar la contraseña actual.";

    partial void OnEsNuevoChanged(bool value) => OnPropertyChanged(nameof(TextoAyudaPassword));

    public async Task CargarAsync()
    {
        try
        {
            Ocupado = true;

            if (RolesPrest.Count == 0)
                await CargarCatalogoRolesAsync();

            var usuarios = await _usuarios.ObtenerTodosAsync();
            Filas.Clear();
            foreach (var usuario in usuarios)
                Filas.Add(new UsuarioFila(usuario));

            FormularioVisible = false;
            MensajeError = MensajeExito = string.Empty;
        }
        catch (UnauthorizedAccessException ex)
        {
            MensajeError = ex.Message;
            Filas.Clear();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cargando usuarios");
            _dialogos.MostrarError("Usuarios", ex.Message);
        }
        finally
        {
            Ocupado = false;
        }
    }

    /// <summary>Arma los tres combos con los roles de cada modo + "Sin acceso".</summary>
    private async Task CargarCatalogoRolesAsync()
    {
        var roles = await _usuarios.ObtenerRolesAsync();
        LlenarCombo(RolesPrest, roles, "prestcontrol");
        LlenarCombo(RolesDealer, roles, "dealercontrol");
        LlenarCombo(RolesAuto, roles, "autocontrol");
        // Catálogo de permisos por pantalla de cada modo (013): fijo por sesión
        await LlenarPermisosAsync(PermisosPrest, "prestcontrol");
        await LlenarPermisosAsync(PermisosDealer, "dealercontrol");
        await LlenarPermisosAsync(PermisosAuto, "autocontrol");
    }

    private async Task LlenarPermisosAsync(ObservableCollection<PermisoCheck> destino, string modo)
    {
        destino.Clear();
        foreach (var permiso in await _usuarios.ObtenerCatalogoPermisosDeModoAsync(modo))
            destino.Add(new PermisoCheck(permiso, marcado: false));
    }

    private static void LlenarCombo(ObservableCollection<Opcion<int?>> combo,
        IReadOnlyList<Rol> roles, string modo)
    {
        combo.Clear();
        combo.Add(new Opcion<int?>(null, "Sin acceso"));
        foreach (var rol in roles.Where(r => r.Modo == modo))
            combo.Add(new Opcion<int?>(rol.Id, rol.Nombre));
    }

    [RelayCommand]
    private void Nuevo()
    {
        _editandoId = null;
        EsNuevo = true;
        TituloFormulario = "Nuevo usuario";
        Username = Nombre = Apellido = string.Empty;
        PasswordNueva = string.Empty;
        PasswordDebeLimpiarse?.Invoke();
        Activo = true;
        MensajeError = MensajeExito = string.Empty;
        EsAdministrador = false;
        EsProgramador = false;
        RolPrestSeleccionado = RolesPrest.FirstOrDefault();
        RolDealerSeleccionado = RolesDealer.FirstOrDefault();
        RolAutoSeleccionado = RolesAuto.FirstOrDefault();
        // Alta nueva: sin rol elegido, todos los checkboxes desmarcados
        foreach (var p in PermisosPrest.Concat(PermisosDealer).Concat(PermisosAuto))
            p.Marcado = false;
        FormularioVisible = true;
    }

    [RelayCommand]
    private async Task EditarAsync(UsuarioFila? fila)
    {
        if (fila is null)
            return;
        try
        {
            _editandoId = fila.Id;
            EsNuevo = false;
            TituloFormulario = $"Editar usuario — {fila.NombreCompleto}";
            Username = fila.Usuario.Username;
            Nombre = fila.Usuario.Nombre;
            Apellido = fila.Usuario.Apellido ?? string.Empty;
            Activo = fila.Usuario.Activo;
            PasswordNueva = string.Empty;
            PasswordDebeLimpiarse?.Invoke();
            MensajeError = MensajeExito = string.Empty;

            var roles = await _usuarios.ObtenerRolesDeUsuarioAsync(fila.Id);
            _cargandoUsuario = true;
            try
            {
                EsAdministrador = roles.EsAdmin;
                EsProgramador = roles.EsProgramador;
                RolPrestSeleccionado = OpcionPara(RolesPrest, roles.RolPrestId);
                RolDealerSeleccionado = OpcionPara(RolesDealer, roles.RolDealerId);
                RolAutoSeleccionado = OpcionPara(RolesAuto, roles.RolAutoId);

                // Checkboxes: el set guardado (013) o, si nunca se guardó, los del rol
                await CargarPermisosGuardadosAsync(PermisosPrest, fila.Id, "prestcontrol", roles.RolPrestId);
                await CargarPermisosGuardadosAsync(PermisosDealer, fila.Id, "dealercontrol", roles.RolDealerId);
                await CargarPermisosGuardadosAsync(PermisosAuto, fila.Id, "autocontrol", roles.RolAutoId);
            }
            finally
            {
                _cargandoUsuario = false;
            }

            FormularioVisible = true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error abriendo el usuario {Id}", fila.Id);
            _dialogos.MostrarError("Usuarios", ex.Message);
        }
    }

    private async Task CargarPermisosGuardadosAsync(ObservableCollection<PermisoCheck> destino,
        long usuarioId, string modo, int? rolId)
    {
        if (rolId is null)
        {
            foreach (var p in destino)
                p.Marcado = false;
            return;
        }
        var guardados = await _usuarios.ObtenerPermisosModoUsuarioAsync(usuarioId, modo);
        if (guardados.Count == 0)
        {
            await PrecargarPermisosDeRolAsync(destino, rolId);
            return;
        }
        foreach (var p in destino)
            p.Marcado = guardados.Contains(p.Id);
    }

    private static Opcion<int?> OpcionPara(ObservableCollection<Opcion<int?>> combo, int? rolId) =>
        combo.FirstOrDefault(o => o.Valor == rolId) ?? combo[0];

    [RelayCommand]
    private async Task GuardarAsync()
    {
        MensajeError = MensajeExito = string.Empty;
        // Set marcado por modo (013): solo de los modos con rol elegido
        var permisosPorModo = new Dictionary<string, IReadOnlyList<int>>();
        if (RolPrestSeleccionado?.Valor is not null)
            permisosPorModo["prestcontrol"] = [.. PermisosPrest.Where(p => p.Marcado).Select(p => p.Id)];
        if (RolDealerSeleccionado?.Valor is not null)
            permisosPorModo["dealercontrol"] = [.. PermisosDealer.Where(p => p.Marcado).Select(p => p.Id)];
        if (RolAutoSeleccionado?.Valor is not null)
            permisosPorModo["autocontrol"] = [.. PermisosAuto.Where(p => p.Marcado).Select(p => p.Id)];

        var roles = new RolesUsuario(
            EsAdministrador,
            RolPrestSeleccionado?.Valor,
            RolDealerSeleccionado?.Valor,
            RolAutoSeleccionado?.Valor,
            permisosPorModo,
            EsProgramador);

        try
        {
            Ocupado = true;
            var eraNuevo = _editandoId is null;

            if (eraNuevo)
            {
                await _usuarios.CrearAsync(Username, Nombre, Apellido, roles, PasswordNueva);
            }
            else
            {
                var id = _editandoId!.Value;
                await _usuarios.ActualizarAsync(id, Nombre, Apellido, roles, Activo);
                if (!string.IsNullOrEmpty(PasswordNueva))
                    await _usuarios.RestablecerPasswordAsync(id, PasswordNueva);
            }

            PasswordNueva = string.Empty;
            PasswordDebeLimpiarse?.Invoke();
            await CargarAsync();
            MensajeExito = eraNuevo
                ? "Usuario creado. Ya puede iniciar sesión."
                : "Usuario actualizado.";
        }
        catch (ArgumentException ex) { MensajeError = ex.Message; }
        catch (InvalidOperationException ex) { MensajeError = ex.Message; }
        catch (UnauthorizedAccessException ex) { MensajeError = ex.Message; }
        catch (Exception ex)
        {
            Log.Error(ex, "Error guardando el usuario");
            _dialogos.MostrarError("Usuarios", ex.Message);
        }
        finally
        {
            Ocupado = false;
        }
    }

    [RelayCommand]
    private void Cancelar()
    {
        FormularioVisible = false;
        PasswordNueva = string.Empty;
        PasswordDebeLimpiarse?.Invoke();
        MensajeError = MensajeExito = string.Empty;
    }
}
