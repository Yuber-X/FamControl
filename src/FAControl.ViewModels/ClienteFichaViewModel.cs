using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAControl.Common;
using FAControl.Models;
using FAControl.Services;
using Serilog;

namespace FAControl.ViewModels;

/// <summary>
/// Ficha de cliente (mockup 3): datos de contacto + cinco métricas
/// + sus préstamos. Desde aquí se edita, se elimina (soft delete protegido)
/// y se le abre un préstamo nuevo con el cliente preseleccionado.
/// </summary>
public partial class ClienteFichaViewModel : ObservableObject
{
    private readonly ClienteService _clientes;
    private readonly PrestamoService _prestamos;
    private readonly IDialogService _dialogos;
    private long _clienteId;

    public event Action<long>? EditarSolicitado;
    public event Action<long>? NuevoPrestamoSolicitado;
    public event Action<long>? PrestamoSeleccionado;
    public event Action? VolverSolicitado;

    public ClienteFichaViewModel(ClienteService clientes, PrestamoService prestamos, IDialogService dialogos)
    {
        _clientes = clientes;
        _prestamos = prestamos;
        _dialogos = dialogos;
    }

    public ObservableCollection<PrestamoFila> Prestamos { get; } = [];

    [ObservableProperty] private string _nombreCompleto = string.Empty;
    [ObservableProperty] private string _cedula = string.Empty;
    [ObservableProperty] private string _telefonoTexto = string.Empty;
    [ObservableProperty] private string _direccionTexto = string.Empty;
    [ObservableProperty] private string _emailTexto = string.Empty;
    [ObservableProperty] private string _notasTexto = string.Empty;
    [ObservableProperty] private string _clienteDesdeTexto = string.Empty;
    [ObservableProperty] private decimal _totalPrestado;
    [ObservableProperty] private decimal _totalCobrado;
    [ObservableProperty] private decimal _saldoPendiente;
    [ObservableProperty] private int _prestamosActivos;
    [ObservableProperty] private int _cuotasVencidas;
    [ObservableProperty] private bool _tienePrestamos;

    // ---------- Historial de buena conducta (pedido 2026-08-06) ----------
    // Lo primero que el prestamista quiere saber de alguien que vuelve a pedir:
    // "¿este ya me pagó antes, y cómo?". Todo se calcula de lo que ya está en la
    // base; no hay nada que cargar a mano.

    /// <summary>Clave de la calificación ("Excelente", "Riesgosa"…): el XAML la usa para el color.</summary>
    [ObservableProperty] private string _conductaClave = nameof(ConductaCliente.SinHistorial);
    /// <summary>Etiqueta grande: "Buen pagador", "Riesgoso"…</summary>
    [ObservableProperty] private string _conductaTitulo = string.Empty;
    /// <summary>Una línea con lo esencial: cuántas cuotas y qué tan puntual.</summary>
    [ObservableProperty] private string _conductaResumen = string.Empty;
    /// <summary>Los contratos: cuántos saldó, cuántos tiene abiertos, desde cuándo es cliente.</summary>
    [ObservableProperty] private string _conductaContratos = string.Empty;
    /// <summary>Los atrasos, o la aclaración de que nunca se atrasó.</summary>
    [ObservableProperty] private string _conductaAtrasos = string.Empty;
    [ObservableProperty] private int _conductaPorcentajeATiempo;

    public async Task CargarAsync(long clienteId)
    {
        try
        {
            _clienteId = clienteId;
            var cliente = await _clientes.ObtenerPorIdAsync(clienteId)
                ?? throw new InvalidOperationException("El cliente no existe o fue eliminado.");
            var metricas = await _clientes.ObtenerMetricasAsync(clienteId);
            var conducta = await _clientes.ObtenerConductaAsync(clienteId);
            var prestamos = await _prestamos.ObtenerResumenesAsync();

            NombreCompleto = cliente.NombreCompleto;
            Cedula = cliente.Cedula;
            TelefonoTexto = cliente.Telefono ?? "—";
            DireccionTexto = cliente.Direccion ?? "—";
            EmailTexto = cliente.Email ?? "—";
            NotasTexto = string.IsNullOrWhiteSpace(cliente.Notas) ? "—" : cliente.Notas;
            ClienteDesdeTexto = FechaNegocio.AUtcLocal(cliente.CreatedAtUtc).ToString(Textos.FormatoFecha, Textos.CulturaRd);

            TotalPrestado = metricas.TotalPrestado;
            TotalCobrado = metricas.TotalCobrado;
            SaldoPendiente = metricas.SaldoPendiente;
            PrestamosActivos = metricas.PrestamosActivos;
            CuotasVencidas = metricas.CuotasVencidas;

            MostrarConducta(conducta);

            Prestamos.Clear();
            foreach (var resumen in prestamos.Where(p => p.ClienteId == clienteId))
                Prestamos.Add(new PrestamoFila(resumen));
            TienePrestamos = Prestamos.Count > 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cargando la ficha del cliente {Id}", clienteId);
            _dialogos.MostrarError("Ficha de cliente", $"No se pudo cargar la ficha.\n\n{ex.Message}");
        }
    }

