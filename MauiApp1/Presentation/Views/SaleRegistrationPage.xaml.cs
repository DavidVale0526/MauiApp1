using MauiApp1.Presentation.ViewModels;
using MauiApp1.Application.Sync;
using MauiApp1.Application.Sales;

namespace MauiApp1.Presentation.Views;

public partial class SaleRegistrationPage : CoordinatorRefreshPage
{
    private void OnSaleLayoutSizeChanged(object? sender, EventArgs args)
    {
        if (PageLayout.Width <= 0) return;
        var useDesktopLayout = PageLayout.Width >= 900;
        PageLayout.Padding = useDesktopLayout ? new Thickness(28, 24) : new Thickness(16, 16);
        SaleLayout.WidthRequest = Math.Min(1440, Math.Max(0, PageLayout.Width - PageLayout.Padding.HorizontalThickness));
        ContentPanels.ColumnDefinitions.Clear();
        ContentPanels.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        if (useDesktopLayout)
        {
            ContentPanels.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(360)));
            if (!ContentPanels.Children.Contains(CartPanel))
            {
                CartSheetHost.Content = null;
                ContentPanels.Add(CartPanel, 1);
            }
            CartSheetBackdrop.IsVisible = false;
        }
        else if (ContentPanels.Children.Contains(CartPanel))
        {
            ContentPanels.Remove(CartPanel);
            CartSheetHost.Content = CartPanel;
        }

        MobileSummary.IsVisible = !useDesktopLayout;
        CloseCartButton.IsVisible = !useDesktopLayout;
        CartSheetHost.HeightRequest = Math.Max(0, Math.Min(800, PageLayout.Height * 0.92));
    }

    private void OnShowCartClicked(object? sender, EventArgs args) => CartSheetBackdrop.IsVisible = true;

    private void OnCloseCartClicked(object? sender, EventArgs args) => CartSheetBackdrop.IsVisible = false;

    protected override bool OnBackButtonPressed()
    {
        if (!CartSheetBackdrop.IsVisible) return base.OnBackButtonPressed();
        CartSheetBackdrop.IsVisible = false;
        return true;
    }

    private readonly SaleRegistrationViewModel _viewModel;
    private readonly ISaleService _saleService;
    private bool _reviewInProgress;

    public SaleRegistrationPage(SaleRegistrationViewModel viewModel, ISaleService saleService,
        ILocalDataChangeNotifier changeNotifier) : base(changeNotifier)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _saleService = saleService;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }

    protected override Task RefreshAfterCoordinatorChangesAsync() => _viewModel.RefreshAfterCoordinatorChangesAsync();

    private async void OnBackClicked(object? sender, EventArgs eventArgs)
    {
        if (CartSheetBackdrop.IsVisible) CartSheetBackdrop.IsVisible = false;
        else await Navigation.PopModalAsync();
    }

    private void OnRemoveLineClicked(object? sender, EventArgs eventArgs)
    {
        if ((sender as Button)?.BindingContext is SaleCartLine line)
        {
            _viewModel.RemoveLine(line);
        }
    }

    private async void OnReviewClicked(object? sender, EventArgs eventArgs)
    {
        if (_reviewInProgress) return;
        var sale = _viewModel.PrepareSale();
        if (sale is null)
        {
            return;
        }

        _reviewInProgress = true;
        try
        {
            var retry = _viewModel.ConfirmButtonText.StartsWith("Reintentar", StringComparison.Ordinal);
            var review = new SaleReviewPage(_viewModel, sale, retry);
            await Navigation.PushModalAsync(review);
            if (!await review.Decision)
            {
                _viewModel.CancelReview();
                return;
            }

            var saved = await _viewModel.RecordPendingAsync();
            CartSheetBackdrop.IsVisible = false;
            if (saved)
            {
                await ShowSavedTicketAsync();
            }
        }
        catch
        {
            _viewModel.CancelReview();
            await DisplayAlertAsync("Revisión de venta", "No se pudo completar la revisión. Vuelve a intentarlo.", "Aceptar");
        }
        finally { _reviewInProgress = false; }
    }

    private async Task ShowSavedTicketAsync()
    {
        if (_viewModel.LastCompletedSaleOperationId is not { } operationId)
        {
            return;
        }

        try
        {
            var sale = (await _saleService.GetHistoryAsync())
                .SingleOrDefault(item => item.OperationId == operationId);
            if (sale is not null)
            {
                await Navigation.PushAsync(new SaleTicketPage(sale));
                return;
            }
        }
        catch
        {
            // The sale is already saved; the history page remains available to reopen its ticket.
        }

        await DisplayAlertAsync(
            "Venta guardada",
            "La venta se guardó, pero no se pudo abrir el ticket ahora. Puedes consultarlo desde el historial de ventas.",
            "Aceptar");
    }
}
