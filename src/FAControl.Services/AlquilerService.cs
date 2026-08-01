using FAControl.Common;
using FAControl.Data;
using FAControl.Models;
using Serilog;

namespace FAControl.Services;

/// <summary>
/// Rent a car (DealerControl). Alquilar es atómico: código AL-0001 + alquiler +
/// marcar el vehículo 'alquilado' + auditoría. La devolución cierra el alquiler
/// y libera el vehículo ('disponible'). Días y monto se calculan en el Service.
/// </summary>
public class AlquilerService
{
    private readonly AlquilerRepository _alquileres;
    private readonly VehiculoRepository _vehiculos;
    private readonly ClienteRepository _clientes;
    private readonly ContadorRepository _contador;
    private readonly ConexionFactory _factory;
    private readonly AuditoriaService _auditoria;

    public AlquilerService(AlquilerRepository alquileres, VehiculoRepository vehiculos,
        ClienteRepository clientes, ContadorRepository contador, ConexionFactory factory,
        AuditoriaService auditoria)
    {
        _alquileres = alquileres;
        _vehiculos = vehiculos;
        _clientes = clientes;
        _contador = contador;
        _factory = factory;
        _auditoria = auditoria;
    }

    public Task<IReadOnlyList<AlquilerResumen>> ObtenerResumenesAsync(CancellationToken ct = default)
    {
        ExigirLectura();
        return _alquileres.ObtenerResumenesAsync(ct);
    }

    /// <summary>Días facturables entre inicio y fin (mínimo 1 día).</summary>
    public static int CalcularDias(DateOnly inicio, DateOnly fin) =>
        Math.Max(1, fin.DayNumber - inicio.DayNumber);

