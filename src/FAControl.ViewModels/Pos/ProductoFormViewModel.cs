// Portado de POS500.ViewModels el 2026-07-30 al integrar el punto de venta a la
// suite. Usa el SesionActual, los permisos y el IDialogService de FAControl.
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAControl.Common;
using FAControl.Models.Pos;
using FAControl.Services.Pos;
using Serilog;

namespace FAControl.ViewModels.Pos;

/// <summary>
/// Formulario de producto (nuevo y edición). Precio y cantidad llegan como
/// texto desde la UI y se validan aquí (decimal SIEMPRE para dinero).
/// </summary>
public partial class ProductoFormViewModel : ObservableObject
{
    private static readonly CultureInfo CulturaDo = CultureInfo.GetCultureInfo("es-DO");
    private readonly ProductoService _servicio;
    private readonly IDialogService _dialogos;
    private long? _productoId; // null = nuevo

    public event Action<long>? Guardado;
    public event Action? Cancelado;

    public ProductoFormViewModel(ProductoService servicio, IDialogService dialogos)
    {
        _servicio = servicio;
        _dialogos = dialogos;
    }

    [ObservableProperty] private string _titulo = "Nuevo producto";
    [ObservableProperty] private string _codigo = string.Empty;
    [ObservableProperty] private string _nombre = string.Empty;
    [ObservableProperty] private string _precioTexto = string.Empty;
    [ObservableProperty] private string _cantidadTexto = "0";
    [ObservableProperty] private string _descripcion = string.Empty;
    [ObservableProperty] private DateTime? _fechaCaducidad;
    [ObservableProperty] private string _mensajeError = string.Empty;
    [ObservableProperty] private bool _ocupado;

    public void PrepararNuevo()
    {
        _productoId = null;
        Titulo = "Nuevo producto";
        Codigo = Nombre = PrecioTexto = Descripcion = string.Empty;
        CantidadTexto = "0";
        FechaCaducidad = null;
        MensajeError = string.Empty;
    }

    public async Task PrepararEdicionAsync(long productoId)
    {
        var producto = await _servicio.ObtenerPorIdAsync(productoId)
            ?? throw new InvalidOperationException("El producto no existe o fue eliminado.");

        _productoId = productoId;
        Titulo = $"Editar producto — {producto.Nombre}";
        Codigo = producto.Codigo ?? string.Empty;
        Nombre = producto.Nombre;
        PrecioTexto = producto.Precio.ToString("0.00", CulturaDo);
        CantidadTexto = producto.Cantidad.ToString(CulturaDo);
        Descripcion = producto.Descripcion ?? string.Empty;
        FechaCaducidad = producto.FechaCaducidad?.ToDateTime(TimeOnly.MinValue);
        MensajeError = string.Empty;
    }

    [RelayCommand]
    private async Task GuardarAsync()
    {
        if (!decimal.TryParse(PrecioTexto, NumberStyles.Number, CulturaDo, out var precio))
        {
            MensajeError = "El precio no es un número válido (ej: 150.00).";
            return;
        }
        if (!int.TryParse(CantidadTexto, NumberStyles.Integer, CulturaDo, out var cantidad))
        {
            MensajeError = "La cantidad debe ser un número entero.";
            return;
        }

        var datos = new ProductoDatos(Codigo, Nombre, precio, cantidad, Descripcion,
            FechaCaducidad is { } f ? DateOnly.FromDateTime(f) : null);
        try
        {
            Ocupado = true;
            MensajeError = string.Empty;

            long id;
            if (_productoId is null)
            {
                id = await _servicio.CrearAsync(datos);
                _dialogos.Informar("Producto creado", $"{datos.Nombre} se registró correctamente.");
            }
            else
            {
                id = _productoId.Value;
                await _servicio.ActualizarAsync(id, datos);
            }

            Guardado?.Invoke(id);
        }
        catch (ArgumentException ex)
        {
            MensajeError = ex.Message;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error guardando el producto");
            _dialogos.MostrarError("Guardar producto", $"No se pudo guardar el producto.\n\n{ex.Message}");
        }
        finally
        {
            Ocupado = false;
        }
    }

    [RelayCommand]
    private void Cancelar() => Cancelado?.Invoke();
}
