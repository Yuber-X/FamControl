using System.Windows.Controls;

namespace FAControl.Views;

/// <summary>
/// Expediente de un contrato de PrestControl. Sin code-behind propio: los
/// diálogos de archivo los maneja ExpedienteClienteView, que es el control
/// compartido con DealControl y Alquileres.
/// </summary>
public partial class ExpedienteContratoView : UserControl
{
    public ExpedienteContratoView() => InitializeComponent();
}
