namespace FAControl.Common;

/// <summary>
/// Nombres de tablas de la base de datos. Prohibido usar cadenas mágicas
/// para tablas en los repositorios — siempre referenciar estas constantes.
/// </summary>
public static class DbNames
{
    public const string Usuario = "usuario";
    public const string Sesion = "sesion";
    public const string Cliente = "cliente";
    public const string Prestamo = "prestamo";
    public const string Cuota = "cuota";
    public const string Pago = "pago";
    public const string Auditoria = "auditoria";
    public const string Contador = "contador";
}
