// Portado de POS-500 el 2026-07-30 al integrar el punto de venta a la suite.
// Cambios respecto del original: sus tablas llevan prefijo pos_ dentro de
// facontrol_db (024), y usa el SesionActual y la auditoria de la suite.
using FAControl.Common;
using FAControl.Data.Pos;
// Solo el enum de la auditoria compartida: importar todo FAControl.Models
// chocaria con Cliente/ClienteDatos, que en el POS son otra cosa.
using AccionAuditoria = FAControl.Models.AccionAuditoria;
using FAControl.Models.Pos;

namespace FAControl.Services.Pos;

/// <summary>
/// Reglas de negocio de productos: nombre obligatorio, precio > 0,
/// cantidad ≥ 0, código único si viene. Requiere permiso productos.
/// </summary>
public class ProductoService
{
    private readonly ProductoRepository _productos;
    private readonly AuditoriaService _auditoria;

    public ProductoService(ProductoRepository productos, AuditoriaService auditoria)
    {
        _productos = productos;
        _auditoria = auditoria;
    }

    public Task<List<Producto>> ObtenerTodosAsync(CancellationToken ct = default) =>
        _productos.ObtenerTodosAsync(ct);

    public Task<List<Producto>> ObtenerConCaducidadAsync(CancellationToken ct = default) =>
        _productos.ObtenerConCaducidadAsync(ct);

    public Task<Producto?> ObtenerPorIdAsync(long id, CancellationToken ct = default) =>
        _productos.ObtenerPorIdAsync(id, ct);

    public Task<AlmacenTotales> ObtenerTotalesAsync(CancellationToken ct = default) =>
        _productos.ObtenerTotalesAsync(ct);

    public async Task<long> CrearAsync(ProductoDatos datos, CancellationToken ct = default)
    {
        var limpios = await ValidarAsync(datos, exceptoId: null, ct);
        var id = await _productos.InsertarAsync(limpios, ct);
        await _auditoria.RegistrarAsync(AccionAuditoria.Crear, DbNamesPos.Producto, id,
            $"Producto creado: {limpios.Nombre} (precio {limpios.Precio:0.00}, stock {limpios.Cantidad})", ct);
        return id;
    }

    public async Task ActualizarAsync(long id, ProductoDatos datos, CancellationToken ct = default)
    {
        var anterior = await _productos.ObtenerPorIdAsync(id, ct)
            ?? throw new InvalidOperationException("El producto no existe o fue eliminado.");
        var limpios = await ValidarAsync(datos, exceptoId: id, ct);
        await _productos.ActualizarAsync(id, limpios, ct);

        var detalle = $"Producto modificado: {limpios.Nombre}";
        if (anterior.Precio != limpios.Precio)
            detalle += $" · precio {anterior.Precio:0.00} → {limpios.Precio:0.00}";
        if (anterior.Cantidad != limpios.Cantidad)
            detalle += $" · stock {anterior.Cantidad} → {limpios.Cantidad}";
        await _auditoria.RegistrarAsync(AccionAuditoria.Modificar, DbNamesPos.Producto, id, detalle, ct);
    }

    public async Task EliminarAsync(long id, CancellationToken ct = default)
    {
        ValidarPermiso();
        var producto = await _productos.ObtenerPorIdAsync(id, ct)
            ?? throw new InvalidOperationException("El producto no existe o ya fue eliminado.");
        await _productos.EliminarAsync(id, ct);
        await _auditoria.RegistrarAsync(AccionAuditoria.Eliminar, DbNamesPos.Producto, id,
            $"Producto eliminado: {producto.Nombre}", ct);
    }

    private async Task<ProductoDatos> ValidarAsync(ProductoDatos datos, long? exceptoId, CancellationToken ct)
    {
        ValidarPermiso();

        if (string.IsNullOrWhiteSpace(datos.Nombre))
            throw new ArgumentException("El nombre del producto es obligatorio.");
        if (datos.Precio <= 0m)
            throw new ArgumentException("El precio debe ser mayor que cero.");
        if (datos.Cantidad < 0)
            throw new ArgumentException("La cantidad no puede ser negativa.");

        var codigo = string.IsNullOrWhiteSpace(datos.Codigo) ? null : datos.Codigo.Trim();
        if (codigo is not null && await _productos.ExisteCodigoAsync(codigo, exceptoId, ct))
            throw new ArgumentException($"Ya existe un producto con el código {codigo}.");

        return datos with
        {
            Codigo = codigo,
            Nombre = datos.Nombre.Trim(),
            Descripcion = string.IsNullOrWhiteSpace(datos.Descripcion) ? null : datos.Descripcion.Trim()
        };
    }

    private static void ValidarPermiso()
    {
        if (!SesionActual.TienePermiso("productos"))
            throw new InvalidOperationException("No tienes permiso para gestionar productos.");
    }
}
