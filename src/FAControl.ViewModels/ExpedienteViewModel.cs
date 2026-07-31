using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAControl.Common;
using FAControl.Models;
using FAControl.Services;
using Serilog;

namespace FAControl.ViewModels;

/// <summary>Documento del expediente, listo para pintar en la lista o en la cuadrícula.</summary>
public record DocumentoFila(DocumentoVenta Documento)
{
    public long Id => Documento.Id;
    public string Nombre => Documento.Nombre;
    public string Familia => Documento.Familia;
    /// <summary>Para qué sirve el papel, en castellano.</summary>
    public string TipoTexto => Documento.Tipo switch
    {
        TipoDocumento.FacturaEscaneada => "Factura firmada",
        TipoDocumento.Contrato => "Contrato",
        TipoDocumento.Identificacion => "Identificación",
        TipoDocumento.Pagare => "Pagaré",
        TipoDocumento.Intimacion => "Intimación",
        _ => "Otro"
    };

    public string TamanoTexto => Documento.TamanoTexto;
    public string FechaTexto =>
        FechaNegocio.AUtcLocal(Documento.CreatedAtUtc).ToString(Textos.FormatoFecha, Textos.CulturaRd);
    public string SubidoPorTexto =>
        string.IsNullOrWhiteSpace(Documento.SubidoPor) ? "—" : Documento.SubidoPor!;
    public bool EsFacturaEscaneada => Documento.Tipo == TipoDocumento.FacturaEscaneada;

    /// <summary>Glifo de Segoe MDL2 según la familia del archivo (vista de cuadrícula).</summary>
    public string Icono => Familia switch
    {
        "Imagen" => "",
        "Word" => "",
        "Excel" => "",
        "PDF" => "",
        "Comprimido" => "",
        "Texto" => "",
        _ => ""
    };
}

/// <summary>Contrato al que se puede re-ubicar un documento.</summary>
public record DestinoDocumento(DuenoExpediente Dueno, string Texto);

/// <summary>
/// Expediente digital del contrato (018, pedido del cliente 2026-07-27): acá se
/// guardan las facturas, documentos e imágenes que el cliente entregó para
/// comprar. Dos vistas (lista y cuadrícula), subida de varios archivos a la vez
/// y descarga de todo en un ZIP.
///
/// Vive colgado de VentaFinanciamientoViewModel: es una sección de esa pantalla,
/// no una página aparte.
/// </summary>
public partial class ExpedienteViewModel : ObservableObject
{
    private readonly ExpedienteService _expedientes;
    private readonly ReporteDealService _contratos;
    private readonly IDialogService _dialogos;
    /// <summary>De qué cuelga el expediente abierto: una venta o un préstamo (026).</summary>
    private DuenoExpediente _dueno = DuenoExpediente.DeVenta(0);

    public ExpedienteViewModel(ExpedienteService expedientes, ReporteDealService contratos,
        IDialogService dialogos)
    {
        _expedientes = expedientes;
        _contratos = contratos;
        _dialogos = dialogos;
    }

    public ObservableCollection<DocumentoFila> Documentos { get; } = [];
    /// <summary>Otros contratos, para "Re-ubicar" cuando el papel era de otro cliente.</summary>
    public ObservableCollection<DestinoDocumento> Destinos { get; } = [];

    [ObservableProperty] private bool _vistaLista = true;
    [ObservableProperty] private bool _sinDocumentos = true;
    [ObservableProperty] private string _resumenTexto = string.Empty;
    [ObservableProperty] private string _mensaje = string.Empty;
    [ObservableProperty] private bool _esError;
    [ObservableProperty] private bool _ocupado;

    public bool VistaCuadricula => !VistaLista;
    partial void OnVistaListaChanged(bool value) => OnPropertyChanged(nameof(VistaCuadricula));

    /// <summary>Eliminar y re-ubicar son exclusivos del Admin (pedido del cliente).</summary>
    public bool PuedeAdministrar => SesionActual.EsAdmin;

    /// <summary>
    /// Filtro para el selector de archivos de la View. Se arma acá y no en la
    /// View para que las extensiones permitidas tengan una sola fuente de
    /// verdad (la del servicio, que es quien las hace cumplir).
    /// </summary>
    public static string FiltroArchivos =>
        "Documentos e imágenes|" +
        string.Join(";", ExpedienteService.ExtensionesPermitidas.Select(x => "*" + x)) +
        "|Todos los archivos|*.*";

    public DuenoExpediente Dueno => _dueno;

    public Task CargarAsync(long ventaId) => CargarAsync(DuenoExpediente.DeVenta(ventaId));

