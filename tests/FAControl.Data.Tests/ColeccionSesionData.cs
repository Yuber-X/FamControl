using Xunit;

namespace FAControl.Data.Tests;

/// <summary>
/// Colección para los tests de integración que tocan el estático SesionActual.
///
/// POR QUÉ EXISTE: mismo motivo que ColeccionSesion en Services.Tests —
/// SesionActual es global al proceso y xUnit corre las clases EN PARALELO, así
/// que dos clases que inician sesión se pisan (el Dispose de una le cierra la
/// sesión a la otra y la auditoría tira "No se puede auditar sin sesión activa").
/// Pasó al agregar FlujoVentaPlazosTests junto a FlujoPrestamoPagoTests.
///
/// TODA clase de integración que llame a SesionActual.Iniciar/Cerrar debe llevar
/// [Collection(Nombre)]. Sin ella, el fallo aparece recién con la siguiente clase.
/// </summary>
[CollectionDefinition(Nombre, DisableParallelization = true)]
public class ColeccionSesionData
{
    public const string Nombre = "SesionActual.Data";
}
