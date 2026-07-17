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

    public async Task<IReadOnlyList<TablaExportada>> ObtenerTodoAsync(CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        return
        [
            await LeerAsync(conexion, "Clientes", $"""
                SELECT id, cedula, nombre, apellido, telefono, direccion, email, notas,
                       created_at, updated_at, deleted_at
                FROM {DbNames.Cliente} ORDER BY id;
                """, ct),
            await LeerAsync(conexion, "Préstamos", $"""
                SELECT id, codigo, cliente_id, monto_capital, moneda, tasa_interes, plazo_cuotas,
                       modalidad, metodo_amortizacion, fecha_inicio, garantia, estado, notas,
                       created_at, updated_at
                FROM {DbNames.Prestamo} ORDER BY id;
                """, ct),
            await LeerAsync(conexion, "Cuotas", $"""
                SELECT id, prestamo_id, numero_cuota, fecha_vencimiento, capital, interes,
                       monto_total, saldo_despues, monto_pagado, estado, created_at, updated_at
                FROM {DbNames.Cuota} ORDER BY prestamo_id, numero_cuota;
                """, ct),
            await LeerAsync(conexion, "Pagos", $"""
                SELECT id, cuota_id, numero_recibo, fecha_pago, monto_pagado, monto_interes,
                       monto_capital, metodo_pago, notas, created_at, deleted_at
                FROM {DbNames.Pago} ORDER BY id;
                """, ct),
            await LeerAsync(conexion, "Auditoría", $"""
                SELECT id, usuario_id, entidad, entidad_id, accion, descripcion, timestamp
                FROM {DbNames.Auditoria} ORDER BY id;
                """, ct)
        ];
    }

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
