namespace FAControl.Common;

/// <summary>
/// Permite a Configuración forzar una revisión del aviso de vencimientos.
/// Vive en Common (como IDialogService) porque lo implementa la capa App
/// y lo consumen los ViewModels: la dependencia inversa no está permitida.
/// </summary>
public interface IAvisoVencidos
{
    /// <summary>
    /// Revisa y muestra el aviso AHORA, ignorando el "ya avisé hoy".
    /// Necesario tras restablecer los silenciados: si no, el aviso no
    /// reaparece hasta el día siguiente y el botón parece no hacer nada.
    /// </summary>
    Task RevisarAhoraAsync();
}
