using System.Globalization;
using FAControl.Common;
using FAControl.Data;
using FAControl.Models;

namespace FAControl.Services;

/// <summary>
/// Almacén de contratos (cliente 2026-07-17): cada préstamo tiene su pagaré.
/// Este servicio compone el pagaré de un préstamo EXISTENTE a partir del
/// préstamo, su cliente, sus cuotas y los datos del negocio, para poder
/// reimprimirlo o verlo desde el almacén sin recalcular nada.
/// </summary>
public class ContratoService
{
    private static readonly CultureInfo CulturaRd = CultureInfo.GetCultureInfo("es-DO");
    private const string FormatoFecha = @"dd'/'MM'/'yyyy";

    private readonly PrestamoService _prestamos;
    private readonly ClienteService _clientes;
    private readonly AjustesLocales _ajustes;
    private readonly PrestamoActaRepository _actas;

    public ContratoService(PrestamoService prestamos, ClienteService clientes,
        AjustesLocales ajustes, PrestamoActaRepository actas)
    {
        _prestamos = prestamos;
        _clientes = clientes;
        _ajustes = ajustes;
        _actas = actas;
    }

    /// <summary>
    /// Préstamos como filas del almacén de contratos, AISLADOS por modo:
    /// PrestControl muestra pagarés personales; AutoControl, los de créditos
    /// vehiculares. Nunca se cruzan.
    /// </summary>
    public Task<IReadOnlyList<PrestamoResumen>> ObtenerContratosAsync(CancellationToken ct = default) =>
        _prestamos.ObtenerResumenesAsync(SesionActual.SoloVehicularesDelModo, ct);

    /// <summary>
    /// Reconstruye el pagaré de un préstamo existente (para verlo o reimprimirlo).
    /// Toma las cuotas TAL COMO están guardadas: el contrato refleja el préstamo
    /// original, no un recálculo.
    /// </summary>
    public async Task<PagareImpreso> ArmarPagareAsync(long prestamoId, CancellationToken ct = default)
    {
        var prestamo = await _prestamos.ObtenerPorIdAsync(prestamoId, ct)
            ?? throw new InvalidOperationException($"No existe el préstamo con id {prestamoId}.");
        var cliente = await _clientes.ObtenerPorIdAsync(prestamo.ClienteId, ct);
        var cuotas = await _prestamos.ObtenerCuotasAsync(prestamoId, ct);

        return ArmarPagare(prestamo, cliente, cuotas);
    }

    /// <summary>
    /// Reconstruye el PAGARÉ NOTARIAL de un préstamo existente (044).
    ///
    /// Los datos salen de tres lados y en este orden: lo que se cargó en el
    /// préstamo manda; lo que quedó vacío cae a Configuración; lo que no está en
    /// ninguno de los dos sale en blanco para llenarse a mano. Es a propósito —
    /// con un notario se trabaja así, y bloquear la impresión por un campo vacío
    /// sería peor que imprimir el acta incompleta.
    /// </summary>
    public async Task<PagareNotarialImpreso> ArmarNotarialAsync(long prestamoId,
        CancellationToken ct = default)
    {
        var prestamo = await _prestamos.ObtenerPorIdAsync(prestamoId, ct)
            ?? throw new InvalidOperationException($"No existe el préstamo con id {prestamoId}.");
        var cliente = await _clientes.ObtenerPorIdAsync(prestamo.ClienteId, ct);
        var cuotas = await _prestamos.ObtenerCuotasAsync(prestamoId, ct);
        // La copia congelada manda. Sin copia se usa la configuración vigente:
        // es lo único posible para los préstamos anteriores a 045.
        var congelada = await _actas.ObtenerAsync(prestamoId, ct);

        return Componer(prestamo, cliente, cuotas, congelada);
    }

    /// <summary>
    /// True si este préstamo tiene copia congelada del acta, o sea que
    /// reimprimirlo da exactamente el mismo papel que se firmó.
    /// </summary>
    public async Task<bool> TieneActaCongeladaAsync(long prestamoId, CancellationToken ct = default) =>
        await _actas.ObtenerAsync(prestamoId, ct) is not null;

    /// <summary>Reemplaza la copia congelada (corrección desde el detalle).</summary>
    public Task GuardarActaAsync(long prestamoId, DatosNotariales acta,
        CancellationToken ct = default) =>
        _actas.GuardarAsync(prestamoId, acta, ct);

    /// <summary>
    /// Los dos documentos de un préstamo de una sola pasada, para no ir a la
    /// base tres veces cuando la pantalla los necesita a los dos (el combinado
    /// es el notarial más la tabla del pagaré).
    /// </summary>
    public async Task<(PagareImpreso Pagare, PagareNotarialImpreso Notarial)> ArmarTodoAsync(
        long prestamoId, CancellationToken ct = default)
    {
        var prestamo = await _prestamos.ObtenerPorIdAsync(prestamoId, ct)
            ?? throw new InvalidOperationException($"No existe el préstamo con id {prestamoId}.");
        var cliente = await _clientes.ObtenerPorIdAsync(prestamo.ClienteId, ct);
        var cuotas = await _prestamos.ObtenerCuotasAsync(prestamoId, ct);

        var congelada = await _actas.ObtenerAsync(prestamoId, ct);
        var notarial = Componer(prestamo, cliente, cuotas, congelada);
        return (notarial.Deuda, notarial);
    }

