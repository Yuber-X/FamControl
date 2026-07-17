using System.Windows;
using FAControl.Models;
using FAControl.Printing;
using Serilog;

namespace FAControl.Views;

/// <summary>
/// Vista previa e impresión del pagaré (cliente 2026-07-17: contrato a firmar
/// por cliente y prestamista). Se muestra automáticamente al crear el préstamo
/// y también desde el botón "Imprimir pagaré".
/// </summary>
public partial class PagareWindow : Window
{
    private readonly PagareImpreso _pagare;

    public PagareWindow(PagareImpreso pagare)
    {
        InitializeComponent();
        _pagare = pagare;
        // Un documento nuevo para el visor: el mismo objeto no se puede
        // compartir entre el visor y la impresión (un FlowDocument tiene un
        // solo padre lógico).
        Visor.Document = PagareDocumentFactory.Crear(pagare);
    }

    private void BotonImprimir_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var documento = PagareDocumentFactory.Crear(_pagare);
            ImpresoraRecibos.ImprimirDocumento(documento, $"Pagaré {_pagare.CodigoPrestamo}");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error imprimiendo el pagaré {Codigo}", _pagare.CodigoPrestamo);
            MessageBox.Show(this, $"No se pudo imprimir el pagaré.\n\n{ex.Message}",
                "Imprimir pagaré", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BotonCerrar_Click(object sender, RoutedEventArgs e) => Close();
}
