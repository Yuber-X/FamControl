namespace FAControl.Models;

/// <summary>Cuenta única del prestamista (sistema mono-usuario).</summary>
public class Usuario
{
    public long Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string? Apellido { get; set; }
    /// <summary>Rol asignado. NULL = sin rol (no recibe permisos por defecto).</summary>
    public int? RolId { get; set; }
    public string RolNombre { get; set; } = string.Empty;
    public bool Activo { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }

    public string NombreCompleto =>
        string.IsNullOrWhiteSpace(Apellido) ? Nombre : $"{Nombre} {Apellido}";
}

/// <summary>Rol del catálogo (Admin / Supervisor / Cobrador).</summary>
public class Rol
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    /// <summary>Modo al que pertenece el rol ("prestcontrol"/"dealercontrol"/"autocontrol"); null = global (Admin).</summary>
    public string? Modo { get; set; }
    public string? Descripcion { get; set; }
}

/// <summary>
/// Roles de un usuario POR MODO (roles por modo, 011). EsAdmin = global.
/// RolPrestId/RolDealerId/RolAutoId = rol elegido en cada modo (null = sin acceso).
/// </summary>
public record RolesUsuario(bool EsAdmin, int? RolPrestId, int? RolDealerId, int? RolAutoId,
    /// <summary>Rol del punto de venta (024). Null = sin acceso al POS.</summary>
    int? RolPosId = null,
    /// <summary>
    /// Permisos por pantalla MARCADOS por modo (013): clave = modo en texto de BD
    /// ('prestcontrol'...), valor = ids de permiso. Null = usar los del rol.
    /// </summary>
    IReadOnlyDictionary<string, IReadOnlyList<int>>? PermisosPorModo = null,
    /// <summary>
    /// Rol PROGRAMADOR (017): autoridad total y blindada. Manda sobre EsAdmin.
    /// Solo un Programador puede marcarlo; UsuarioService rechaza el resto.
    /// </summary>
    bool EsProgramador = false);

/// <summary>Permiso del catálogo. `Codigo` es lo que consulta SesionActual.TienePermiso.</summary>
public class Permiso
{
    public int Id { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
}

/// <summary>Registro de login/logout en la tabla sesion.</summary>
public class Sesion
{
    public long Id { get; set; }
    public long UsuarioId { get; set; }
    public DateTime LoginAtUtc { get; set; }
    public DateTime? LogoutAtUtc { get; set; }
    public string? IpLocal { get; set; }
}

/// <summary>Persona a la que se le presta. Soft delete vía DeletedAtUtc.</summary>
public class Cliente
{
    public long Id { get; set; }
    public string Cedula { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string Apellido { get; set; } = string.Empty;
    public string? Telefono { get; set; }
    public string? Direccion { get; set; }
    public string? Email { get; set; }
    public string? Notas { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public DateTime? DeletedAtUtc { get; set; }

    public string NombreCompleto => $"{Nombre} {Apellido}".Trim();
}

/// <summary>Contrato de préstamo.</summary>
public class Prestamo
{
    public long Id { get; set; }
    public string Codigo { get; set; } = string.Empty;      // P-0001
    /// <summary>Comprobante fiscal del préstamo (012): registrado o asignado de la secuencia. NULL = sin comprobante.</summary>
    public string? Ncf { get; set; }
    public long ClienteId { get; set; }
    /// <summary>AutoControl: vehículo en garantía. NULL = préstamo personal (PrestControl).</summary>
    public long? VehiculoId { get; set; }
    public decimal MontoCapital { get; set; }
    public string Moneda { get; set; } = "DOP";
    /// <summary>Tasa de interés MENSUAL en porcentaje (ej. 10 = 10% mensual).</summary>
    public decimal TasaInteres { get; set; }
    public int PlazoCuotas { get; set; }
    public Modalidad Modalidad { get; set; } = Modalidad.Mensual;
    public MetodoAmortizacion MetodoAmortizacion { get; set; } = MetodoAmortizacion.CuotaFija;
    /// <summary>
    /// Solo para <see cref="MetodoAmortizacion.CapitalDiferido"/> (040): primera
    /// cuota que ya lleva abono a capital. NULL = lo decidió el sistema. Es un
    /// dato del CONTRATO ("acordamos 6 meses de solo interés"), por eso se
    /// guarda en vez de deducirlo mirando las cuotas.
    /// </summary>
    public int? CuotaInicioCapital { get; set; }
    /// <summary>Fecha del primer pago (fecha de negocio, sin componente horario).</summary>
    public DateOnly FechaInicio { get; set; }
    public string? Garantia { get; set; }
    public EstadoPrestamo Estado { get; set; } = EstadoPrestamo.Activo;
    public string? Notas { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}

/// <summary>Cuota individual de un préstamo.</summary>
public class Cuota
{
    public long Id { get; set; }
    public long PrestamoId { get; set; }
    public int NumeroCuota { get; set; }
    public DateOnly FechaVencimiento { get; set; }
    public decimal Capital { get; set; }
    public decimal Interes { get; set; }
    public decimal MontoTotal { get; set; }
    public decimal SaldoDespues { get; set; }
    public decimal MontoPagado { get; set; }
    public EstadoCuota Estado { get; set; } = EstadoCuota.Pendiente;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }

    public decimal SaldoPendiente => MontoTotal - MontoPagado;
}

/// <summary>
/// Abono a una cuota. Un pago registrado NUNCA se modifica: los errores
/// se corrigen con un pago compensatorio negativo (regla contable).
/// </summary>
public class Pago
{
    public long Id { get; set; }
    public long CuotaId { get; set; }
    public string NumeroRecibo { get; set; } = string.Empty; // R-000001, único e inmutable
    public DateTime FechaPagoUtc { get; set; }
    public decimal MontoPagado { get; set; }
    public decimal MontoInteres { get; set; }
    public decimal MontoCapital { get; set; }
    public MetodoPago MetodoPago { get; set; } = MetodoPago.Efectivo;
    public string? Notas { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
}

/// <summary>Entrada del log de auditoría (inmutable, nunca se borra).</summary>
public class Auditoria
{
    public long Id { get; set; }
    public long UsuarioId { get; set; }
    /// <summary>
    /// Nombre del usuario que hizo la acción (JOIN, no columna).
    /// Multiusuario: el Historial debe decir QUIÉN, no solo qué.
    /// </summary>
    public string UsuarioNombre { get; set; } = string.Empty;
    /// <summary>Estancia donde se hizo (025). Null = linea anterior a esa migracion.</summary>
    public string? Modo { get; set; }
    public string Entidad { get; set; } = string.Empty;
    public long? EntidadId { get; set; }
    public AccionAuditoria Accion { get; set; }
    public string? Descripcion { get; set; }
    public string? IpLocal { get; set; }
    public DateTime TimestampUtc { get; set; }
}