    /// <summary>
    /// Arma el acta con datos ya en memoria. La usa la vista previa de Nuevo
    /// Préstamo, que todavía no tiene préstamo guardado.
    /// </summary>
    /// <param name="actoExplicito">
    /// Las partes del acta tal como están escritas AHORA en el formulario. Si
    /// viene null se usan las de Configuración. Existe para que la vista previa
    /// muestre lo que el usuario acaba de tipear y no lo guardado: si tuviera
    /// que guardar primero para ver el cambio, el panel lateral no serviría.
    /// </param>
    public PagareNotarialImpreso ArmarNotarialBorrador(Prestamo borrador, Cliente? cliente,
        IReadOnlyList<CuotaCalculada> tabla, DatosNotariales? actoExplicito = null) =>
        Componer(borrador, cliente, [.. tabla.Select(c => new Cuota
        {
            NumeroCuota = c.NumeroCuota,
            FechaVencimiento = c.FechaVencimiento,
            Capital = c.Capital,
            Interes = c.Interes,
            MontoTotal = c.MontoTotal,
            SaldoDespues = c.SaldoDespues
        })], actoExplicito);

    private PagareNotarialImpreso Componer(Prestamo prestamo, Cliente? cliente,
        IReadOnlyList<Cuota> cuotas, DatosNotariales? actoExplicito = null)
    {
        var ordenadas = cuotas.OrderBy(c => c.NumeroCuota).ToList();
        var deuda = ArmarPagare(prestamo, cliente, ordenadas);

        var deudor = new ParteDelActo(
            Nombre: cliente?.NombreCompleto ?? "(cliente eliminado)",
            Cedula: cliente?.Cedula ?? string.Empty,
            Sexo: prestamo.DeudorSexo,
            Nacionalidad: Preferir(prestamo.DeudorNacionalidad, "dominicano"),
            EstadoCivil: prestamo.DeudorEstadoCivil ?? string.Empty,
            Ocupacion: prestamo.DeudorOcupacion ?? string.Empty,
            Domicilio: cliente?.Direccion ?? string.Empty);

        // De dónde salen las PARTES (notario, quien firma por la empresa y los
        // testigos): del acto explícito si vino, y si no de Configuración.
        //
        // El explícito es lo que el usuario tiene escrito en Nuevo Préstamo en
        // este momento, o —para un préstamo ya guardado— la copia congelada del
        // día que se firmó. Configuración es solo el respaldo.
        var partes = actoExplicito ?? DesdeConfiguracion();

        // Lo que es DEL PRÉSTAMO manda siempre: el deudor, el acto y folio, la
        // garantía y las condiciones salen de la fila guardada, nunca de los
        // valores generales.
        var acto = partes with
        {
            ActoNo = prestamo.ActoNo ?? string.Empty,
            FolioNo = prestamo.FolioNo ?? string.Empty,
            // Sin fecha de acto cargada se usa el día en que nació el préstamo:
            // es la fecha en la que de hecho se firmó.
            FechaActo = prestamo.FechaActo
                ?? DateOnly.FromDateTime(FechaNegocio.AUtcLocal(prestamo.CreatedAtUtc)),
            Municipio = Preferir(prestamo.MunicipioActo, partes.Municipio,
                _ajustes.MunicipioActo, _ajustes.CiudadNegocio),

            Deudor = deudor,

            CuotasParaExigibilidad = prestamo.CuotasExigibilidad
                ?? NoCero(partes.CuotasParaExigibilidad, _ajustes.CuotasParaExigibilidad),
            DiasDeGracia = prestamo.DiasGracia ?? partes.DiasDeGracia,
            MoraPorcentaje = prestamo.MoraPorcentaje ?? partes.MoraPorcentaje,

            Garantia = prestamo.Garantia ?? string.Empty,
            RegistroTitulos = Preferir(prestamo.RegistroTitulos, partes.RegistroTitulos,
                _ajustes.RegistroTitulos)
        };

        // La cuota que se escribe en el acta es la PACTADA, o sea la primera:
        // la última suele traer el ajuste de centavos del redondeo y decirla en
        // el contrato confundiría al deudor.
        var montoCuota = ordenadas.Count > 0 ? ordenadas[0].MontoTotal : 0m;
        var ultimoPago = ordenadas.Count > 0
            ? ordenadas[^1].FechaVencimiento
            : prestamo.FechaInicio;

        return new PagareNotarialImpreso(
            Deuda: deuda,
            Acto: acto,
            Modalidad: prestamo.Modalidad,
            FechaPrimerPago: prestamo.FechaInicio,
            FechaUltimoPago: ultimoPago,
            TasaMensual: prestamo.TasaInteres,
            MontoCuota: montoCuota,
            CantidadCuotas: ordenadas.Count > 0 ? ordenadas.Count : prestamo.PlazoCuotas);
    }

