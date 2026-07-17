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
    public string RolTexto => string.IsNullOrEmpty(Usuario.RolNombre) ? "(sin rol)" : Usuario.RolNombre;
    public string EstadoTexto => Usuario.Activo ? "Activo" : "Inactivo";
    public string UltimoAccesoTexto => Usuario.LastLoginAtUtc is { } fecha
        ? FechaNegocio.AUtcLocal(fecha).ToString(Textos.FormatoFechaHora, Textos.CulturaRd)
        : "Nunca";
    /// <summary>El usuario de la sesión actual: la UI lo marca para evitar accidentes.</summary>
    public bool EsElActual => Usuario.Id == SesionActual.Id;
}

/// <summary>Casilla de un permiso en el formulario (marcable por el Admin).</summary>
public partial class PermisoCasilla : ObservableObject
{
    public required string Codigo { get; init; }
    public required string Nombre { get; init; }
    public string? Descripcion { get; init; }

    [ObservableProperty] private bool _asignado;
    /// <summary>True si el rol lo otorga por defecto (se muestra como pista).</summary>
    [ObservableProperty] private bool _vieneDelRol;

    public string PistaTexto => VieneDelRol ? "Por defecto en este rol" : "Permiso adicional";

    partial void OnVieneDelRolChanged(bool value) => OnPropertyChanged(nameof(PistaTexto));
}

/// <summary>
/// Admin de Usuarios — SOLO Admin (regla del cliente 2026-07-16): crear
/// empleados, restablecer sus contraseñas sin saber la anterior y ajustar
/// sus permisos desde la misma pantalla.
///
/// Los permisos por defecto los da el ROL (triggers de la BD); las casillas
/// permiten afinar quién puede crear préstamos o editar clientes.
/// La autorización real vive en UsuarioService: esta clase es solo la UI.
/// </summary>
public partial class UsuariosViewModel : ObservableObject
{
    private readonly UsuarioService _usuarios;
    private readonly IDialogService _dialogos;

    private long? _editandoId;          // null = alta nueva
    private IReadOnlyList<Permiso> _catalogo = [];

    /// <summary>
    /// La View debe vaciar el PasswordBox. Hace falta un evento porque el
    /// control no se puede bindear: si el VM limpia PasswordNueva pero el
    /// campo sigue mostrando puntos, el Admin cree que escribió una
    /// contraseña que en realidad ya no existe.
    /// </summary>
    public event Action? PasswordDebeLimpiarse;

    public UsuariosViewModel(UsuarioService usuarios, IDialogService dialogos)
    {
        _usuarios = usuarios;
        _dialogos = dialogos;
    }

    public ObservableCollection<UsuarioFila> Filas { get; } = [];
    public ObservableCollection<Opcion<int>> Roles { get; } = [];
    public ObservableCollection<PermisoCasilla> Permisos { get; } = [];

    [ObservableProperty] private bool _formularioVisible;
    [ObservableProperty] private string _tituloFormulario = "Nuevo usuario";
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _nombre = string.Empty;
    [ObservableProperty] private string _apellido = string.Empty;
    [ObservableProperty] private Opcion<int>? _rolSeleccionado;
    [ObservableProperty] private bool _activo = true;
    [ObservableProperty] private bool _esNuevo = true;
    [ObservableProperty] private string _mensajeError = string.Empty;
    [ObservableProperty] private string _mensajeExito = string.Empty;
    [ObservableProperty] private bool _ocupado;

    /// <summary>
    /// La contraseña la escribe la View: PasswordBox no se puede bindear
    /// (WPF no expone Password como DependencyProperty, a propósito).
    /// </summary>
    public string PasswordNueva { get; set; } = string.Empty;

    public string TextoAyudaPassword => EsNuevo
        ? $"Mínimo {UsuarioService.MinLargoPassword} caracteres."
        : "Dejalo en blanco para no cambiar la contraseña actual.";

    partial void OnEsNuevoChanged(bool value) => OnPropertyChanged(nameof(TextoAyudaPassword));
    partial void OnRolSeleccionadoChanged(Opcion<int>? value) => _ = MarcarPermisosDelRolAsync(value);

