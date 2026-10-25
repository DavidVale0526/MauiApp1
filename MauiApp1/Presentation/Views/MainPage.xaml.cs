using MauiApp1.Presentation.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Networking;
using MauiApp1.Application.Sync;

namespace MauiApp1.Presentation.Views;

public partial class MainPage : CoordinatorRefreshPage
{
    private bool _headerIsActive;

    private void UpdateHeaderLayout(bool desktop)
    {
        var compactMobile = OperatingSystem.IsAndroid() && !desktop;
        var target = desktop ? DesktopHeaderHost : MobileHeaderHost;
        if (target.Content != HeaderStatusBar)
        {
            if (HeaderStatusBar.Parent is ContentView previousHost)
                previousHost.Content = null;
            target.Content = HeaderStatusBar;
        }
        DesktopHeaderHost.IsVisible = desktop;
        MobileHeaderHost.IsVisible = !desktop;
        HeaderIdentity.IsVisible = !desktop;
        HeaderIdentityIcon.IsVisible = compactMobile;
        HeaderIdentity.ColumnSpacing = compactMobile ? 6 : 0;
        HeaderIdentityText.FontSize = compactMobile ? 16 : 18;
        Grid.SetColumnSpan(HeaderIdentity, compactMobile ? 1 : 2);
        // Remove the unused mobile row so it contributes no trailing row spacing.
        if (compactMobile && HeaderStatusLayout.RowDefinitions.Count == 3)
            HeaderStatusLayout.RowDefinitions.RemoveAt(2);
        else if (!compactMobile && HeaderStatusLayout.RowDefinitions.Count == 2)
            HeaderStatusLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        HeaderStatusLayout.ColumnSpacing = compactMobile ? 8 : 16;
        HeaderStatusLayout.RowSpacing = desktop ? 0 : compactMobile ? 4 : 6;
        // 44-unit action + small padding gives about 56 units at normal text size.
        // Auto rows remain free to grow for enlarged text and longer status messages.
        HeaderStatusBar.Padding = compactMobile ? new Thickness(12, 8) : new Thickness(16, desktop ? 5 : 12);
        HeaderConnectionInfo.RowSpacing = desktop || compactMobile ? 0 : 4;
        HeaderConnectionInfo.ColumnSpacing = compactMobile ? 8 : 12;
        Grid.SetColumnSpan(HeaderStateAction, desktop || compactMobile ? 1 : 2);
        Grid.SetRow(HeaderSyncSummary, desktop || compactMobile ? 0 : 1);
        Grid.SetColumn(HeaderSyncSummary, desktop || compactMobile ? 1 : 0);
        Grid.SetColumnSpan(HeaderSyncSummary, desktop || compactMobile ? 1 : 2);
        HeaderSyncSummary.HorizontalTextAlignment = compactMobile ? TextAlignment.End : TextAlignment.Start;
        Grid.SetRow(HeaderConnectionInfo, desktop ? 0 : 1);
        Grid.SetColumnSpan(HeaderConnectionInfo, desktop ? 1 : 2);
        Grid.SetRow(HeaderUserInfo, desktop || compactMobile ? 0 : 2);
        Grid.SetColumn(HeaderUserInfo, desktop || compactMobile ? 1 : 0);
        Grid.SetColumnSpan(HeaderUserInfo, desktop || compactMobile ? 1 : 2);
        HeaderUserInfo.Spacing = compactMobile ? 1 : 2;
        foreach (var label in HeaderUserInfo.Children.OfType<Label>())
            label.HorizontalTextAlignment = desktop || compactMobile ? TextAlignment.End : TextAlignment.Start;
    }

    private void OnDashboardHeadingSizeChanged(object? sender, EventArgs args)
    {
        if (DashboardHeading.Width <= 0) return;
        var buttonWidth = Math.Max(140,
            ((Microsoft.Maui.IView)NewSaleButton).Measure(double.PositiveInfinity, double.PositiveInfinity).Width);
        var minimumTextWidth = OperatingSystem.IsAndroid()
            ? Math.Max(150, ((Microsoft.Maui.IView)DashboardTitle).Measure(double.PositiveInfinity, double.PositiveInfinity).Width)
            : 220;
        var stackAction = DashboardHeading.Width - buttonWidth - DashboardHeading.ColumnSpacing < minimumTextWidth;
        Grid.SetColumnSpan(DashboardHeadingText, stackAction ? 2 : 1);
        Grid.SetRow(NewSaleButton, stackAction ? 1 : 0);
        Grid.SetColumn(NewSaleButton, stackAction ? 0 : 1);
        Grid.SetColumnSpan(NewSaleButton, stackAction ? 2 : 1);
        NewSaleButton.HorizontalOptions = stackAction ? LayoutOptions.Start : LayoutOptions.End;
    }