    /// <summary>
    /// Las partes del acta que viven en Configuración (notario, quien firma por
    /// la empresa, los testigos, el asiento social y las condiciones por
    /// defecto). Es lo que Nuevo Préstamo usa para precargar su formulario, y
    /// el respaldo cuando un préstamo no tiene copia congelada.
    /// </summary>
    public DatosNotariales DesdeConfiguracion() => new()
    {
        Municipio = Preferir(_ajustes.MunicipioActo, _ajustes.CiudadNegocio),

        Notario = new ParteDelActo(
            Nombre: _ajustes.NotarioNombre,
            Cedula: _ajustes.NotarioCedula,
            Nacionalidad: Preferir(_ajustes.NotarioNacionalidad, "dominicano"),
            EstadoCivil: _ajustes.NotarioEstadoCivil,
            Ocupacion: "abogado notario público",
            Domicilio: _ajustes.NotarioDomicilio),
        NotarioMatricula = _ajustes.NotarioMatricula,

        EmpresaDireccion = _ajustes.DireccionNegocio,
        Representante = new ParteDelActo(
            // Sin bloque de representante cargado se usa el nombre del
            // prestamista, que es el que ya venía en el pagaré común.
            Nombre: Preferir(_ajustes.RepresentanteNombre, _ajustes.Prestamista),
            Cedula: _ajustes.RepresentanteCedula,
            Sexo: (SexoPersona)_ajustes.RepresentanteSexo,
            Nacionalidad: Preferir(_ajustes.RepresentanteNacionalidad, "dominicano"),
            EstadoCivil: _ajustes.RepresentanteEstadoCivil,
            Ocupacion: _ajustes.RepresentanteOcupacion,
            Domicilio: _ajustes.RepresentanteDomicilio),

        CuotasParaExigibilidad = _ajustes.CuotasParaExigibilidad,
        DiasDeGracia = _ajustes.DiasDeGracia,
        MoraPorcentaje = _ajustes.MoraPorcentaje,
        RegistroTitulos = _ajustes.RegistroTitulos,

        Testigos =
        [
            new ParteDelActo(_ajustes.Testigo1Nombre, _ajustes.Testigo1Cedula,
                (SexoPersona)_ajustes.Testigo1Sexo, "dominicano",
                _ajustes.Testigo1EstadoCivil, _ajustes.Testigo1Ocupacion, _ajustes.Testigo1Domicilio),
            new ParteDelActo(_ajustes.Testigo2Nombre, _ajustes.Testigo2Cedula,
                (SexoPersona)_ajustes.Testigo2Sexo, "dominicano",
                _ajustes.Testigo2EstadoCivil, _ajustes.Testigo2Ocupacion, _ajustes.Testigo2Domicilio)
        ]
    };

    /// <summary>El valor, o el respaldo si vino en cero (campo sin llenar).</summary>
    private static int NoCero(int valor, int respaldo) => valor > 0 ? valor : respaldo;

    /// <summary>El primer texto con contenido, o cadena vacía.</summary>
    private static string Preferir(params string?[] candidatos) =>
        candidatos.FirstOrDefault(t => !string.IsNullOrWhiteSpace(t))?.Trim() ?? string.Empty;

    private PagareImpreso ArmarPagare(Prestamo prestamo, Cliente? cliente, IReadOnlyList<Cuota> cuotas) =>
        new(
            NombreNegocio: _ajustes.NombreNegocio,
            Prestamista: _ajustes.Prestamista,
            Ciudad: _ajustes.CiudadNegocio,
            Telefono: _ajustes.TelefonoNegocio,
            Email: _ajustes.EmailNegocio,
            Rnc: _ajustes.RncNegocio,
            DeudorNombre: cliente?.NombreCompleto ?? "(cliente eliminado)",
            DeudorCedula: string.IsNullOrWhiteSpace(cliente?.Cedula) ? "—" : cliente.Cedula,
            CodigoPrestamo: prestamo.Codigo,
            MontoPrestado: prestamo.MontoCapital,
            TasaTexto: PagareImpreso.FormatearTasa(prestamo.TasaInteres, prestamo.Modalidad),
            TotalAPagar: cuotas.Sum(c => c.MontoTotal),
            Cuotas: [.. cuotas.OrderBy(c => c.NumeroCuota).Select(c => new PagareCuota(
                c.NumeroCuota,
                c.FechaVencimiento.ToString(FormatoFecha, CulturaRd),
                c.MontoTotal))]);
}

