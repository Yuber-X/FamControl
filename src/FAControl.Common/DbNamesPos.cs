namespace FAControl.Common;

/// <summary>
/// Nombres de tablas de la base del PUNTO DE VENTA (`pos500_db`), que es una
/// base distinta a la de la suite.
///
/// Va aparte de <see cref="DbNames"/> a propósito, aunque algunos nombres se
/// repitan: `cliente` acá es el cliente del mostrador y no tiene nada que ver
/// con el de préstamos. Tenerlos separados evita que alguien escriba una consulta
/// del POS contra la base equivocada sin darse cuenta.
///
/// Lo que NO está acá —usuario, rol, permiso, sesion, auditoria— vive en
/// facontrol_db y se referencia con <see cref="DbNames"/>: es compartido por
/// todos los modos de la suite.
/// </summary>
public static class DbNamesPos
{
    public const string Cliente = "cliente";
    public const string Producto = "producto";
    public const string Factura = "factura";
    public const string Detalle = "detalle";
    public const string CuadreCaja = "cuadre_caja";
    public const string ConfiguracionNegocio = "configuracion_negocio";
}