    private void OnRecentOperationFooterSizeChanged(object? sender, EventArgs args)
    {
        if (!OperatingSystem.IsAndroid() || sender is not Grid footer || footer.Width <= 0 ||
            footer.Children.OfType<Button>().FirstOrDefault() is not { } historyButton) return;
        var buttonWidth = ((Microsoft.Maui.IView)historyButton).Measure(double.PositiveInfinity, double.PositiveInfinity).Width;
        var stacked = footer.Width - buttonWidth - footer.ColumnSpacing < 110;
        Grid.SetRow(historyButton, stacked ? 1 : 0);
        Grid.SetColumn(historyButton, stacked ? 0 : 1);
        Grid.SetColumnSpan(historyButton, stacked ? 2 : 1);
        footer.RowSpacing = stacked ? 4 : 0;
    }

    private void OnSidebarScrollHandlerChanged(object? sender, EventArgs args)
    {
#if WINDOWS
        // Use the native dark scrollbar and retain Windows accessibility behavior.
        if (sender is ScrollView { Handler.PlatformView: Microsoft.UI.Xaml.Controls.ScrollViewer nativeScroll })
            nativeScroll.RequestedTheme = Microsoft.UI.Xaml.ElementTheme.Dark;
#endif
    }

    private void OnSidebarButtonHandlerChanged(object? sender, EventArgs args)
    {
#if WINDOWS
        if (sender is Button { Handler.PlatformView: Microsoft.UI.Xaml.Controls.Button nativeButton })
            nativeButton.HorizontalContentAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Left;
#elif ANDROID
        if (sender is Button { Handler.PlatformView: Android.Widget.Button nativeButton })
            nativeButton.Gravity = Android.Views.GravityFlags.Start | Android.Views.GravityFlags.CenterVertical;
#endif
    }

    private void OnToggleSidebarGroupClicked(object? sender, EventArgs args)
    {
        if (sender is not Button button) return;
        var options = (button.CommandParameter as string) switch
        {
            "Sales" => SalesNavigationOptions,
            "Inventory" => InventoryNavigationOptions,
            "Purchases" => PurchasesNavigationOptions,
            "Customers" => CustomersNavigationOptions,
            "Reports" => ReportsNavigationOptions,
            "Administration" => AdministrationNavigationOptions,
            _ => null
        };
        if (options is null) return;

        options.IsVisible = !options.IsVisible;
        button.Text = button.Text.TrimEnd(' ', '▾', '▸') + (options.IsVisible ? "  ▾" : "  ▸");
        SemanticProperties.SetHint(button, options.IsVisible
            ? "Grupo expandido. Activar para contraer."
            : "Grupo contraído. Activar para expandir.");
    }

    private void OnRootLayoutSizeChanged(object? sender, EventArgs args)
    {
        var windowWidth = RootLayout.Width;
        if (windowWidth <= 0)
        {
            return;
        }

        var showSidebar = windowWidth >= DesktopNavigationMinWidth;
        var availableDashboardWidth = Math.Max(0, windowWidth - (showSidebar ? 250 : 0));
        var dashboardWidth = Math.Min(availableDashboardWidth, 1440);
        DashboardContent.WidthRequest = dashboardWidth;
        RootLayout.ColumnDefinitions[0].Width = new GridLength(showSidebar ? 250 : 0);
        Sidebar.IsVisible = showSidebar;
        UpdateHeaderLayout(showSidebar);
        // Windows keeps a scrollable table even in a narrow window; Android uses cards.
        RecentOperationsTable.IsVisible = OperatingSystem.IsWindows();
        RecentOperationsCards.IsVisible = !OperatingSystem.IsWindows();
        SalesAndAlertsGrid.ColumnDefinitions[0].Width = new GridLength(showSidebar ? 2 : 1, GridUnitType.Star);
        SalesAndAlertsGrid.ColumnDefinitions[1].Width = showSidebar ? GridLength.Star : new GridLength(0);
        SalesAndAlertsGrid.ColumnSpacing = showSidebar ? 22 : 0;
        SalesAndAlertsGrid.RowSpacing = showSidebar ? 0 : OperatingSystem.IsAndroid() ? 16 : 22;
        Grid.SetColumn(OperationalAlertsCard, showSidebar ? 1 : 0);
        Grid.SetRow(OperationalAlertsCard, showSidebar ? 0 : 1);
        // Los indicadores forman una fila en escritorio y una cuadrícula 2×2 en móvil.
        KpiCardsGrid.ColumnDefinitions.Clear();
        for (var index = 0; index < (showSidebar ? 4 : 2); index++)
            KpiCardsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        var kpiCards = new Border[] { SalesKpiCard, LowStockKpiCard, ExpiryKpiCard, CashKpiCard };
        for (var index = 0; index < kpiCards.Length; index++)
        {
            Grid.SetColumn(kpiCards[index], showSidebar ? index : index % 2);
            Grid.SetRow(kpiCards[index], showSidebar ? 0 : index / 2);
        }
        DashboardContent.Padding = showSidebar
            ? new Thickness(36, 12, 36, 30)
            : OperatingSystem.IsAndroid() ? new Thickness(16, 16, 16, 24) : new Thickness(16, 24);

        var cardContentWidth = dashboardWidth - DashboardContent.Padding.Left - DashboardContent.Padding.Right;
        RecentOperationsTableContent.WidthRequest = Math.Max(850, cardContentWidth - 36);
    }