    /// <summary>Carga el expediente de una venta o de un préstamo (026).</summary>
    public async Task CargarAsync(DuenoExpediente dueno)
    {
        _dueno = dueno;
        Mensaje = string.Empty;
        EsError = false;
        try
        {
            var documentos = await _expedientes.ObtenerAsync(dueno);
            Documentos.Clear();
            foreach (var documento in documentos)
                Documentos.Add(new DocumentoFila(documento));

            SinDocumentos = Documentos.Count == 0;
            ResumenTexto = Documentos.Count switch
            {
                0 => "Todavía no hay documentos guardados.",
                1 => "1 documento guardado",
                _ => $"{Documentos.Count} documentos guardados"
            };
            OnPropertyChanged(nameof(PuedeAdministrar));
        }
        catch (UnauthorizedAccessException ex)
        {
            EsError = true;
            Mensaje = ex.Message;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cargando el expediente de {Dueno}", dueno.Descripcion);
            EsError = true;
            Mensaje = $"No se pudo cargar el expediente.\n{ex.Message}";
        }
    }

    [RelayCommand]
    private void VerEnLista() => VistaLista = true;

    [RelayCommand]
    private void VerEnCuadricula() => VistaLista = false;

    /// <summary>
    /// Sube uno o varios archivos de una vez (la View abre el selector múltiple).
    /// Si alguno falla — extensión no permitida, demasiado grande — los demás
    /// igual entran y al final se avisa cuáles quedaron afuera.
    /// </summary>
    public async Task AgregarArchivosAsync(IEnumerable<string> rutas,
        TipoDocumento tipo = TipoDocumento.Otro)
    {
        var rechazados = new List<string>();
        var agregados = 0;

        try
        {
            Ocupado = true;
            foreach (var ruta in rutas)
            {
                try
                {
                    await _expedientes.AgregarAsync(_dueno, ruta, tipo);
                    agregados++;
                }
                catch (Exception ex) when (ex is InvalidOperationException or IOException)
                {
                    rechazados.Add($"• {Path.GetFileName(ruta)}: {ex.Message.Split('\n')[0]}");
                }
                catch (Exception ex)
                {
                    // Cualquier otra cosa (la base, el disco) se informa y se
                    // sigue con el resto de los archivos. Antes escapaba de este
                    // async void y CERRABA la aplicación sin decir una palabra:
                    // pasó de verdad al subir un contrato.
                    Log.Error(ex, "Error inesperado subiendo {Archivo} al expediente", ruta);
                    rechazados.Add($"• {Path.GetFileName(ruta)}: {ex.Message.Split('\n')[0]}");
                }
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            _dialogos.MostrarError("Expediente", ex.Message);
            return;
        }
        finally
        {
            Ocupado = false;
        }

        await CargarAsync(_dueno);

        if (rechazados.Count > 0)
        {
            _dialogos.MostrarError("Algunos archivos no se pudieron guardar",
                $"Se guardaron {agregados}. No entraron:\n\n{string.Join("\n", rechazados)}");
        }
        else
        {
            Mensaje = agregados == 1 ? "Documento guardado." : $"{agregados} documentos guardados.";
            EsError = false;
        }
    }

    // ---------- Factura escaneada (D6, pedido 2026-07-27) ----------

    /// <summary>
    /// La factura FIRMADA y escaneada de esa venta, si el usuario ya la subió.
    /// Null si todavía no hay ninguna.
    /// </summary>
    public async Task<DocumentoFila?> ObtenerFacturaEscaneadaAsync(long ventaId)
    {
        try
        {
            var documento = await _expedientes.ObtenerFacturaEscaneadaAsync(ventaId);
            return documento is null ? null : new DocumentoFila(documento);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error buscando la factura escaneada de la venta {Id}", ventaId);
            return null;
        }
    }

    /// <summary>
    /// Reemplaza la factura del sistema por la escaneada y firmada. No borra
    /// nada: la del sistema se sigue pudiendo imprimir, pero a partir de acá la
    /// que vale para el expediente es esta.
    /// </summary>
    public async Task<DocumentoFila?> ReemplazarFacturaAsync(long ventaId, string rutaArchivo)
    {
        try
        {
            Ocupado = true;
            var documento = await _expedientes.AgregarAsync(DuenoExpediente.DeVenta(ventaId), rutaArchivo,
                TipoDocumento.FacturaEscaneada, "Factura firmada escaneada por el usuario");
            if (_dueno == DuenoExpediente.DeVenta(ventaId))
                await CargarAsync(ventaId);
            return new DocumentoFila(documento);
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or IOException)
        {
            _dialogos.MostrarError("Reemplazar factura", ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error guardando la factura escaneada de la venta {Id}", ventaId);
            _dialogos.MostrarError("Reemplazar factura", $"No se pudo guardar.\n\n{ex.Message}");
            return null;
        }
        finally
        {
            Ocupado = false;
        }
    }

    /// <summary>Descarga TODO el expediente en un ZIP (la View elige dónde).</summary>
    public async Task ExportarZipAsync(string rutaZip)
    {
        try
        {
            Ocupado = true;
            var cantidad = await _expedientes.ExportarZipAsync(_dueno, rutaZip);
            _dialogos.Informar("Expediente exportado",
                $"Se guardaron {cantidad} documento(s) en:\n{rutaZip}");
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException)
        {
            _dialogos.MostrarError("Exportar expediente", ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error exportando el expediente de {Dueno}", _dueno.Descripcion);
            _dialogos.MostrarError("Exportar expediente", $"No se pudo crear el ZIP.\n\n{ex.Message}");
        }
        finally
        {
            Ocupado = false;
        }
    }

    /// <summary>
    /// Ruta real del archivo para que la View lo abra con su app de Windows.
    /// Devuelve null (y avisa) si el archivo ya no está en el disco.
    /// </summary>
    public string? RutaParaAbrir(DocumentoFila? fila)
    {
        if (fila is null)
            return null;
        try
        {
            return _expedientes.RutaParaAbrir(fila.Documento);
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException)
        {
            _dialogos.MostrarError("Abrir documento", ex.Message);
            return null;
        }
    }

    /// <summary>Guarda una copia donde el usuario eligió (la View pide la ruta).</summary>
    public void GuardarCopia(DocumentoFila fila, string rutaDestino)
    {
        try
        {
            _expedientes.GuardarCopia(fila.Documento, rutaDestino);
            _dialogos.Informar("Copia guardada", $"El documento se copió en:\n{rutaDestino}");
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or IOException)
        {
            _dialogos.MostrarError("Guardar documento", ex.Message);
        }
    }

    /// <summary>ELIMINAR — solo Admin.</summary>
    [RelayCommand]
    private async Task EliminarAsync(DocumentoFila? fila)
    {
        if (fila is null)
            return;
        if (!_dialogos.Confirmar("Eliminar documento",
            $"¿Quitar '{fila.Nombre}' del expediente?\n\n" +
            "Deja de aparecer en la lista y en el ZIP. Queda registrado quién lo quitó."))
            return;

        try
        {
            await _expedientes.EliminarAsync(fila.Id);
            await CargarAsync(_dueno);
            Mensaje = "Documento eliminado del expediente.";
            EsError = false;
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException)
        {
            _dialogos.MostrarError("Eliminar documento", ex.Message);
        }
    }

    /// <summary>
    /// Archiva en el expediente un documento que la app acaba de generar (el
    /// pagaré o la intimación al imprimirlos, 026). La Vista lo deja primero en
    /// un archivo temporal porque generar el PDF es cosa de WPF.
    ///
    /// No molesta al usuario si falla: imprimir ya funcionó y lo importante era
    /// eso. El fallo queda en el log.
    /// </summary>
    public async Task<bool> ArchivarImpresoAsync(DuenoExpediente dueno, string rutaArchivo,
        TipoDocumento tipo)
    {
        try
        {
            await _expedientes.AgregarAsync(dueno, rutaArchivo, tipo);
            if (_dueno == dueno)
                await CargarAsync(dueno);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "No se pudo archivar el documento impreso en {Dueno}", dueno.Descripcion);
            return false;
        }
    }

    /// <summary>Carga los contratos a los que se puede re-ubicar (solo Admin los ve).</summary>
    public async Task CargarDestinosAsync()
    {
        try
        {
            Destinos.Clear();
            // Re-ubicar solo tiene sentido dentro del mismo módulo: un papel de
            // una venta no se manda al expediente de un préstamo.
            if (_dueno.Tipo != TipoExpediente.Venta)
                return;

            foreach (var contrato in await _contratos.ObtenerContratosAsync())
            {
                var destino = DuenoExpediente.DeVenta(contrato.VentaId);
                if (destino == _dueno)
                    continue;   // el actual no es destino
                Destinos.Add(new DestinoDocumento(destino,
                    $"{contrato.Codigo} · {contrato.ClienteNombre} · {contrato.VehiculoDescripcion}"));
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cargando los contratos destino para re-ubicar");
        }
    }

    /// <summary>RE-UBICAR — solo Admin: el papel era de otro cliente.</summary>
    public async Task<bool> ReubicarAsync(DocumentoFila fila, DestinoDocumento destino)
    {
        try
        {
            await _expedientes.MoverAsync(fila.Id, destino.Dueno);
            await CargarAsync(_dueno);
            _dialogos.Informar("Documento re-ubicado",
                $"'{fila.Nombre}' pasó al expediente:\n{destino.Texto}");
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException)
        {
            _dialogos.MostrarError("Re-ubicar documento", ex.Message);
            return false;
        }
    }
}
