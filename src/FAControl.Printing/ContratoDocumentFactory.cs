using System.Windows.Documents;
using FAControl.Models;

namespace FAControl.Printing;

/// <summary>
/// Punto único para pedir cualquiera de los tres contratos (pedido del cliente
/// 2026-09-03). La interfaz elige un <see cref="TipoContrato"/> y no tiene que
/// saber qué fábrica lo arma.
///
/// Existe para que agregar un cuarto documento el día de mañana toque UN solo
/// switch, y no las cuatro pantallas que hoy imprimen contratos (Nuevo Préstamo,
/// el detalle del préstamo, el almacén de contratos y el archivado automático).
/// </summary>
public static class ContratoDocumentFactory
{
    /// <summary>
    /// El documento listo para el visor o la impresora.
    ///
    /// Se pide uno NUEVO cada vez a propósito: un FlowDocument tiene un solo
    /// padre lógico, así que el que está en pantalla no se puede mandar también
    /// a imprimir.
    /// </summary>
    public static FlowDocument Crear(TipoContrato tipo, PagareNotarialImpreso contrato) => tipo switch
    {
        TipoContrato.Pagare => PagareDocumentFactory.Crear(contrato.Deuda),
        TipoContrato.Notarial => PagareNotarialDocumentFactory.Crear(contrato),
        _ => PagareNotarialDocumentFactory.CrearCombinado(contrato)
    };

    /// <summary>Cómo se llama el trabajo en la cola de impresión y el PDF.</summary>
    public static string Descripcion(TipoContrato tipo, string codigoPrestamo) =>
        $"{TiposDeContrato.Nombre(tipo)} {codigoPrestamo}";
}
