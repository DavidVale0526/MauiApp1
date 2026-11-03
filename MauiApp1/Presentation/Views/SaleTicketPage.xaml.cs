using MauiApp1.Application.Sales;
using MauiApp1.Presentation.ViewModels;
using MauiApp1.Presentation.Services;

namespace MauiApp1.Presentation.Views;

public partial class SaleTicketPage : ContentPage
{
    private readonly IReceiptDocumentActions _documentActions;
    private string? _pdfPath;
    private bool _documentActionBusy;

    public SaleTicketPage(SaleHistoryEntry sale, IReceiptDocumentActions documentActions)
    {
        InitializeComponent();
        _documentActions = documentActions;
        BindingContext = new SaleTicketViewModel(sale);
    }

    private async void OnBackClicked(object? sender, EventArgs eventArgs) => await Navigation.PopAsync();

    private async void OnSavePdfClicked(object? sender, EventArgs eventArgs)
    {
        await RunDocumentActionAsync(async () =>
        {
            var viewModel = (SaleTicketViewModel)BindingContext;
            var savedPath = await _documentActions.SavePdfAsync(viewModel.PdfFileName, await GetPdfContentsAsync());
            if (savedPath is not null)
                await DisplayAlertAsync("Comprobante guardado", "El PDF se guardó en la ubicación seleccionada.", "Aceptar");
        });
    }

    private async void OnSharePdfClicked(object? sender, EventArgs eventArgs)
    {
        await RunDocumentActionAsync(async () =>
        {
            await _documentActions.SharePdfAsync(await GetPdfAsync());
        });
    }

    private async void OnOpenPdfClicked(object? sender, EventArgs eventArgs)
    {
        await RunDocumentActionAsync(async () =>
        {
            await _documentActions.OpenPdfAsync(await GetPdfAsync());
            await DisplayAlertAsync("Imprimir comprobante", "El comprobante se abrió como PDF. Si el lector lo permite, usa su opción Imprimir.", "Aceptar");
        });
    }

    private async Task<string> GetPdfAsync()
    {
        if (_pdfPath is not null && File.Exists(_pdfPath)) return _pdfPath;
        var viewModel = (SaleTicketViewModel)BindingContext;
        _pdfPath = Path.Combine(FileSystem.CacheDirectory, viewModel.PdfFileName);
        await File.WriteAllBytesAsync(_pdfPath, await GetPdfContentsAsync());
        return _pdfPath;
    }

    private Task<byte[]> GetPdfContentsAsync()
    {
        var viewModel = (SaleTicketViewModel)BindingContext;
        return Task.FromResult(SaleReceiptPdfGenerator.Generate(viewModel.Sale));
    }

    private async Task RunDocumentActionAsync(Func<Task> action)
    {
        if (_documentActionBusy) return;
        _documentActionBusy = true;
        SetDocumentButtonsEnabled(false);
        try
        {
            await action();
        }
        catch (TaskCanceledException)
        {
            // The native save/share sheet was dismissed by the user.
        }
        catch (Exception)
        {
            await DisplayAlertAsync("Comprobante", "No se pudo completar la acción con el PDF. La venta sigue guardada y puedes reintentar desde el historial.", "Aceptar");
        }
        finally
        {
            _documentActionBusy = false;
            SetDocumentButtonsEnabled(true);
        }
    }

    private void SetDocumentButtonsEnabled(bool enabled)
    {
        SavePdfButton.IsEnabled = enabled;
        SharePdfButton.IsEnabled = enabled;
        OpenPdfButton.IsEnabled = enabled;
    }

    private void OnPageSizeChanged(object? sender, EventArgs eventArgs)
    {
        if (sender is Grid layout)
        {
            layout.Padding = Width >= 900 ? new Thickness(32, 28) : new Thickness(16, 20);
            TicketLayout.WidthRequest = Math.Min(720, Math.Max(0, layout.Width - layout.Padding.HorizontalThickness));
        }
    }
}