    public async Task CargarAsync()
    {
        try
        {
            Ocupado = true;

            if (Roles.Count == 0)
            {
                foreach (var rol in await _usuarios.ObtenerRolesAsync())
                    Roles.Add(new Opcion<int>(rol.Id, rol.Nombre));
                _catalogo = await _usuarios.ObtenerCatalogoPermisosAsync();
            }

            var usuarios = await _usuarios.ObtenerTodosAsync();
            Filas.Clear();
            foreach (var usuario in usuarios)
                Filas.Add(new UsuarioFila(usuario));

            FormularioVisible = false;
            MensajeError = MensajeExito = string.Empty;
        }
        catch (UnauthorizedAccessException ex)
        {
            // Un no-Admin no deberia llegar acá (el sidebar lo oculta), pero
            // si llega, se le dice claro en vez de reventar.
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

    // ------------------------------------------------------------------
    // Formulario
    // ------------------------------------------------------------------

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
        // Por defecto Cobrador: el rol menos peligroso si alguien no lo cambia.
        // Nombre completo porque la propiedad Roles tapa a Common.Roles.
        RolSeleccionado = Roles.FirstOrDefault(r => r.Texto == Common.Roles.Cobrador)
                          ?? Roles.FirstOrDefault();
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

            RolSeleccionado = Roles.FirstOrDefault(r => r.Valor == fila.Usuario.RolId);

            // Permisos EFECTIVOS del usuario (rol + overrides), no los del rol
            var actuales = await _usuarios.ObtenerPermisosAsync(fila.Id);
            var delRol = fila.Usuario.RolId is { } rolId
                ? await _usuarios.ObtenerPermisosDeRolAsync(rolId)
                : [];

            Permisos.Clear();
            foreach (var permiso in _catalogo)
                Permisos.Add(new PermisoCasilla
                {
                    Codigo = permiso.Codigo,
                    Nombre = permiso.Nombre,
                    Descripcion = permiso.Descripcion,
                    Asignado = actuales.Contains(permiso.Codigo),
                    VieneDelRol = delRol.Contains(permiso.Codigo)
                });

            FormularioVisible = true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error abriendo el usuario {Id}", fila.Id);
            _dialogos.MostrarError("Usuarios", ex.Message);
        }
    }

    /// <summary>Al elegir rol, se premarcan sus permisos por defecto.</summary>
    private async Task MarcarPermisosDelRolAsync(Opcion<int>? rol)
    {
        if (rol is null)
            return;

        try
        {
            var delRol = await _usuarios.ObtenerPermisosDeRolAsync(rol.Valor);

            if (Permisos.Count == 0)
                foreach (var permiso in _catalogo)
                    Permisos.Add(new PermisoCasilla
                    {
                        Codigo = permiso.Codigo,
                        Nombre = permiso.Nombre,
                        Descripcion = permiso.Descripcion
                    });

            foreach (var casilla in Permisos)
            {
                casilla.VieneDelRol = delRol.Contains(casilla.Codigo);
                // En alta nueva se parte SIEMPRE de los del rol; en edición se
                // respetan los overrides que el Admin ya tenía puestos.
                if (EsNuevo)
                    casilla.Asignado = casilla.VieneDelRol;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cargando los permisos del rol {Rol}", rol.Texto);
        }
    }

    [RelayCommand]
    private async Task GuardarAsync()
    {
        MensajeError = MensajeExito = string.Empty;

        if (RolSeleccionado is null)
        {
            MensajeError = "Elegí un rol para el usuario.";
            return;
        }

        try
        {
            Ocupado = true;
            long id;
            var eraNuevo = _editandoId is null;

            if (eraNuevo)
            {
                id = await _usuarios.CrearAsync(Username, Nombre, Apellido,
                    RolSeleccionado.Valor, PasswordNueva);
            }
            else
            {
                id = _editandoId!.Value;
                await _usuarios.ActualizarAsync(id, Nombre, Apellido, RolSeleccionado.Valor, Activo);

                // La contraseña solo se toca si el Admin escribió una nueva
                if (!string.IsNullOrEmpty(PasswordNueva))
                    await _usuarios.RestablecerPasswordAsync(id, PasswordNueva);
            }

            // Permisos finales: el trigger ya sembró los del rol; esto aplica
            // los ajustes del Admin encima.
            await _usuarios.GuardarPermisosAsync(id, Permisos.Where(p => p.Asignado).Select(p => p.Codigo));

            PasswordNueva = string.Empty;
        PasswordDebeLimpiarse?.Invoke();
            await CargarAsync();
            MensajeExito = eraNuevo
                ? "Usuario creado. Ya puede iniciar sesión."
                : "Usuario actualizado.";
        }
        catch (ArgumentException ex)
        {
            MensajeError = ex.Message;
        }
        catch (InvalidOperationException ex)
        {
            MensajeError = ex.Message;
        }
        catch (UnauthorizedAccessException ex)
        {
            MensajeError = ex.Message;
        }
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

    /// <summary>Deshace los overrides: vuelve a los permisos que da el rol.</summary>
    [RelayCommand]
    private async Task RestablecerPermisosAsync()
    {
        if (RolSeleccionado is null)
            return;

        var delRol = await _usuarios.ObtenerPermisosDeRolAsync(RolSeleccionado.Valor);
        foreach (var casilla in Permisos)
        {
            casilla.VieneDelRol = delRol.Contains(casilla.Codigo);
            casilla.Asignado = casilla.VieneDelRol;
        }
        MensajeExito = $"Permisos restablecidos a los de {RolSeleccionado.Texto}. Acordate de guardar.";
    }
}
