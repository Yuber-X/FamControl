using MySqlConnector;
using FAControl.Common;

namespace FAControl.Data;

/// <summary>Una tabla lista para exportar: encabezados + filas de celdas.</summary>
public record TablaExportada(string Nombre, IReadOnlyList<string> Encabezados, IReadOnlyList<object?[]> Filas);

/// <summary>
/// Lecturas completas para la exportación a Excel (migración/consulta).
/// Columnas explícitas siempre — nada de SELECT *.
/// </summary>
public class ExportacionRepository
{
    private readonly ConexionFactory _factory;

    public ExportacionRepository(ConexionFactory factory) => _factory = factory;

    /// <summary>
    /// Las tablas del MODO ACTIVO (2026-08-01). Antes exportaba siempre las de
    /// prestamos: dentro del dealer o del punto de venta se bajaba un Excel con
    /// cuotas y pagos que no tenian nada que ver con lo que el usuario estaba
    /// mirando, y faltaba todo lo suyo.
    ///
    /// Cada estancia se lleva SUS datos y nada mas. Es la misma regla que rige
    /// todo el resto de la suite: los modos no mezclan informacion.
    /// </summary>
    public async Task<IReadOnlyList<TablaExportada>> ObtenerTodoAsync(ModoApp modo,
        CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        var ambito = modo.ClaveDb();

        return modo switch
        {
            ModoApp.DealerControl => await DealAsync(conexion, ambito, ct),
            ModoApp.Pos500 => await PosAsync(conexion, ambito, ct),
            _ => await CreditoAsync(conexion, ambito, ct)
        };
    }

    /// <summary>PrestControl y AutoControl: la cartera de prestamos.</summary>
    private static async Task<IReadOnlyList<TablaExportada>> CreditoAsync(
        MySqlConnection conexion, string ambito, CancellationToken ct) =>
    [
        await LeerAsync(conexion, "Clientes", $"""
            SELECT id, cedula, nombre, apellido, telefono, direccion, email, notas,
                   created_at, updated_at, deleted_at
            FROM {DbNames.Cliente} WHERE ambito = '{ambito}' ORDER BY id;
            """, ct),
        await LeerAsync(conexion, "Préstamos", $"""
            SELECT p.id, p.codigo, p.cliente_id, p.monto_capital, p.moneda, p.tasa_interes,
                   p.plazo_cuotas, p.modalidad, p.metodo_amortizacion, p.fecha_inicio,
                   p.garantia, p.estado, p.notas, p.created_at, p.updated_at
            FROM {DbNames.Prestamo} p
            JOIN {DbNames.Cliente} c ON c.id = p.cliente_id AND c.ambito = '{ambito}'
            ORDER BY p.id;
            """, ct),
        await LeerAsync(conexion, "Cuotas", $"""
            SELECT q.id, q.prestamo_id, q.numero_cuota, q.fecha_vencimiento, q.capital, q.interes,
                   q.monto_total, q.saldo_despues, q.monto_pagado, q.estado, q.created_at, q.updated_at
            FROM {DbNames.Cuota} q
            JOIN {DbNames.Prestamo} p ON p.id = q.prestamo_id
            JOIN {DbNames.Cliente} c ON c.id = p.cliente_id AND c.ambito = '{ambito}'
            ORDER BY q.prestamo_id, q.numero_cuota;
            """, ct),
        await LeerAsync(conexion, "Pagos", $"""
            SELECT g.id, g.cuota_id, g.numero_recibo, g.fecha_pago, g.monto_pagado, g.monto_interes,
                   g.monto_capital, g.metodo_pago, g.notas, g.created_at, g.deleted_at
            FROM {DbNames.Pago} g
            JOIN {DbNames.Cuota} q ON q.id = g.cuota_id
            JOIN {DbNames.Prestamo} p ON p.id = q.prestamo_id
            JOIN {DbNames.Cliente} c ON c.id = p.cliente_id AND c.ambito = '{ambito}'
            ORDER BY g.id;
            """, ct),
        await AuditoriaAsync(conexion, ambito, ct)
    ];

