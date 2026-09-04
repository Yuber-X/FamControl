using MySqlConnector;
using FAControl.Common;
using FAControl.Models;

namespace FAControl.Data;

/// <summary>
/// Copia CONGELADA de las partes del pagaré notarial (045).
///
/// El notario, quien firma por la empresa y los dos testigos se guardan tal
/// como salieron impresos. Reimprimir un contrato firmado el año pasado tiene
/// que dar el MISMO papel, aunque el negocio haya cambiado de notario desde
/// entonces: es un documento con valor ejecutorio, no un reporte.
///
/// Sin fila = el acta se arma con la configuración vigente. Es lo correcto
/// para los préstamos anteriores a este cambio: de esos no hay copia y no se
/// puede inventar una.
/// </summary>
public class PrestamoActaRepository
{
    private readonly ConexionFactory _factory;

    public PrestamoActaRepository(ConexionFactory factory) => _factory = factory;

    private const string Columnas = """
        prestamo_id, empresa_direccion, municipio,
        notario_nombre, notario_matricula, notario_cedula, notario_estado_civil,
        notario_ocupacion, notario_domicilio, notario_nacionalidad, notario_sexo,
        repr_nombre, repr_cedula, repr_estado_civil, repr_ocupacion,
        repr_domicilio, repr_nacionalidad, repr_sexo,
        t1_nombre, t1_cedula, t1_estado_civil, t1_ocupacion, t1_domicilio,
        t1_nacionalidad, t1_sexo,
        t2_nombre, t2_cedula, t2_estado_civil, t2_ocupacion, t2_domicilio,
        t2_nacionalidad, t2_sexo
        """;

    /// <summary>La copia congelada de ese préstamo, o null si no tiene.</summary>
    public async Task<DatosNotariales?> ObtenerAsync(long prestamoId, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"SELECT {Columnas} FROM {DbNames.PrestamoActa} WHERE prestamo_id = @id;";
        cmd.Parameters.AddWithValue("@id", prestamoId);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Mapear(reader) : null;
    }

    /// <summary>
    /// Guarda (o reemplaza) la copia. Se llama al crear el préstamo y al
    /// corregirlo desde el detalle: en los dos casos, lo que queda guardado es
    /// lo que se va a reimprimir.
    /// </summary>
    public Task GuardarAsync(long prestamoId, DatosNotariales acta, CancellationToken ct = default) =>
        GuardarAsync(prestamoId, acta, null, null, ct);

    /// <summary>
    /// Igual, pero dentro de una transacción en curso: al crear un préstamo, la
    /// copia del acta tiene que entrar o no entrar junto con el préstamo.
    /// </summary>
    public async Task GuardarAsync(long prestamoId, DatosNotariales acta,
        MySqlConnection? conexionExterna, MySqlTransaction? transaccion,
        CancellationToken ct = default)
    {
        var conexion = conexionExterna ?? await _factory.AbrirAsync(ct);
        try
        {
            using var cmd = conexion.CreateCommand();
            cmd.Transaction = transaccion;
            cmd.CommandText = $"""
                INSERT INTO {DbNames.PrestamoActa} ({Columnas})
                VALUES (
                    @id, @empresaDireccion, @municipio,
                    @nNombre, @nMatricula, @nCedula, @nEstadoCivil,
                    @nOcupacion, @nDomicilio, @nNacionalidad, @nSexo,
                    @rNombre, @rCedula, @rEstadoCivil, @rOcupacion,
                    @rDomicilio, @rNacionalidad, @rSexo,
                    @t1Nombre, @t1Cedula, @t1EstadoCivil, @t1Ocupacion, @t1Domicilio,
                    @t1Nacionalidad, @t1Sexo,
                    @t2Nombre, @t2Cedula, @t2EstadoCivil, @t2Ocupacion, @t2Domicilio,
                    @t2Nacionalidad, @t2Sexo)
                ON DUPLICATE KEY UPDATE
                    empresa_direccion = @empresaDireccion, municipio = @municipio,
                    notario_nombre = @nNombre, notario_matricula = @nMatricula,
                    notario_cedula = @nCedula, notario_estado_civil = @nEstadoCivil,
                    notario_ocupacion = @nOcupacion, notario_domicilio = @nDomicilio,
                    notario_nacionalidad = @nNacionalidad, notario_sexo = @nSexo,
                    repr_nombre = @rNombre, repr_cedula = @rCedula,
                    repr_estado_civil = @rEstadoCivil, repr_ocupacion = @rOcupacion,
                    repr_domicilio = @rDomicilio, repr_nacionalidad = @rNacionalidad,
                    repr_sexo = @rSexo,
                    t1_nombre = @t1Nombre, t1_cedula = @t1Cedula,
                    t1_estado_civil = @t1EstadoCivil, t1_ocupacion = @t1Ocupacion,
                    t1_domicilio = @t1Domicilio, t1_nacionalidad = @t1Nacionalidad,
                    t1_sexo = @t1Sexo,
                    t2_nombre = @t2Nombre, t2_cedula = @t2Cedula,
                    t2_estado_civil = @t2EstadoCivil, t2_ocupacion = @t2Ocupacion,
                    t2_domicilio = @t2Domicilio, t2_nacionalidad = @t2Nacionalidad,
                    t2_sexo = @t2Sexo,
                    updated_at = UTC_TIMESTAMP();
                """;
            cmd.Parameters.AddWithValue("@id", prestamoId);
            cmd.Parameters.AddWithValue("@empresaDireccion", Texto(acta.EmpresaDireccion));
            cmd.Parameters.AddWithValue("@municipio", Texto(acta.Municipio));

            AgregarParte(cmd, "n", acta.Notario);
            cmd.Parameters.AddWithValue("@nMatricula", Texto(acta.NotarioMatricula));
            AgregarParte(cmd, "r", acta.Representante);

            var t1 = acta.Testigos.Count > 0 ? acta.Testigos[0] : new ParteDelActo("", "");
            var t2 = acta.Testigos.Count > 1 ? acta.Testigos[1] : new ParteDelActo("", "");
            AgregarParte(cmd, "t1", t1);
            AgregarParte(cmd, "t2", t2);

            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            // Solo se cierra la que abrió este método: la externa es de quien
            // maneja la transacción.
            if (conexionExterna is null)
                conexion.Dispose();
        }
    }