    private async void OnSynchronizationClicked(object? sender, EventArgs args)
    {
        if (Handler?.MauiContext?.Services.GetRequiredService<SyncManagementPage>() is not { } page) return;
        await Navigation.PushModalAsync(new NavigationPage(page));
    }

    private readonly MainPageViewModel _viewModel;
    private readonly ICoordinatorRealtimeClient _realtimeClient;
    private readonly IConnectivity _connectivity;
    private readonly ILocalOperationSyncService _operationSyncService;

    public MainPage(MainPageViewModel viewModel, ICoordinatorRealtimeClient realtimeClient,
        IConnectivity connectivity, ILocalOperationSyncService operationSyncService,
        ILocalDataChangeNotifier changeNotifier) : base(changeNotifier)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _realtimeClient = realtimeClient;
        _connectivity = connectivity;
        _operationSyncService = operationSyncService;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _headerIsActive = true;
        _connectivity.ConnectivityChanged += OnConnectivityChanged;
        _realtimeClient.StateChanged += OnHeaderRealtimeStateChanged;
        var headerRefresh = RefreshHeaderOnMainThreadAsync();
        await _viewModel.LoadLowStockAlertsAsync();
        await _viewModel.LoadWeeklySalesAsync();
        await TrySynchronizeInBackgroundAsync();
        await _viewModel.LoadRecentOperationsAsync();
        await _realtimeClient.StartAsync();
        await headerRefresh;
    }

    protected override void OnDisappearing()
    {
        _headerIsActive = false;
        _connectivity.ConnectivityChanged -= OnConnectivityChanged;
        _realtimeClient.StateChanged -= OnHeaderRealtimeStateChanged;
        base.OnDisappearing();
    }

    protected override async Task RefreshAfterCoordinatorChangesAsync()
    {
        await _viewModel.LoadLowStockAlertsAsync();
        await _viewModel.LoadWeeklySalesAsync();
        await _viewModel.LoadRecentOperationsAsync();
        await RefreshHeaderOnMainThreadAsync();
    }

    private Task RefreshHeaderOnMainThreadAsync() => !_headerIsActive
        ? Task.CompletedTask
        : MainThread.InvokeOnMainThreadAsync(() => _headerIsActive
            ? _viewModel.RefreshHeaderAsync() : Task.CompletedTask);

    private async void OnHeaderRealtimeStateChanged(CoordinatorRealtimeState state) =>
        await RefreshHeaderOnMainThreadAsync();

    private async void OnRefreshRecentOperationsClicked(object? sender, EventArgs args) =>
        await _viewModel.LoadRecentOperationsAsync();

    private void OnRecentOperationHistoryClicked(object? sender, EventArgs args)
    {
        if (sender is not Button { BindingContext: RecentOperationItem item }) return;
        switch (item.History)
        {
            case RecentOperationHistory.Sales: OnSaleHistoryClicked(sender, args); break;
            case RecentOperationHistory.Purchases: OnPurchaseHistoryClicked(sender, args); break;
            case RecentOperationHistory.Inventory: OnInventoryMovementHistoryClicked(sender, args); break;
            case RecentOperationHistory.Payments: OnPaymentsClicked(sender, args); break;
        }
    }

    private async void OnRefreshWeeklySalesClicked(object? sender, EventArgs args) =>
        await _viewModel.LoadWeeklySalesAsync();

    private async void OnRefreshOperationalAlertsClicked(object? sender, EventArgs args) =>
        await _viewModel.LoadLowStockAlertsAsync();

    private async void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs args) =>
        await TrySynchronizeInBackgroundAsync();

    private async Task TrySynchronizeInBackgroundAsync()
    {
        try
        {
            await _operationSyncService.SynchronizeAsync();
        }
        catch
        {
            // Operations remain in the durable Outbox and can be retried later.
        }
        finally
        {
            await RefreshHeaderOnMainThreadAsync();
        }
    }

    private async void OnCreateCashierAccountClicked(object? sender, EventArgs eventArgs)
    {
        if (BindingContext is not MainPageViewModel { IsAdministrator: true } ||
            Handler?.MauiContext?.Services.GetRequiredService<CashierSetupPage>() is not { } page)
        {
            return;
        }

        await Navigation.PushModalAsync(new NavigationPage(page));
    }

    private async void OnManageCategoriesClicked(object? sender, EventArgs eventArgs)
    {
        if (Handler?.MauiContext?.Services.GetRequiredService<CategoryManagementPage>() is not { } page)
        {
            return;
        }

        await Navigation.PushModalAsync(new NavigationPage(page));
    }

    private async void OnManageProductsClicked(object? sender, EventArgs eventArgs)
    {
        if (Handler?.MauiContext?.Services.GetRequiredService<ProductManagementPage>() is not { } page)
        {
            return;
        }

        await Navigation.PushModalAsync(new NavigationPage(page));
    }

    private async void OnConsultStockClicked(object? sender, EventArgs eventArgs)
    {
        if (Handler?.MauiContext?.Services.GetRequiredService<StockConsultationPage>() is not { } page)
        {
            return;
        }

        await Navigation.PushModalAsync(new NavigationPage(page));
    }

    private async void OnRegisterPurchaseClicked(object? sender, EventArgs eventArgs)
    {
        if (Handler?.MauiContext?.Services.GetRequiredService<PurchaseRegistrationPage>() is not { } page)
        {
            return;
        }

        await Navigation.PushModalAsync(new NavigationPage(page));
    }

    private async void OnPurchaseHistoryClicked(object? sender, EventArgs eventArgs)
    {
        if (Handler?.MauiContext?.Services.GetRequiredService<PurchaseHistoryPage>() is not { } page)
        {
            return;
        }

        await Navigation.PushModalAsync(new NavigationPage(page));
    }

    private async void OnInventoryMovementHistoryClicked(object? sender, EventArgs eventArgs)
    {
        if (Handler?.MauiContext?.Services.GetRequiredService<InventoryMovementHistoryPage>() is not { } page)
        {
            return;
        }

        await Navigation.PushModalAsync(new NavigationPage(page));
    }

    private async void OnRegisterSaleClicked(object? sender, EventArgs eventArgs)
    {
        if (Handler?.MauiContext?.Services.GetRequiredService<SaleRegistrationPage>() is not { } page)
        {
            return;
        }

        await Navigation.PushModalAsync(new NavigationPage(page));
    }

    private async void OnSaleHistoryClicked(object? sender, EventArgs eventArgs)
    {
        if (Handler?.MauiContext?.Services.GetRequiredService<SaleHistoryPage>() is not { } page)
        {
            return;
        }

        await Navigation.PushModalAsync(new NavigationPage(page));
    }

    private async void OnCustomersClicked(object? sender, EventArgs eventArgs)
    {
        if (Handler?.MauiContext?.Services.GetRequiredService<CustomerManagementPage>() is not { } page) return;
        await Navigation.PushModalAsync(new NavigationPage(page));
    }

    private async void OnPaymentsClicked(object? sender, EventArgs eventArgs)
    {
        if (Handler?.MauiContext?.Services.GetRequiredService<PaymentManagementPage>() is not { } page) return;
        await Navigation.PushModalAsync(new NavigationPage(page));
    }

    private async void OnReportsClicked(object? sender, EventArgs eventArgs)
    {
        if (Handler?.MauiContext?.Services.GetRequiredService<ReportsPage>() is not { } page) return;
        await Navigation.PushModalAsync(new NavigationPage(page));
    }

    private async void OnRegisterOtherExitClicked(object? sender, EventArgs eventArgs)
    {
        if (Handler?.MauiContext?.Services.GetRequiredService<OtherExitPage>() is not { } page)
        {
            return;
        }

        await Navigation.PushModalAsync(new NavigationPage(page));
    }
}
