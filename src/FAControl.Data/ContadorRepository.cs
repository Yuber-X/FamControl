using MySqlConnector;
using FAControl.Common;

namespace FAControl.Data;

/// <summary>
/// Correlativos atómicos (codigo de préstamo, numero de recibo).
/// SIEMPRE dentro de la transacción de la operación que consume el número:
/// SELECT ... FOR UPDATE bloquea la fila hasta el COMMIT, así dos operaciones
/// simultáneas jamás reciben el mismo correlativo y un rollback no lo consume.
/// </summary>
public class ContadorRepository
{
    public const string Prestamo = "prestamo";
    public const string Recibo = "recibo";
    public const string Vehiculo = "vehiculo";
    public const string Venta = "venta";
    public const string Alquiler = "alquiler";

    /// <summary>Reserva y devuelve el siguiente valor del contador indicado.</summary>
    public async Task<long> SiguienteAsync(string nombre, MySqlConnection conexion,
        MySqlTransaction transaccion, CancellationToken ct = default)
    {
        long actual;
        using (var select = conexion.CreateCommand())
        {
            select.Transaction = transaccion;
            select.CommandText = $"SELECT valor FROM {DbNames.Contador} WHERE nombre = @nombre FOR UPDATE;";
            select.Parameters.AddWithValue("@nombre", nombre);
            var resultado = await select.ExecuteScalarAsync(ct)
                ?? throw new InvalidOperationException($"No existe el contador '{nombre}'.");
            actual = Convert.ToInt64(resultado);
        }

        var siguiente = actual + 1;
        using (var update = conexion.CreateCommand())
        {
            update.Transaction = transaccion;
            update.CommandText = $"UPDATE {DbNames.Contador} SET valor = @valor WHERE nombre = @nombre;";
            update.Parameters.AddWithValue("@valor", siguiente);
            update.Parameters.AddWithValue("@nombre", nombre);
            await update.ExecuteNonQueryAsync(ct);
        }

        return siguiente;
    }
}