    /// <summary>Borra la copia (el acta se vacía por completo desde la corrección).</summary>
    public async Task EliminarAsync(long prestamoId, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"DELETE FROM {DbNames.PrestamoActa} WHERE prestamo_id = @id;";
        cmd.Parameters.AddWithValue("@id", prestamoId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ==================================================================

    private static void AgregarParte(MySqlCommand cmd, string prefijo, ParteDelActo parte)
    {
        cmd.Parameters.AddWithValue($"@{prefijo}Nombre", Texto(parte.Nombre));
        cmd.Parameters.AddWithValue($"@{prefijo}Cedula", Texto(parte.Cedula));
        cmd.Parameters.AddWithValue($"@{prefijo}EstadoCivil", Texto(parte.EstadoCivil));
        cmd.Parameters.AddWithValue($"@{prefijo}Ocupacion", Texto(parte.Ocupacion));
        cmd.Parameters.AddWithValue($"@{prefijo}Domicilio", Texto(parte.Domicilio));
        cmd.Parameters.AddWithValue($"@{prefijo}Nacionalidad", Texto(parte.Nacionalidad));
        cmd.Parameters.AddWithValue($"@{prefijo}Sexo", (int)parte.Sexo);
    }

    private static object Texto(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? DBNull.Value : valor.Trim();

    private static DatosNotariales Mapear(MySqlDataReader reader) => new()
    {
        EmpresaDireccion = Leer(reader, "empresa_direccion"),
        Municipio = Leer(reader, "municipio"),
        Notario = LeerParte(reader, "notario"),
        NotarioMatricula = Leer(reader, "notario_matricula"),
        Representante = LeerParte(reader, "repr"),
        Testigos = [LeerParte(reader, "t1"), LeerParte(reader, "t2")]
    };

    private static ParteDelActo LeerParte(MySqlDataReader reader, string prefijo) => new(
        Nombre: Leer(reader, $"{prefijo}_nombre"),
        Cedula: Leer(reader, $"{prefijo}_cedula"),
        Sexo: (SexoPersona)reader.GetInt32($"{prefijo}_sexo"),
        Nacionalidad: Leer(reader, $"{prefijo}_nacionalidad"),
        EstadoCivil: Leer(reader, $"{prefijo}_estado_civil"),
        Ocupacion: Leer(reader, $"{prefijo}_ocupacion"),
        Domicilio: Leer(reader, $"{prefijo}_domicilio"));

    private static string Leer(MySqlDataReader reader, string columna)
    {
        var i = reader.GetOrdinal(columna);
        return reader.IsDBNull(i) ? string.Empty : reader.GetString(i);
    }
}