    /// <summary>DealControl: inventario, ventas y alquileres.</summary>
    private static async Task<IReadOnlyList<TablaExportada>> DealAsync(
        MySqlConnection conexion, string ambito, CancellationToken ct) =>
    [
        await LeerAsync(conexion, "Clientes", $"""
            SELECT id, cedula, nombre, apellido, telefono, direccion, email, notas,
                   created_at, updated_at, deleted_at
            FROM {DbNames.Cliente} WHERE ambito = '{ambito}' ORDER BY id;
            """, ct),
        await LeerAsync(conexion, "Vehículos", $"""
            SELECT id, codigo, vin, marca, modelo, anio, color, placa, matricula, tipo,
                   kilometraje, costo_adquisicion, gastos_importacion, precio_venta,
                   estado, notas, created_at, updated_at, deleted_at
            FROM {DbNames.Vehiculo} ORDER BY id;
            """, ct),
        await LeerAsync(conexion, "Gastos de importación", $"""
            SELECT id, vehiculo_id, concepto, monto, fecha, created_at
            FROM {DbNames.VehiculoGasto} ORDER BY vehiculo_id, id;
            """, ct),
        await LeerAsync(conexion, "Ventas", $"""
            SELECT id, codigo, vehiculo_id, cliente_id, fecha_venta, precio, tipo_venta,
                   inicial, fecha_limite, metodo_pago, estado, cancelada_at, cancelada_motivo,
                   retencion_porcentaje, retenido, devuelto, notas, created_at
            FROM {DbNames.VentaVehiculo} ORDER BY id;
            """, ct),
        await LeerAsync(conexion, "Plazos", $"""
            SELECT id, venta_id, numero, fecha_vencimiento, monto, monto_pagado, estado,
                   created_at, updated_at
            FROM {DbNames.VentaPlazo} ORDER BY venta_id, numero;
            """, ct),
        await LeerAsync(conexion, "Cobros de plazos", $"""
            SELECT id, plazo_id, numero_recibo, fecha_pago, monto, metodo_pago, notas,
                   created_at, deleted_at
            FROM {DbNames.VentaPlazoPago} ORDER BY id;
            """, ct),
        await LeerAsync(conexion, "Alquileres", $"""
            SELECT id, codigo, vehiculo_id, cliente_id, fecha_inicio, fecha_fin, fecha_devolucion,
                   tarifa_dia, dias, dias_reales, monto_total, monto_final, estado,
                   cerrado_motivo, cerrado_at, notas, created_at
            FROM {DbNames.Alquiler} ORDER BY id;
            """, ct),
        await LeerAsync(conexion, "Cobros de alquiler", $"""
            SELECT id, alquiler_id, numero_recibo, fecha_pago, monto, metodo_pago, notas,
                   created_at, deleted_at
            FROM {DbNames.AlquilerPago} ORDER BY id;
            """, ct),
        await AuditoriaAsync(conexion, ambito, ct)
    ];

    /// <summary>POS-500: catalogo, facturacion y caja.</summary>
    private static async Task<IReadOnlyList<TablaExportada>> PosAsync(
        MySqlConnection conexion, string ambito, CancellationToken ct) =>
    [
        await LeerAsync(conexion, "Clientes", $"""
            SELECT id, cedula, nombre, telefono, direccion, notas,
                   created_at, updated_at, deleted_at
            FROM {DbNamesPos.Cliente} ORDER BY id;
            """, ct),
        await LeerAsync(conexion, "Productos", $"""
            SELECT id, codigo, nombre, precio, cantidad, descripcion, fecha_caducidad,
                   created_at, updated_at, deleted_at
            FROM {DbNamesPos.Producto} ORDER BY id;
            """, ct),
        await LeerAsync(conexion, "Facturas", $"""
            SELECT id, numero_factura, cliente_id, usuario_id, fecha_emision, subtotal,
                   itbis, total, metodo_pago, efectivo_recibido, cambio, estado, created_at
            FROM {DbNamesPos.Factura} ORDER BY id;
            """, ct),
        await LeerAsync(conexion, "Detalle de facturas", $"""
            SELECT id, factura_id, producto_id, cantidad, precio_unitario, subtotal
            FROM {DbNamesPos.Detalle} ORDER BY factura_id, id;
            """, ct),
        await LeerAsync(conexion, "Cuadres de caja", $"""
            SELECT id, usuario_id, fecha, total_facturas, total_vendido,
                   tiempo_activo_segundos, created_at
            FROM {DbNamesPos.CuadreCaja} ORDER BY id;
            """, ct),
        await AuditoriaAsync(conexion, ambito, ct)
    ];

    /// <summary>
    /// El historial DE ESA ESTANCIA. Las filas viejas quedaron sin modo (la
    /// columna llego en 025): se incluyen igual, porque perderlas seria peor
    /// que mostrar de mas en un archivo que el dueño baja para consultar.
    /// </summary>
    private static Task<TablaExportada> AuditoriaAsync(
        MySqlConnection conexion, string ambito, CancellationToken ct) =>
        LeerAsync(conexion, "Historial", $"""
            SELECT id, usuario_id, entidad, entidad_id, accion, descripcion, modo, timestamp
            FROM {DbNames.Auditoria}
            WHERE modo = '{ambito}' OR modo IS NULL
            ORDER BY id;
            """, ct);

    private static async Task<TablaExportada> LeerAsync(MySqlConnection conexion, string nombre,
        string sql, CancellationToken ct)
    {
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = sql;
        using var reader = await cmd.ExecuteReaderAsync(ct);

        var encabezados = new string[reader.FieldCount];
        for (var i = 0; i < reader.FieldCount; i++)
            encabezados[i] = reader.GetName(i);

        var filas = new List<object?[]>();
        while (await reader.ReadAsync(ct))
        {
            var fila = new object?[reader.FieldCount];
            for (var i = 0; i < reader.FieldCount; i++)
                fila[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            filas.Add(fila);
        }
        return new TablaExportada(nombre, encabezados, filas);
    }
}
