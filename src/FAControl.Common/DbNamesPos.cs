namespace FAControl.Common;

/// <summary>
/// Tablas del PUNTO DE VENTA. Viven en `facontrol_db`, junto al resto de la
/// suite, con prefijo `pos_` (024).
///
/// El prefijo no es decoración: varias se llamaban igual que las de la suite
/// —`cliente` sobre todo— y el cliente del mostrador es otra cosa que el de
/// préstamos (cédula opcional, sin apellido, sin ámbito). Tenerlas separadas y
/// marcadas evita que alguien escriba una consulta contra la tabla equivocada.
///
/// Los usuarios, roles, permisos, sesiones y la auditoría son los de
/// <see cref="DbNames"/>: compartidos por todos los modos.
/// </summary>
public static class DbNamesPos
{
    public const string Cliente = "pos_cliente";
    public const string Producto = "pos_producto";
    public const string Factura = "pos_factura";
    public const string Detalle = "pos_detalle";
    public const string CuadreCaja = "pos_cuadre_caja";
    public const string ConfiguracionNegocio = "pos_configuracion";
}
