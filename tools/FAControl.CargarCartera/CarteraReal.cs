using FAControl.Models;

namespace FAControl.CargarCartera;

/// <summary>
/// Un préstamo del listado real, con el cliente que lo tomó.
/// </summary>
/// <param name="Cedula">Documento tal como lo entrega ClienteService (o el marcador de pendiente).</param>
/// <param name="Notas">Lo que hay que saber de ESTE contrato: dudas del papel, pagos observados.</param>
public record FilaCartera(
    string Cedula,
    string Nombre,
    string Apellido,
    string Telefono,
    string Direccion,
    decimal Capital,
    decimal TasaMensual,
    int Cuotas,
    MetodoAmortizacion Metodo,
    DateOnly FechaPrestamo,
    DateOnly PrimerPago,
    string Garantia,
    string Notas);

/// <summary>
/// La cartera REAL de préstamos de Familia Almonte, transcrita del PDF
/// "clientes reales para prestControl.pdf" que el cliente entregó el 29/07/2026.
///
/// REGLA DE ESTA TRANSCRIPCIÓN: no se inventa ni se corrige nada en silencio.
/// Donde el papel se contradice, se carga la lectura más defendible y la
/// contradicción queda escrita en las Notas del préstamo (se ven en la ficha) y
/// en el informe FAControl_CarteraReal_Informe_v1_2026-07-30.md.
///
/// Lo que NO se carga: los pagos ya hechos. El papel los lista de forma
/// irregular (fechas que no coinciden con la fecha de pago pactada, montos que
/// no cuadran con la cuota) y registrarlos a ojo ensuciaría la contabilidad
/// desde el día uno. Van en el informe para que Yuber los confirme con el
/// cliente y los cargue desde Cobros — que además es la mejor prueba del módulo.
/// </summary>
public static class CarteraReal
{
    /// <summary>Marcador para el único cliente sin cédula en el papel.</summary>
    public const string CedulaPendiente = "PENDIENTE-001";

    /// <summary>Apellido de los clientes que en el papel figuran solo con nombre.</summary>
    public const string ApellidoPendiente = "(pendiente)";

