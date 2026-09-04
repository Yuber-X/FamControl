using System.Diagnostics;
using System.Windows;
using FAControl.ViewModels;
using Microsoft.Win32;

namespace FAControl.Views;

/// <summary>
/// El "ver automático" que pidió el cliente (2026-07-27): al hacer doble clic
/// sobre un documento del expediente, esta ventana ofrece qué hacer con él —
/// Abrir, Guardar copia, Re-ubicar y Eliminar.
///
/// Re-ubicar y Eliminar SOLO aparecen para un Admin. La regla real vive en
/// ExpedienteService: aquí se ocultan para no ofrecer lo que va a ser rechazado.
/// </summary>
public partial class DocumentoAccionesWindow : Window
{
    private readonly ExpedienteViewModel _vm;
    private readonly DocumentoFila _fila;

    public DocumentoAccionesWindow(ExpedienteViewModel vm, DocumentoFila fila)
    {
        InitializeComponent();
        VentanaAjustable.Ajustar(this);
        _vm = vm;
        _fila = fila;

        IconoArchivo.Text = fila.Icono;
        TextoNombre.Text = fila.Nombre;
        TextoDetalle.Text = $"{fila.Familia} · {fila.TamanoTexto} · subido el {fila.FechaTexto} por {fila.SubidoPorTexto}";

        PanelAdmin.Visibility = vm.PuedeAdministrar ? Visibility.Visible : Visibility.Collapsed;
        if (vm.PuedeAdministrar)
        {
            ComboDestinos.ItemsSource = vm.Destinos;
            _ = CargarDestinosAsync();
        }
    }

    private async Task CargarDestinosAsync() => await _vm.CargarDestinosAsync();

    /// <summary>
    /// Abre el archivo con la app que Windows tenga asociada: un .docx abre
    /// Word, un .xlsx Excel, una imagen el visor de fotos. UseShellExecute es
    /// justamente lo que hace esa resolución; la extensión ya pasó por la lista
    /// blanca del servicio, así que no se puede disparar un ejecutable.
    /// </summary>
    private void Abrir_Click(object sender, RoutedEventArgs e)
    {
        var ruta = _vm.RutaParaAbrir(_fila);
        if (ruta is null)
            return;
        try
        {
            Process.Start(new ProcessStartInfo(ruta) { UseShellExecute = true });
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"Windows no pudo abrir el archivo.\n\n{ex.Message}",
                "Abrir documento", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Guardar_Click(object sender, RoutedEventArgs e)
    {
        var dialogo = new SaveFileDialog
        {
            Title = "¿Dónde guardo la copia?",
            FileName = _fila.Nombre,
            Filter = "Todos los archivos|*.*"
        };
        if (dialogo.ShowDialog(this) != true)
            return;

        _vm.GuardarCopia(_fila, dialogo.FileName);
        Close();
    }

    private async void Reubicar_Click(object sender, RoutedEventArgs e)
    {
        if (ComboDestinos.SelectedItem is not DestinoDocumento destino)
        {
            MessageBox.Show(this, "Elige a qué contrato hay que mover el documento.",
                "Re-ubicar", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (await _vm.ReubicarAsync(_fila, destino))
            Close();
    }

    private void Eliminar_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.EliminarCommand.CanExecute(_fila))
            _vm.EliminarCommand.Execute(_fila);
        Close();
    }

    private void Cerrar_Click(object sender, RoutedEventArgs e) => Close();
}
