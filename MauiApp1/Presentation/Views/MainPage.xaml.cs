using MauiApp1.Presentation.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Networking;
using MauiApp1.Application.Sync;

namespace MauiApp1.Presentation.Views;

public partial class MainPage : CoordinatorRefreshPage
{
    private void OnRootLayoutSizeChanged(object? sender, EventArgs args)
    {
        var windowWidth = RootLayout.Width;
        if (windowWidth <= 0)
        {
            return;
        }

        var showSidebar = windowWidth >= 950;
        var availableDashboardWidth = Math.Max(0, windowWidth - (showSidebar ? 250 : 0));
        var dashboardWidth = Math.Min(availableDashboardWidth, 1440);
        DashboardContent.WidthRequest = dashboardWidth;
        RootLayout.ColumnDefinitions[0].Width = new GridLength(showSidebar ? 250 : 0);
        Sidebar.IsVisible = showSidebar;
        BottomActionsGrid.IsVisible = false;
        DashboardContent.Padding = showSidebar
            ? new Thickness(36, 30)
            : new Thickness(16, 24);

        var cardContentWidth = dashboardWidth - DashboardContent.Padding.Left - DashboardContent.Padding.Right;
        var useTwoColumnCards = cardContentWidth >= 820;
        MenuCardsGrid.ColumnDefinitions.Clear();
        MenuCardsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        if (useTwoColumnCards)
        {
            MenuCardsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        }

        var cards = new Border[] { SalesCard, InventoryCard, PurchasesCard, OtherCard };
        for (var index = 0; index < cards.Length; index++)
        {
            Grid.SetColumn(cards[index], useTwoColumnCards ? index % 2 : 0);
            Grid.SetRow(cards[index], useTwoColumnCards ? index / 2 : index);
        }
    }

    private async void OnSynchronizationClicked(object? sender, EventArgs args)
    {
        if (Handler?.MauiContext?.Services.GetRequiredService<SyncManagementPage>() is not { } page) return;
        await Navigation.PushModalAsync(new NavigationPage(page));
    }

    private async void OnAccountRecoveryClicked(object? sender, EventArgs args)
    {
        if (Handler?.MauiContext?.Services.GetRequiredService<AccountRecoveryPage>() is not { } page) return;
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
        _connectivity.ConnectivityChanged += OnConnectivityChanged;
        await _viewModel.LoadLowStockAlertsAsync();
        await TrySynchronizeInBackgroundAsync();
        await _realtimeClient.StartAsync();
    }

    protected override void OnDisappearing()
    {
        _connectivity.ConnectivityChanged -= OnConnectivityChanged;
        base.OnDisappearing();
    }

    protected override Task RefreshAfterCoordinatorChangesAsync() => _viewModel.LoadLowStockAlertsAsync();

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
