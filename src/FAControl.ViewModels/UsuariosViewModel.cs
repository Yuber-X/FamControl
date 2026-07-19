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
    public string EstadoTexto => Usuario.Activo ? "Activo" : "Inactivo";
    public string UltimoAccesoTexto => Usuario.LastLoginAtUtc is { } fecha
        ? FechaNegocio.AUtcLocal(fecha).ToString(Textos.FormatoFechaHora, Textos.CulturaRd)
        : "Nunca";
    /// <summary>El usuario de la sesión actual: la UI lo marca para evitar accidentes.</summary>
    public bool EsElActual => Usuario.Id == SesionActual.Id;
}

/// <summary>
/// Admin de Usuarios — SOLO Admin. Modelo de ROLES POR MODO (Yuber 2026-07-18):
/// se marca "administrador" (acceso global) o se elige UN rol por cada modo
/// (PrestControl / DealControl / AutoControl), con opción "Sin acceso". Cada rol
/// trae sus propios permisos; los efectivos son la unión (lo maneja el Service).
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
    [ObservableProperty] private Opcion<int?>? _rolPrestSeleccionado;
    [ObservableProperty] private Opcion<int?>? _rolDealerSeleccionado;
    [ObservableProperty] private Opcion<int?>? _rolAutoSeleccionado;

    /// <summary>True cuando NO es admin: habilita los selectores de rol por modo.</summary>
    public bool RolesPorModoHabilitados => !EsAdministrador;
    partial void OnEsAdministradorChanged(bool value) => OnPropertyChanged(nameof(RolesPorModoHabilitados));

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
        RolPrestSeleccionado = RolesPrest.FirstOrDefault();
        RolDealerSeleccionado = RolesDealer.FirstOrDefault();
        RolAutoSeleccionado = RolesAuto.FirstOrDefault();
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
            EsAdministrador = roles.EsAdmin;
            RolPrestSeleccionado = OpcionPara(RolesPrest, roles.RolPrestId);
            RolDealerSeleccionado = OpcionPara(RolesDealer, roles.RolDealerId);
            RolAutoSeleccionado = OpcionPara(RolesAuto, roles.RolAutoId);

            FormularioVisible = true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error abriendo el usuario {Id}", fila.Id);
            _dialogos.MostrarError("Usuarios", ex.Message);
        }
    }

    private static Opcion<int?> OpcionPara(ObservableCollection<Opcion<int?>> combo, int? rolId) =>
        combo.FirstOrDefault(o => o.Valor == rolId) ?? combo[0];

    [RelayCommand]
    private async Task GuardarAsync()
    {
        MensajeError = MensajeExito = string.Empty;
        var roles = new RolesUsuario(
            EsAdministrador,
            RolPrestSeleccionado?.Valor,
            RolDealerSeleccionado?.Valor,
            RolAutoSeleccionado?.Valor);

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