    /// <summary>Registra un alquiler. Devuelve id y código AL-0001.</summary>
    public async Task<(long Id, string Codigo)> RegistrarAsync(AlquilerDatos datos, CancellationToken ct = default)
    {
        ExigirEscritura();

        var vehiculo = await _vehiculos.ObtenerPorIdAsync(datos.VehiculoId, ct)
            ?? throw new InvalidOperationException("El vehículo no existe o fue eliminado.");
        if (vehiculo.Estado != EstadoVehiculo.Disponible)
            throw new InvalidOperationException(
                $"El vehículo {vehiculo.Codigo} no está disponible para alquilar (estado actual: {vehiculo.Estado}).");

        var cliente = await _clientes.ObtenerPorIdAsync(datos.ClienteId, ct)
            ?? throw new InvalidOperationException("El cliente no existe o fue eliminado.");

        if (datos.FechaFin < datos.FechaInicio)
            throw new ArgumentException("La fecha de devolución no puede ser anterior al inicio.");
        if (datos.TarifaDia <= 0m)
            throw new ArgumentException("La tarifa por día debe ser mayor que cero.");

        var dias = CalcularDias(datos.FechaInicio, datos.FechaFin);
        var tarifa = Math.Round(datos.TarifaDia, 2, MidpointRounding.AwayFromZero);
        var total = Math.Round(tarifa * dias, 2, MidpointRounding.AwayFromZero);

        using var conexion = await _factory.AbrirAsync(ct);
        using var transaccion = await conexion.BeginTransactionAsync(ct);
        try
        {
            var numero = await _contador.SiguienteAsync(ContadorRepository.Alquiler, conexion, transaccion, ct);
            var codigo = $"AL-{numero:D4}";

            var alquiler = new Alquiler
            {
                Codigo = codigo,
                VehiculoId = datos.VehiculoId,
                ClienteId = datos.ClienteId,
                FechaInicio = datos.FechaInicio,
                FechaFin = datos.FechaFin,
                TarifaDia = tarifa,
                Dias = dias,
                MontoTotal = total,
                Estado = EstadoAlquiler.Activo,
                Notas = string.IsNullOrWhiteSpace(datos.Notas) ? null : datos.Notas.Trim()
            };

            var id = await _alquileres.InsertarAsync(alquiler, conexion, transaccion, ct);
            await _vehiculos.CambiarEstadoAsync(datos.VehiculoId, EstadoVehiculo.Alquilado, conexion, transaccion, ct);
            await _auditoria.RegistrarEnTransaccionAsync(AccionAuditoria.Crear, DbNames.Alquiler, id,
                $"Alquiler {codigo}: {vehiculo.Descripcion} a {cliente.NombreCompleto}, " +
                $"{dias} día(s) × {tarifa:N2} = {total:N2} DOP", conexion, transaccion, ct);

            await transaccion.CommitAsync(ct);
            Log.Information("Alquiler {Codigo} (id {Id}) para cliente {Cliente}", codigo, id, cliente.Id);
            return (id, codigo);
        }
        catch
        {
            await transaccion.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    /// <summary>Trae el alquiler completo (pantalla de detalles, 031).</summary>
    public async Task<Alquiler?> ObtenerPorIdAsync(long id, CancellationToken ct = default)
    {
        ExigirLectura();
        return await _alquileres.ObtenerPorIdAsync(id, ct);
    }

    /// <summary>
    /// Cierra un alquiler activo (031). UNA sola operacion para las dos formas
    /// de terminar, como pidio el cliente ("con un solo btn seria suficiente"),
    /// pero preguntando cual es: por dentro las dos liberan el vehiculo, pero
    /// DEVUELTO es plata ganada y CANCELADO puede ser plata a devolver. Fundirlas
    /// a ciegas perderia esa diferencia justo en los reportes.
    ///
    /// Si el cliente devuelve TARDE o antes, los dias y el monto reales se
    /// recalculan y se guardan aparte de los pactados: sin eso el sistema
    /// seguiria mostrando el monto original como si nada hubiera cambiado.
    ///
    /// Es solo para Admin o para quien tenga el permiso alquileres_editar:
    /// cerrar libera un vehiculo y puede implicar devolver dinero.
    /// </summary>
    public async Task<ResultadoCierreAlquiler> CerrarAsync(CierreAlquilerDatos datos,
        CancellationToken ct = default)
    {
        ExigirEscritura();
        if (!SesionActual.EsAdmin && !SesionActual.TienePermiso(Permisos.AlquileresEditar))
            throw new UnauthorizedAccessException(
                "No tenes permiso para cerrar alquileres. Pediselo al administrador.");
        if (string.IsNullOrWhiteSpace(datos.Motivo))
            throw new ArgumentException(
                "Indica por que se cierra el alquiler: queda en el historial.", nameof(datos));

        var alquiler = await _alquileres.ObtenerPorIdAsync(datos.AlquilerId, ct)
            ?? throw new InvalidOperationException("El alquiler no existe.");
        if (alquiler.Estado != EstadoAlquiler.Activo)
            throw new InvalidOperationException(
                $"El alquiler {alquiler.Codigo} ya esta cerrado.");

        var cancelado = datos.Tipo == CierreAlquiler.Cancelado;

        // En una cancelacion no hay dias usados: el contrato no corrio. En una
        // devolucion se cuenta hasta la fecha real, que puede no ser la pactada.
        DateOnly? fechaDevolucion = cancelado ? null : (datos.FechaDevolucion ?? FechaNegocio.Hoy);
        var diasReales = cancelado ? 0 : CalcularDias(alquiler.FechaInicio, fechaDevolucion!.Value);
        // El monto sale de los TRAMOS del contrato (039). Con una sola tarifa da
        // exactamente lo mismo que antes; con renovaciones a otro precio, cada
        // dia se cobra a la tarifa que estaba vigente cuando se uso.
        var renovaciones = await _alquileres.ObtenerRenovacionesAsync(datos.AlquilerId, ct);
        var montoFinal = cancelado
            ? 0m
            : CalcularMonto(alquiler, renovaciones, diasReales);

        var nuevoEstado = cancelado ? EstadoAlquiler.Cancelado : EstadoAlquiler.Finalizado;

        using var conexion = await _factory.AbrirAsync(ct);
        using var transaccion = await conexion.BeginTransactionAsync(ct);
        try
        {
            var filas = await _alquileres.CerrarAsync(datos.AlquilerId, nuevoEstado, fechaDevolucion,
                datos.Motivo.Trim(), diasReales, montoFinal, conexion, transaccion, ct);
            if (filas == 0)
                throw new InvalidOperationException(
                    "El alquiler se cerro desde otra pantalla mientras tanto. " +
                    "Volve a abrirlo para ver como quedo.");

            // El vehiculo vuelve al inventario disponible.
            await _vehiculos.CambiarEstadoAsync(alquiler.VehiculoId, EstadoVehiculo.Disponible,
                conexion, transaccion, ct);

            var detalleMonto = cancelado
                ? ""
                : $" - {diasReales} dia(s) reales sobre {alquiler.Dias} pactado(s), " +
                  $"cobrar {montoFinal:N2} DOP (pactado {alquiler.MontoTotal:N2})";
            await _auditoria.RegistrarEnTransaccionAsync(
                cancelado ? AccionAuditoria.Anular : AccionAuditoria.Modificar,
                DbNames.Alquiler, datos.AlquilerId,
                $"Alquiler {alquiler.Codigo} {(cancelado ? "cancelado" : "finalizado (vehiculo devuelto)")}" +
                $"{detalleMonto}. Motivo: {datos.Motivo.Trim()}",
                conexion, transaccion, ct);

            await transaccion.CommitAsync(ct);
            Log.Information("Alquiler {Codigo} cerrado ({Estado}) por {Usuario}. Motivo: {Motivo}",
                alquiler.Codigo, nuevoEstado, SesionActual.Username, datos.Motivo);

            return new ResultadoCierreAlquiler(alquiler.Codigo, datos.Tipo,
                alquiler.Dias, diasReales, alquiler.MontoTotal, montoFinal);
        }
        catch
        {
            await transaccion.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    /// <summary>
    /// Corrige un alquiler ya registrado (031 - "asi si se produce un error de
    /// digitacion se pueda arreglar").
    ///
    /// Solo mientras siga ACTIVO. Una vez cerrado, los dias y el monto quedan
    /// como quedaron: ese contrato ya se liquido y el cliente pago sobre esos
    /// numeros. Corregir por detras haria que la caja del dia deje de cuadrar
    /// con lo que se cobro.
    ///
    /// Nunca se tocan el codigo, el vehiculo ni el cliente: el primero ya se
    /// emitio, y cambiar los otros dos no es corregir un tipeo, es otro alquiler.
    /// </summary>
    public async Task EditarAsync(EdicionAlquiler cambios, CancellationToken ct = default)
    {
        ExigirEscritura();
        if (!SesionActual.EsAdmin && !SesionActual.TienePermiso(Permisos.AlquileresEditar))
            throw new UnauthorizedAccessException("No tenes permiso para corregir alquileres.");
        if (string.IsNullOrWhiteSpace(cambios.Motivo))
            throw new ArgumentException(
                "Indica por que se corrige el alquiler: queda en el historial.", nameof(cambios));
        if (cambios.TarifaDia <= 0m)
            throw new ArgumentException("La tarifa por dia tiene que ser mayor que cero.", nameof(cambios));
        if (cambios.FechaFin < cambios.FechaInicio)
            throw new ArgumentException("La fecha de fin no puede ser anterior a la de inicio.", nameof(cambios));

        var alquiler = await _alquileres.ObtenerPorIdAsync(cambios.AlquilerId, ct)
            ?? throw new InvalidOperationException("El alquiler no existe.");
        if (alquiler.Estado != EstadoAlquiler.Activo)
            throw new InvalidOperationException(
                $"El alquiler {alquiler.Codigo} ya esta cerrado y liquidado: sus dias y su monto no se " +
                "pueden cambiar. Si hay un error, registra un alquiler nuevo con los datos correctos.");

        // Con renovaciones de por medio (039) esta correccion ya no aplica:
        // recalcula el total como tarifa x dias sobre TODO el periodo, y eso
        // borraria los tramos que se pactaron a otro precio. Corregir un tipeo
        // de un contrato que ya se re-negocio no es corregir un tipeo.
        var yaRenovado = await _alquileres.ObtenerRenovacionesAsync(cambios.AlquilerId, ct);
        if (yaRenovado.Count > 0)
            throw new InvalidOperationException(
                $"El alquiler {alquiler.Codigo} tiene {yaRenovado.Count} renovacion(es): sus fechas y su " +
                "tarifa ya se re-pactaron y no se pueden corregir por detras, porque cada tramo tiene su " +
                "propio precio. Si hay que cambiar algo, hacelo con una renovacion nueva.");

        // Se anota el detalle ANTES de tocar el objeto: despues ya no queda el
        // valor viejo (Alquiler es una clase, no un record).
        var detalle = new List<string>();
        if (alquiler.FechaInicio != cambios.FechaInicio)
            detalle.Add($"inicio {alquiler.FechaInicio:dd/MM/yyyy} a {cambios.FechaInicio:dd/MM/yyyy}");
        if (alquiler.FechaFin != cambios.FechaFin)
            detalle.Add($"fin {alquiler.FechaFin:dd/MM/yyyy} a {cambios.FechaFin:dd/MM/yyyy}");
        if (alquiler.TarifaDia != cambios.TarifaDia)
            detalle.Add($"tarifa {alquiler.TarifaDia:N2} a {cambios.TarifaDia:N2}");
        if (alquiler.Notas != cambios.Notas)
            detalle.Add("notas");

        if (detalle.Count == 0)
            return;   // Nada cambio: no se ensucia la auditoria con un registro vacio

        alquiler.FechaInicio = cambios.FechaInicio;
        alquiler.FechaFin = cambios.FechaFin;
        alquiler.TarifaDia = cambios.TarifaDia;
        alquiler.Notas = cambios.Notas;
        // Dias y total son DERIVADOS: se recalculan con la misma cuenta del alta,
        // para que no puedan quedar contradiciendo a las fechas y la tarifa.
        alquiler.Dias = CalcularDias(cambios.FechaInicio, cambios.FechaFin);
        alquiler.MontoTotal = Math.Round(cambios.TarifaDia * alquiler.Dias, 2, MidpointRounding.AwayFromZero);

        using var conexion = await _factory.AbrirAsync(ct);
        using var transaccion = await conexion.BeginTransactionAsync(ct);
        try
        {
            await _alquileres.ActualizarDatosAsync(alquiler, conexion, transaccion, ct);
            await _auditoria.RegistrarEnTransaccionAsync(AccionAuditoria.Modificar, DbNames.Alquiler,
                cambios.AlquilerId,
                $"Alquiler {alquiler.Codigo} corregido ({string.Join(", ", detalle)}) - " +
                $"queda en {alquiler.Dias} dia(s) por {alquiler.MontoTotal:N2} DOP. " +
                $"Motivo: {cambios.Motivo.Trim()}",
                conexion, transaccion, ct);

            await transaccion.CommitAsync(ct);
            Log.Information("Alquiler {Codigo} corregido por {Usuario}: {Detalle}",
                alquiler.Codigo, SesionActual.Username, string.Join(", ", detalle));
        }
        catch
        {
            await transaccion.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    // ---------- Renovacion del alquiler (039) ----------

    /// <summary>
    /// El cliente sigue con el auto: se corre la fecha de devolucion (pedido del
    /// cliente 2026-08-01).
    ///
    /// POR QUE NO ES UNA EDICION
    /// Editar recalcula el total como tarifa x dias sobre todo el periodo. Si la
    /// renovacion viene a otro precio —que es justo lo que se pidio permitir—,
    /// eso le cambiaria el precio a los dias que el cliente YA uso y quizas ya
    /// pago. Cada renovacion se guarda como un TRAMO con su tarifa, y el
    /// contrato vale la suma de los tramos.
    ///
    /// El vehiculo no se toca: sigue alquilado, que es exactamente lo que
    /// significa que el cliente siga con el.
    /// </summary>
    public async Task<ResultadoRenovacion> RenovarAsync(RenovacionAlquiler datos,
        CancellationToken ct = default)
    {
        ExigirEscritura();
        if (!SesionActual.EsAdmin && !SesionActual.TienePermiso(Permisos.AlquileresEditar))
            throw new UnauthorizedAccessException(
                "No tenes permiso para renovar alquileres. Pediselo al administrador.");
        if (datos.TarifaDia <= 0m)
            throw new ArgumentException("La tarifa por dia tiene que ser mayor que cero.", nameof(datos));

        var alquiler = await _alquileres.ObtenerPorIdAsync(datos.AlquilerId, ct)
            ?? throw new InvalidOperationException("El alquiler no existe.");
        if (alquiler.Estado != EstadoAlquiler.Activo)
            throw new InvalidOperationException(
                $"El alquiler {alquiler.Codigo} ya esta cerrado: el vehiculo volvio al inventario. " +
                "Si el cliente se lo lleva otra vez, es un alquiler nuevo.");
        if (datos.FechaFinNueva <= alquiler.FechaFin)
            throw new ArgumentException(
                $"La nueva fecha de devolucion tiene que ser posterior al {alquiler.FechaFin:dd/MM/yyyy}, " +
                "que es hasta cuando va el contrato hoy.", nameof(datos));

        var renovaciones = await _alquileres.ObtenerRenovacionesAsync(datos.AlquilerId, ct);
        var tarifaVigente = TarifaVigente(alquiler, renovaciones);

        // Los dias del tramo nuevo se cuentan desde el dia SIGUIENTE al fin
        // anterior: el ultimo dia pactado ya esta cobrado en el tramo anterior,
        // contarlo dos veces le cobraria un dia de mas al cliente.
        var dias = datos.FechaFinNueva.DayNumber - alquiler.FechaFin.DayNumber;
        var monto = Math.Round(datos.TarifaDia * dias, 2, MidpointRounding.AwayFromZero);

        var renovacion = new AlquilerRenovacion
        {
            AlquilerId = datos.AlquilerId,
            FechaFinAnterior = alquiler.FechaFin,
            FechaFinNueva = datos.FechaFinNueva,
            TarifaDia = datos.TarifaDia,
            Dias = dias,
            Monto = monto,
            Notas = string.IsNullOrWhiteSpace(datos.Notas) ? null : datos.Notas.Trim()
        };

        var diasTotales = alquiler.Dias + dias;
        var montoTotal = alquiler.MontoTotal + monto;

        using var conexion = await _factory.AbrirAsync(ct);
        using var transaccion = await conexion.BeginTransactionAsync(ct);
        try
        {
            var filas = await _alquileres.RenovarAsync(datos.AlquilerId, datos.FechaFinNueva,
                diasTotales, montoTotal, conexion, transaccion, ct);
            if (filas == 0)
                throw new InvalidOperationException(
                    "El alquiler se cerro desde otra pantalla mientras tanto. " +
                    "Volve a abrirlo para ver como quedo.");

            await _alquileres.InsertarRenovacionAsync(renovacion, conexion, transaccion, ct);

            var cambioTarifa = datos.TarifaDia != tarifaVigente;
            var detalleTarifa = cambioTarifa
                ? $" a {datos.TarifaDia:N2} DOP por dia (antes {tarifaVigente:N2})"
                : $" a la misma tarifa de {datos.TarifaDia:N2} DOP por dia";
            await _auditoria.RegistrarEnTransaccionAsync(AccionAuditoria.Modificar, DbNames.Alquiler,
                datos.AlquilerId,
                $"Alquiler {alquiler.Codigo} renovado: la devolucion pasa del " +
                $"{alquiler.FechaFin:dd/MM/yyyy} al {datos.FechaFinNueva:dd/MM/yyyy}, " +
                $"{dias} dia(s) mas{detalleTarifa} ({monto:N2} DOP). " +
                $"El contrato queda en {diasTotales} dia(s) por {montoTotal:N2} DOP.",
                conexion, transaccion, ct);

            await transaccion.CommitAsync(ct);
            Log.Information("Alquiler {Codigo} renovado hasta {Fecha} por {Usuario} (+{Dias} dias, {Monto})",
                alquiler.Codigo, datos.FechaFinNueva, SesionActual.Username, dias, monto);

            return new ResultadoRenovacion(alquiler.Codigo, alquiler.FechaFin, datos.FechaFinNueva,
                dias, datos.TarifaDia, monto, diasTotales, montoTotal)
            {
                CambioLaTarifa = cambioTarifa
            };
        }
        catch
        {
            await transaccion.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    /// <summary>Renovaciones del alquiler, en orden cronologico (pantalla de detalle).</summary>
    public async Task<IReadOnlyList<AlquilerRenovacion>> ObtenerRenovacionesAsync(long alquilerId,
        CancellationToken ct = default)
    {
        ExigirLectura();
        return await _alquileres.ObtenerRenovacionesAsync(alquilerId, ct);
    }

    /// <summary>
    /// La tarifa que rige HOY: la de la ultima renovacion, o la original si el
    /// contrato nunca se renovo. `alquiler.TarifaDia` guarda la del PRIMER tramo
    /// a proposito, para no perder la historia.
    /// </summary>
    public static decimal TarifaVigente(Alquiler alquiler,
        IReadOnlyList<AlquilerRenovacion> renovaciones) =>
        renovaciones.Count == 0 ? alquiler.TarifaDia : renovaciones[^1].TarifaDia;

    /// <summary>
    /// Lo que corresponde cobrar por <paramref name="diasUsados"/> dias, tramo
    /// por tramo (039).
    ///
    /// Cada dia se cobra a la tarifa que estaba vigente cuando se uso: los
    /// primeros dias a la tarifa original, los del tramo renovado a la suya. Si
    /// el cliente devuelve MAS TARDE que el ultimo tramo, esos dias de mas van a
    /// la tarifa vigente, que es la que el cliente acepto ultimo.
    ///
    /// Sin renovaciones da exactamente tarifa x dias, igual que antes.
    ///
    /// El redondeo es UNO SOLO, al final: redondear tramo por tramo acumularia
    /// centavos que no cuadrarian con la suma de los cobros.
    /// </summary>
    public static decimal CalcularMonto(Alquiler alquiler,
        IReadOnlyList<AlquilerRenovacion> renovaciones, int diasUsados)
    {
        if (diasUsados <= 0)
            return 0m;

        // Tramo base: hasta donde iba el contrato antes de la primera renovacion.
        var diasBase = renovaciones.Count == 0
            ? alquiler.Dias
            : CalcularDias(alquiler.FechaInicio, renovaciones[0].FechaFinAnterior);

        var total = 0m;
        var restantes = diasUsados;

        var enTramo = Math.Min(restantes, diasBase);
        total += alquiler.TarifaDia * enTramo;
        restantes -= enTramo;

        foreach (var r in renovaciones)
        {
            if (restantes <= 0)
                break;
            enTramo = Math.Min(restantes, r.Dias);
            total += r.TarifaDia * enTramo;
            restantes -= enTramo;
        }

        // Devolvio despues del ultimo tramo: los dias de mas, a la tarifa vigente.
        if (restantes > 0)
            total += TarifaVigente(alquiler, renovaciones) * restantes;

        return Math.Round(total, 2, MidpointRounding.AwayFromZero);
    }

    private static void ExigirLectura()
    {
        if (!SesionActual.TienePermiso(Permisos.Alquileres))
            throw new UnauthorizedAccessException("No tenés permiso para ver los alquileres.");
    }

    private static void ExigirEscritura()
    {
        if (!SesionActual.TienePermiso(Permisos.Alquileres))
            throw new UnauthorizedAccessException("No tenés permiso para gestionar alquileres.");
    }

    // ---------- Cobros del alquiler (034) ----------

    /// <summary>
    /// Como va el cobro: cuanto hay que cobrar, cuanto entro y que falta.
    ///
    /// EL MONTO A COBRAR CAMBIA al cerrar el contrato: mientras esta abierto se
    /// mide contra lo PACTADO, que es lo mejor que se sabe; una vez cerrado,
    /// contra lo que realmente correspondio (031), que puede ser mas si el
    /// cliente devolvio tarde. Un alquiler cancelado no se cobra: su monto a
    /// cobrar es cero y lo que se haya recibido queda como saldo a favor.
    /// </summary>
    public async Task<EstadoCobroAlquiler> ObtenerEstadoCobroAsync(long alquilerId,
        CancellationToken ct = default)
    {
        ExigirLectura();

        var alquiler = await _alquileres.ObtenerPorIdAsync(alquilerId, ct)
            ?? throw new InvalidOperationException("El alquiler no existe.");
        var pagos = await _alquileres.ObtenerPagosAsync(alquilerId, ct);

        var aCobrar = alquiler.Estado switch
        {
            EstadoAlquiler.Cancelado => 0m,
            EstadoAlquiler.Finalizado => alquiler.MontoFinal ?? alquiler.MontoTotal,
            _ => alquiler.MontoTotal
        };

        var cobrado = pagos.Sum(p => p.Monto);
        return new EstadoCobroAlquiler(aCobrar, cobrado, pagos,
            CalcularCalendario(alquiler, aCobrar, cobrado));
    }

    /// <summary>
    /// Parte el periodo del alquiler en cuotas MENSUALES y reparte sobre ellas
    /// lo ya cobrado (037).
    ///
    /// NO se guarda: se calcula del periodo y la tarifa, como el semaforo de un
    /// prestamo. Guardarlo obligaria a mantenerlo sincronizado cada vez que se
    /// corrige una fecha o se cierra el contrato, y seria una segunda fuente de
    /// verdad para algo que ya se deduce.
    ///
    /// El ultimo mes se ajusta para que la suma de las cuotas de EXACTAMENTE el
    /// monto a cobrar: repartir por division deja centavos sueltos.
    /// </summary>
    private static IReadOnlyList<CuotaAlquiler> CalcularCalendario(
        Alquiler alquiler, decimal aCobrar, decimal cobrado)
    {
        if (aCobrar <= 0m)
            return [];

        // El periodo real: hasta la devolucion si ya volvio, si no hasta lo pactado
        var fin = alquiler.FechaDevolucion ?? alquiler.FechaFin;
        if (fin <= alquiler.FechaInicio)
            fin = alquiler.FechaInicio.AddDays(1);

        var tramos = new List<(DateOnly Desde, DateOnly Hasta)>();
        var desde = alquiler.FechaInicio;
        while (desde < fin)
        {
            // AddMonths respeta el fin de mes: del 31/01 pasa al 28/02, que es
            // lo que espera cualquiera que cobre "el mismo dia del mes".
            var siguiente = desde.AddMonths(1);
            if (siguiente > fin) siguiente = fin;
            tramos.Add((desde, siguiente));
            desde = siguiente;
        }

        var totalDias = Math.Max(1, fin.DayNumber - alquiler.FechaInicio.DayNumber);
        var cuotas = new List<CuotaAlquiler>(tramos.Count);
        var restanteCobrado = cobrado;
        var acumuladoMonto = 0m;

        for (var i = 0; i < tramos.Count; i++)
        {
            var (d, h) = tramos[i];
            var dias = Math.Max(1, h.DayNumber - d.DayNumber);
            var esUltima = i == tramos.Count - 1;

            // La ultima absorbe el redondeo, para que la suma cierre exacta
            var monto = esUltima
                ? aCobrar - acumuladoMonto
                : Math.Round(aCobrar * dias / totalDias, 2, MidpointRounding.AwayFromZero);
            acumuladoMonto += monto;

            // Lo cobrado se aplica en cascada: satura la primera cuota, sigue
            // con la segunda. Mismo criterio que un abono de venta.
            var pagado = Math.Min(restanteCobrado, monto);
            restanteCobrado -= pagado;

            cuotas.Add(new CuotaAlquiler(i + 1, d, h, dias, monto, pagado));
        }
        return cuotas;
    }

    /// <summary>
    /// Registra un cobro contra el alquiler (034 — pedido del cliente: "se
    /// necesita un grid y su propia forma de registrar cobros").
    ///
    /// En la practica el alquiler se paga en dos veces: un adelanto al retirar
    /// el vehiculo y el resto al devolverlo. Antes no habia donde anotar eso.
    ///
    /// ATOMICO: reserva del numero de recibo + insercion + auditoria en UNA
    /// transaccion. Un rollback no quema un numero de talonario.
    ///
    /// NO se puede cobrar mas de lo que falta. El tope se lee DENTRO de la
    /// transaccion y con el alquiler bloqueado: sin eso, dos cajeros cobrando a
    /// la vez veri­an cada uno el saldo de antes y entre los dos se pasari­an.
    /// </summary>
    public async Task<AlquilerPago> RegistrarCobroAsync(CobroAlquiler cobro,
        CancellationToken ct = default)
    {
        ExigirEscritura();
        if (cobro.Monto <= 0m)
            throw new ArgumentException("El monto del cobro tiene que ser mayor que cero.", nameof(cobro));

        var alquiler = await _alquileres.ObtenerPorIdAsync(cobro.AlquilerId, ct)
            ?? throw new InvalidOperationException("El alquiler no existe.");
        if (alquiler.Estado == EstadoAlquiler.Cancelado)
            throw new InvalidOperationException(
                $"El alquiler {alquiler.Codigo} esta cancelado: no se le cobra. " +
                "Si el cliente ya habia pagado, eso se maneja como devolucion.");

        var aCobrar = alquiler.Estado == EstadoAlquiler.Finalizado
            ? alquiler.MontoFinal ?? alquiler.MontoTotal
            : alquiler.MontoTotal;

        var monto = Math.Round(cobro.Monto, 2, MidpointRounding.AwayFromZero);

        using var conexion = await _factory.AbrirAsync(ct);
        using var transaccion = await conexion.BeginTransactionAsync(ct);
        try
        {
            var cobrado = await _alquileres.ObtenerCobradoParaActualizarAsync(
                cobro.AlquilerId, conexion, transaccion, ct);
            var falta = aCobrar - cobrado;

            if (falta <= 0m)
                throw new InvalidOperationException(
                    $"El alquiler {alquiler.Codigo} ya esta saldado: no queda nada por cobrar.");
            if (monto > falta)
                throw new InvalidOperationException(
                    $"No se puede cobrar {monto:N2} DOP: a este alquiler solo le faltan " +
                    $"{falta:N2} DOP.");

            var numero = await _contador.SiguienteAsync(
                ContadorRepository.ReciboAlquiler, conexion, transaccion, ct);
            var pago = new AlquilerPago
            {
                AlquilerId = cobro.AlquilerId,
                NumeroRecibo = $"RA-{numero:D6}",
                Monto = monto,
                MetodoPago = cobro.Metodo,
                Notas = cobro.Notas
            };
            pago.Id = await _alquileres.InsertarPagoAsync(pago, conexion, transaccion, ct);

            var saldado = cobrado + monto >= aCobrar;
            await _auditoria.RegistrarEnTransaccionAsync(AccionAuditoria.Crear,
                DbNames.AlquilerPago, pago.Id,
                $"Cobro {pago.NumeroRecibo} del alquiler {alquiler.Codigo}: {monto:N2} DOP " +
                $"({cobro.Metodo}). Falta {(aCobrar - cobrado - monto):N2} DOP" +
                (saldado ? " — alquiler SALDADO" : ""),
                conexion, transaccion, ct);

            await transaccion.CommitAsync(ct);
            pago.FechaPagoUtc = DateTime.UtcNow;
            pago.CobradoPor = SesionActual.Nombre;

            Log.Information("Cobro {Recibo} del alquiler {Codigo}: {Monto} DOP",
                pago.NumeroRecibo, alquiler.Codigo, monto);
            return pago;
        }
        catch
        {
            await transaccion.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
