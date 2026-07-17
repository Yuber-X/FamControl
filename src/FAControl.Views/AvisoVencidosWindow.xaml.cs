using System.Globalization;
using System.Windows;
using FAControl.Models;

namespace FAControl.Views;

/// <summary>
/// Aviso de clientes que se pasaron de su fecha de pago (estilo notificador
/// del POS-400). Única acción: OK. Cada cliente puede silenciarse
/// individualmente con su checkbox.
/// </summary>
public partial class AvisoVencidosWindow : Window
{
    /// <summary>Fila de la mini-lista, con su checkbox de silenciado.</summary>
    private sealed class Fila
    {
        private static readonly CultureInfo CulturaRd = CultureInfo.GetCultureInfo("es-DO");

        public required ClienteVencido Datos { get; init; }
        public bool Silenciar { get; set; }

        public string Nombre => Datos.NombreCompleto;
        public string Detalle =>
            $"{Datos.CuotasVencidas} cuota(s) vencida(s) · RD$ {Datos.MontoVencido.ToString("N2", CulturaRd)}" +
            $" · desde el {Datos.PrimerVencimiento:dd/MM/yyyy}";
    }

    private readonly List<Fila> _filas;

    public AvisoVencidosWindow(IReadOnlyList<ClienteVencido> vencidos)
    {
        InitializeComponent();
        _filas = vencidos.Select(v => new Fila { Datos = v }).ToList();
        Lista.ItemsSource = _filas;
        TextoResumen.Text = vencidos.Count == 1
            ? "Un cliente acaba de pasarse de su fecha de pago:"
            : $"{vencidos.Count} clientes se pasaron de su fecha de pago:";
    }

    /// <summary>Ids marcados con "No volver a preguntar por este cliente".</summary>
    public IReadOnlyList<long> ObtenerSilenciados() =>
        _filas.Where(f => f.Silenciar).Select(f => f.Datos.ClienteId).ToList();

    private void BotonOk_Click(object sender, RoutedEventArgs e) => Close();
}