    public static readonly IReadOnlyList<FilaCartera> Filas =
    [
        // ---------- A RÉDITO (cuota fija dominicana) ----------

        new("123-0010892-0", "José Luis", "Luciano", "809-494-4144", "Piedra Blanca, Bonao",
            Capital: 150_000m, TasaMensual: 3m, Cuotas: 18,
            Metodo: MetodoAmortizacion.CuotaFija,
            FechaPrestamo: new DateOnly(2026, 6, 2),
            PrimerPago: new DateOnly(2026, 7, 2),
            Garantia: "Automóvil Suzuki",
            Notas: "Préstamo a rédito del 02/06/2026. Cuota 12,833 (8,333 capital + 4,500 interés). " +
                   "OJO: el listado anota un primer pago de 13,000 el 06/07/2026 — 167 más que la " +
                   "cuota y cuatro días después de la fecha pactada. CONFIRMAR con el cliente."),

        new("053-0044889-0", "Arley", "Mena", "829-554-9009", "Constanza",
            Capital: 30_000m, TasaMensual: 6m, Cuotas: 6,
            Metodo: MetodoAmortizacion.CuotaFija,
            FechaPrestamo: new DateOnly(2026, 7, 3),
            PrimerPago: new DateOnly(2026, 7, 30),
            Garantia: "Motor Nipponia",
            Notas: "Comerciante. Préstamo a rédito del 03/07/2026, paga los días 30 de cada mes. " +
                   "Cuota 6,800 (5,000 capital + 1,800 interés). El listado no anota ningún pago."),

        new("053-0030030-7", "Valentina", "Quiroz", "829-702-4878", "Sabina, Constanza",
            Capital: 20_000m, TasaMensual: 6m, Cuotas: 5,
            Metodo: MetodoAmortizacion.CuotaFija,
            FechaPrestamo: new DateOnly(2026, 7, 3),
            PrimerPago: new DateOnly(2026, 8, 3),
            Garantia: "Motor Nipponia",
            Notas: "Préstamo a rédito del 03/07/2026, paga el 3 de cada mes. Cuota 5,200 " +
                   "(4,000 capital + 1,200 interés). El listado no anota ningún pago."),

        // ---------- ABIERTOS (solo interés, capital abierto) ----------

        new("053-0038510-0", "Dionicio", ApellidoPendiente, "829-483-6798", "Sabina, Constanza",
            Capital: 1_100_000m, TasaMensual: 1.5m, Cuotas: 12,
            Metodo: MetodoAmortizacion.SoloInteres,
            FechaPrestamo: new DateOnly(2026, 5, 25),
            PrimerPago: new DateOnly(2026, 6, 25),
            Garantia: "Camión marca Isuzu",
            Notas: "FALTA EL APELLIDO en el listado. Interés 16,500 mensuales (el papel escribe " +
                   "'16,5000', que es 16,500 con un cero de más). El listado lo llama 'Amortizado' " +
                   "pero pone cuotas y abono a capital ABIERTOS: se cargó como abierto, que es lo " +
                   "que describe. Las 12 cuotas son proyección, no vencimiento pactado. " +
                   "Pago anotado: 07/07/2026 por 16,500."),

        new("402-2622469-5", "Miguel Ángel", ApellidoPendiente, "829-648-7604", "Carbimota, La Vega",
            Capital: 500_000m, TasaMensual: 2m, Cuotas: 12,
            Metodo: MetodoAmortizacion.SoloInteres,
            FechaPrestamo: new DateOnly(2026, 4, 16),
            PrimerPago: new DateOnly(2026, 5, 16),
            Garantia: "Vehículo",
            Notas: "FALTA EL APELLIDO en el listado. Abierto: 10,000 de interés mensual, monto fijo " +
                   "hasta los 6 meses. Pagos anotados: 14/05, junio y julio de 2026, 10,000 cada uno " +
                   "(solo interés). Las 12 cuotas son proyección."),

        new("402-2570404-4", "Grabiel", ApellidoPendiente, "829-986-6596", "Río Seco",
            Capital: 400_000m, TasaMensual: 3m, Cuotas: 12,
            Metodo: MetodoAmortizacion.SoloInteres,
            FechaPrestamo: new DateOnly(2026, 4, 17),
            PrimerPago: new DateOnly(2026, 5, 17),
            Garantia: "Camión marca Hyundai",
            Notas: "FALTA EL APELLIDO en el listado (¿Gabriel?). Abierto: 12,000 de interés mensual, " +
                   "fijo por 6 meses sin variación. Pago anotado: 17/05/2026 por 12,000."),

        new(CedulaPendiente, "Wendy", "Yocasta", "849-917-7310", "Constanza",
            Capital: 300_000m, TasaMensual: 2.5m, Cuotas: 12,
            Metodo: MetodoAmortizacion.SoloInteres,
            FechaPrestamo: new DateOnly(2026, 3, 17),
            PrimerPago: new DateOnly(2026, 4, 17),
            Garantia: "Vehículo CRV",
            Notas: "FALTA LA CÉDULA en el listado: se cargó el marcador PENDIENTE-001, hay que " +
                   "reemplazarlo. Abierto: 7,500 de interés mensual, fijo por 6 meses. " +
                   "Pagos anotados: 23/04/2026 (7,500), 25/05/2026 y 25/06/2026 de 15,000 cada uno " +
                   "= 7,500 interés + 7,500 CAPITAL. Es decir ya abonó 15,000 al capital: " +
                   "el saldo real sería 285,000, no 300,000. CONFIRMAR antes de cobrar."),

        new("402-3371392-0", "Jhoster", "Jassiel", "809-943-9743", "Constanza",
            Capital: 400_000m, TasaMensual: 4m, Cuotas: 12,
            Metodo: MetodoAmortizacion.SoloInteres,
            FechaPrestamo: new DateOnly(2025, 12, 24),
            PrimerPago: new DateOnly(2026, 1, 24),
            Garantia: "Yipeta Tacoma",
            Notas: "⚠ INCONSISTENCIA DEL LISTADO: dice capital 400,000 al 2% mensual, pero el " +
                   "interés que anota y que el cliente paga es 16,000 — que es el 4% de 400,000 " +
                   "(el 2% daría 8,000). Se cargó al 4% porque es lo que reproduce los 16,000 que " +
                   "figuran cobrados seis veces. Si en realidad el capital es 800,000 al 2%, hay " +
                   "que corregirlo. CONFIRMAR con el cliente antes de usarlo en firme. " +
                   "Además el papel dice 'paga el 1 de cada mes' pero todos los pagos son el 24. " +
                   "Pagos anotados: 24/01, 24/02, 24/03, 24/04, 24/05 y 24/06 de 2026, 16,000 c/u."),

        new("123-0015086-4", "Richard", "Ovalles", "849-342-6206", "Bonao",
            Capital: 1_000_000m, TasaMensual: 2m, Cuotas: 12,
            Metodo: MetodoAmortizacion.SoloInteres,
            FechaPrestamo: new DateOnly(2025, 12, 18),
            PrimerPago: new DateOnly(2026, 1, 18),
            Garantia: "Vehículo Chevrolet",
            Notas: "El papel escribe el capital como 'RD$ 1,00,000.00'; se cargó 1,000,000 porque " +
                   "es el único monto cuyo 2% da los 20,000 de interés que anota. " +
                   "TELÉFONO DUDOSO: el listado pone '1649-342-6206'; se cargó 849-342-6206 " +
                   "(el 649 no es código de RD). CONFIRMAR. " +
                   "Pagos anotados: 19/01, 19/02, 20/03, 20/04, 20/05, 21/06 y 17/07 de 2026, " +
                   "20,000 cada uno (solo interés)."),

        new("053-0035805-7", "Mariana", "Sánchez", "809-624-3745", "Constanza",
            Capital: 200_000m, TasaMensual: 2m, Cuotas: 12,
            Metodo: MetodoAmortizacion.SoloInteres,
            FechaPrestamo: new DateOnly(2025, 9, 18),
            PrimerPago: new DateOnly(2025, 9, 24),
            Garantia: "Casa",
            Notas: "Abierto: 4,000 de interés mensual, sin abono a capital. " +
                   "⚠ El listado anota el primer pago el 26/08/2025, ANTES de la fecha del " +
                   "préstamo (18/09/2025), y dice que paga el 24 de cada mes. Se tomó el 24/09/2025 " +
                   "como primer vencimiento. CONFIRMAR las fechas con el cliente. " +
                   "'Hasta junio 2026 solamente pagó 4,000' por mes.")
    ];
}
