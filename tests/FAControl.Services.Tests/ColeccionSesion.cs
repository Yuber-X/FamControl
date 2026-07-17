using Xunit;

namespace FAControl.Services.Tests;

/// <summary>
/// Colección para los tests que tocan el estático SesionActual.
///
/// POR QUÉ EXISTE: SesionActual es global al proceso. xUnit corre las clases de
/// test EN PARALELO, así que dos clases que inicien sesión con roles distintos
/// se pisan entre sí y los tests fallan de forma intermitente (pasó en POS-500:
/// una clase logueada como Cajero le quitaba los permisos a otra que probaba el
/// flujo de venta). Marcar la clase con [Collection(Nombre)] la serializa.
///
/// TODA clase de test que llame a SesionActual.Iniciar/Cerrar debe llevar la
/// marca. Sin ella, el fallo no aparece hasta que alguien agrega la segunda.
/// </summary>
[CollectionDefinition(Nombre, DisableParallelization = true)]
public class ColeccionSesion
{
    public const string Nombre = "SesionActual";
}