    /// <summary>
    /// Arma las tres líneas del historial. Se redacta acá, en el ViewModel, para
    /// que la View no tenga que decidir nada — solo pintar según ConductaClave.
    /// </summary>
    private void MostrarConducta(ClienteConducta c)
    {
        ConductaClave = c.Calificacion.ToString();
        ConductaTitulo = Textos.De(c.Calificacion);
        ConductaPorcentajeATiempo = c.PorcentajeATiempo;

        ConductaResumen = c.CuotasSaldadas == 0
            ? c.EsClienteConocido
                ? "Tiene préstamos abiertos pero todavía no terminó de pagar ninguna cuota."
                : "Primera vez que se le presta: no hay historial de pago."
            : $"Pagó {c.CuotasATiempo} de {c.CuotasSaldadas} cuotas en fecha ({c.PorcentajeATiempo}% a tiempo).";

        ConductaContratos = c.EsClienteConocido
            ? $"Préstamos: {c.PrestamosTotales} en total · {c.PrestamosSaldados} saldados · " +
              $"{c.PrestamosActivos} activos" +
              (c.PrestamosCancelados > 0 ? $" · {c.PrestamosCancelados} cancelados" : string.Empty) +
              (c.PrimerPrestamo is { } desde
                  ? $" · cliente desde {desde.ToString(Textos.FormatoFecha, Textos.CulturaRd)}"
                  : string.Empty)
            : "Todavía no se le ha prestado.";

        if (c.CuotasVencidasHoy > 0)
        {
            // Lo de hoy manda sobre el promedio histórico: da igual que haya sido
            // buen pagador si en este momento debe.
            ConductaAtrasos = $"⚠️ Hoy tiene {c.CuotasVencidasHoy} " +
                              (c.CuotasVencidasHoy == 1 ? "cuota vencida" : "cuotas vencidas") + " sin pagar.";
        }
        else if (c.CuotasTarde == 0)
        {
            ConductaAtrasos = c.CuotasSaldadas == 0
                ? string.Empty
                : "Nunca se atrasó: todas las cuotas que pagó fueron en fecha o antes.";
        }
        else
        {
            ConductaAtrasos = $"Se atrasó en {c.CuotasTarde} " +
                              (c.CuotasTarde == 1 ? "cuota" : "cuotas") +
                              $" · promedio {c.DiasPromedioAtraso} " +
                              (c.DiasPromedioAtraso == 1 ? "día" : "días") +
                              $" · peor atraso {c.PeorAtrasoDias} días.";
        }
    }

    [RelayCommand]
    private void Editar() => EditarSolicitado?.Invoke(_clienteId);

    [RelayCommand]
    private void NuevoPrestamo() => NuevoPrestamoSolicitado?.Invoke(_clienteId);

    [RelayCommand]
    private void VerPrestamo(PrestamoFila? fila)
    {
        if (fila is not null)
            PrestamoSeleccionado?.Invoke(fila.Id);
    }

    [RelayCommand]
    private void Volver() => VolverSolicitado?.Invoke();

    [RelayCommand]
    private async Task EliminarAsync()
    {
        if (!_dialogos.Confirmar("Eliminar cliente",
            $"¿Eliminar a {NombreCompleto}?\n\n" +
            "Su historial de préstamos y pagos se conserva, pero ya no aparecerá en las listas."))
            return;

        try
        {
            await _clientes.EliminarAsync(_clienteId);
            _dialogos.Informar("Cliente eliminado", $"{NombreCompleto} fue eliminado.");
            VolverSolicitado?.Invoke();
        }
        catch (InvalidOperationException ex)
        {
            // Regla de negocio (préstamos activos): mensaje claro, sin stack
            _dialogos.MostrarError("Eliminar cliente", ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error eliminando el cliente {Id}", _clienteId);
            _dialogos.MostrarError("Eliminar cliente", $"No se pudo eliminar el cliente.\n\n{ex.Message}");
        }
    }
}
