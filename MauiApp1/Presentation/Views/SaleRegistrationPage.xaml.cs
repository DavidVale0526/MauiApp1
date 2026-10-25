using MauiApp1.Presentation.ViewModels;
using MauiApp1.Application.Sync;
using MauiApp1.Application.Sales;

namespace MauiApp1.Presentation.Views;

public partial class SaleRegistrationPage : CoordinatorRefreshPage
{
    private bool? _quantityActionsStacked;
    private ScrollView? _androidCartViewport;
    private bool _updatingAndroidCartViewport;

    private void OnSaleLayoutSizeChanged(object? sender, EventArgs args)
    {
        if (SaleViewport.Width <= 0 || SaleViewport.Height <= 0) return;
        var useDesktopLayout = SaleViewport.Width >= DesktopNavigationMinWidth;
        PageLayout.Padding = useDesktopLayout ? new Thickness(28, 24) : new Thickness(16, 16);
        SaleLayout.WidthRequest = Math.Min(1440, Math.Max(0, SaleViewport.Width - PageLayout.Padding.HorizontalThickness));
        SaleBadge.IsVisible = useDesktopLayout;
        if (useDesktopLayout)
        {
            if (ContentPanels.ColumnDefinitions.Count == 1)
                ContentPanels.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(360)));
            if (!ContentPanels.Children.Contains(CartPanel))
            {
                CartSheetHost.Content = null;
                ContentPanels.Add(CartPanel, 1);
            }
            CartSheetBackdrop.IsVisible = false;
        }
        else
        {
            if (ContentPanels.ColumnDefinitions.Count > 1)
                ContentPanels.ColumnDefinitions.RemoveAt(1);
            if (ContentPanels.Children.Contains(CartPanel))
            {
                ContentPanels.Remove(CartPanel);
                CartSheetHost.Content = CartPanel;
            }
        }

        MobileSummary.IsVisible = !useDesktopLayout;
        CloseCartButton.IsVisible = !useDesktopLayout;
        CartSheetHost.HeightRequest = Math.Max(0, Math.Min(800, SaleViewport.Height * 0.92));
        UpdateAndroidCartViewport();
        UpdateSaleContentHeight();
    }

    private void OnCartSectionSizeChanged(object? sender, EventArgs args) => UpdateAndroidCartViewport();

    private void UpdateAndroidCartViewport()
    {
        if (DeviceInfo.Platform != DevicePlatform.Android || _updatingAndroidCartViewport ||
            SaleViewport.Width <= 0 || CartSheetHost.HeightRequest <= 0) return;

        _updatingAndroidCartViewport = true;
        try
        {
            var contentWidth = SaleViewport.Width - CartPanel.Padding.HorizontalThickness;
            if (contentWidth <= 0) return;
            static double HeightOf(View view, double width) =>
                ((Microsoft.Maui.IView)view).Measure(width, double.PositiveInfinity).Height;

            var listMinimum = CartCollection.ItemsSource is System.Collections.ICollection { Count: 0 } &&
                CartCollection.EmptyView is View emptyView
                ? Math.Max(160, HeightOf(emptyView, contentWidth))
                : 160;
            var minimum = CartPanel.Padding.VerticalThickness + CartLayout.RowSpacing * 3
                + HeightOf(CartHeader, contentWidth) + HeightOf(CartCreditOptions, contentWidth)
                + HeightOf(CartTotals, contentWidth) + listMinimum;
            var needsScrolling = MobileSummary.IsVisible && minimum > CartSheetHost.HeightRequest;

            if (needsScrolling)
            {
                // Auto rows keep the header, credit controls and totals in sequence;
                // the list stays bounded so the whole sheet can scroll in a short window.
                CartLayout.RowDefinitions[2].Height = GridLength.Auto;
                CartCollection.HeightRequest = Math.Max(listMinimum, Math.Min(240, CartSheetHost.HeightRequest * 0.5));
                if (CartPanel.Content == CartLayout)
                {
                    CartPanel.Content = null;
                    _androidCartViewport ??= new ScrollView
                    {
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Never
                    };
                    _androidCartViewport.Content = CartLayout;
                    CartPanel.Content = _androidCartViewport;
                }
            }
            else
            {
                if (_androidCartViewport?.Content == CartLayout)
                {
                    _androidCartViewport.Content = null;
                    CartPanel.Content = CartLayout;
                }
                CartLayout.RowDefinitions[2].Height = GridLength.Star;
                CartCollection.HeightRequest = -1;
            }
        }
        finally { _updatingAndroidCartViewport = false; }
    }

    private void OnQuantityActionsSizeChanged(object? sender, EventArgs args)
    {
        if (QuantityActions.Width <= 0) return;
        // Measure scaled controls so Android's accessibility font size is respected.
        var entryWidth = ((Microsoft.Maui.IView)QuantityEntry).Measure(double.PositiveInfinity, double.PositiveInfinity).Width;
        var buttonWidth = ((Microsoft.Maui.IView)AddQuantityButton).Measure(double.PositiveInfinity, double.PositiveInfinity).Width;
        var stack = QuantityActions.Width < Math.Max(180, entryWidth) + buttonWidth + QuantityActions.ColumnSpacing;
        if (_quantityActionsStacked != stack)
        {
            _quantityActionsStacked = stack;
            QuantityActions.ColumnDefinitions.Clear();
            QuantityActions.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            if (!stack) QuantityActions.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            Grid.SetColumn(AddQuantityButton, stack ? 0 : 1);
            Grid.SetRow(AddQuantityButton, stack ? 1 : 0);
            Grid.SetRow(QuantityHelp, stack ? 2 : 1);
            Grid.SetColumnSpan(QuantityHelp, stack ? 1 : 2);
            AddQuantityButton.HorizontalOptions = stack ? LayoutOptions.Fill : LayoutOptions.End;
        }
        UpdateSaleContentHeight();
    }

    private void UpdateSaleContentHeight()
    {
        if (SaleViewport.Width <= 0 || SaleViewport.Height <= 0) return;
        var width = SaleLayout.WidthRequest;
        var catalogWidth = width - (MobileSummary.IsVisible ? 0 : 360 + ContentPanels.ColumnSpacing);
        if (catalogWidth <= 0) return;
        static double HeightOf(View view, double availableWidth) =>
            ((Microsoft.Maui.IView)view).Measure(availableWidth, double.PositiveInfinity).Height;

        // Keep both collections bounded. Only the page scrolls additionally when the
        // keyboard, enlarged text or a short window leaves too little vertical space.
        var catalogMinimum = HeightOf(CatalogHeader, catalogWidth) + HeightOf(QuantityPanel, catalogWidth) + 180 + 24;
        var minimum = PageLayout.Padding.VerticalThickness + HeightOf(SaleHeader, width)
            + catalogMinimum + HeightOf(SaleMessages, width) + SaleLayout.RowSpacing * 3;
        if (MobileSummary.IsVisible) minimum += HeightOf(MobileSummary, width);
        var height = Math.Max(SaleViewport.Height, minimum);
        if (Math.Abs(PageLayout.HeightRequest - height) > 0.5) PageLayout.HeightRequest = height;
    }

    private void OnShowCartClicked(object? sender, EventArgs args)
    {
        QuantityEntry.Unfocus();
        CartSheetBackdrop.IsVisible = true;
        UpdateAndroidCartViewport();
    }

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
        HideSoftInputOnTapped = true;
        if (DeviceInfo.Platform == DevicePlatform.Android)
        {
            // Reserve the IME area around the whole page (including its shared footer),
            // rather than letting Android pan a full-height ScrollView behind the keyboard.
            SafeAreaEdges = Microsoft.Maui.SafeAreaEdges.All;
            CloseCartButton.WidthRequest = 48;
            CloseCartButton.HeightRequest = -1;
            CloseCartButton.MinimumHeightRequest = 48;
        }
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
