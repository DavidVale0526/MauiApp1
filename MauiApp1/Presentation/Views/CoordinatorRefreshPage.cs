using MauiApp1.Application.Sync;
using Microsoft.Extensions.DependencyInjection;

namespace MauiApp1.Presentation.Views;

public abstract class CoordinatorRefreshPage : ContentPage
{
    private readonly ILocalDataChangeNotifier _changeNotifier;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private Grid? _mobileRoot;
    private View? _originalContent;

    protected CoordinatorRefreshPage(ILocalDataChangeNotifier changeNotifier) => _changeNotifier = changeNotifier;

    protected abstract Task RefreshAfterCoordinatorChangesAsync();

    protected override void OnAppearing()
    {
        base.OnAppearing();
        EnsureMobileNavigation();
        _changeNotifier.DataChanged += OnLocalDataChanged;
    }

    private void EnsureMobileNavigation()
    {
        if (_mobileRoot is not null || Content is null) return;

        var content = Content;
        _originalContent = content;
        Content = null;
        _mobileRoot = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto)
            },
            BackgroundColor = BackgroundColor
        };
        _mobileRoot.Add(content);

        var navBar = CreateMobileNavigationBar();
        Grid.SetRow(navBar, 1);
        _mobileRoot.Add(navBar);
        _mobileRoot.SizeChanged += (_, _) =>
        {
            var isMobile = _mobileRoot.Width < 900;
            navBar.IsVisible = isMobile;
            _mobileRoot.RowDefinitions[1].Height = isMobile ? GridLength.Auto : new GridLength(0);
        };
        Content = _mobileRoot;
    }

    private Border CreateMobileNavigationBar()
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            Padding = new Thickness(8, 4),
            ColumnSpacing = 4,
            BackgroundColor = Colors.White
        };

        var home = CreateNavigationButton("nav_home", "Inicio", IsHomePage(), async () => await NavigateHomeAsync());
        var sales = CreateNavigationButton("nav_sales", "Ventas", IsSalesPage(), async () => await NavigateSalesAsync());
        var inventory = CreateNavigationButton("nav_inventory", "Inventario", IsInventoryPage(), async () => await NavigateToPageAsync<StockConsultationPage>());
        var reports = CreateNavigationButton("nav_reports", "Reportes", GetType().Name == nameof(ReportsPage), async () => await NavigateToPageAsync<ReportsPage>());
        var more = CreateNavigationButton("nav_more", "Más", IsMorePage(), async () => await NavigateMoreAsync());
        grid.Add(home, 0);
        grid.Add(sales, 1);
        grid.Add(inventory, 2);
        grid.Add(reports, 3);
        grid.Add(more, 4);

        return new Border
        {
            Content = grid,
            BackgroundColor = Colors.White,
            Stroke = Color.FromArgb("#E3EAF2"),
            StrokeThickness = 1,
            Padding = 0,
            HeightRequest = 68
        };
    }

    private static View CreateNavigationButton(string iconName, string title, bool isSelected, Func<Task> onClicked)
    {
        var icon = new Image
        {
            Source = isSelected ? $"{iconName}_active.png" : $"{iconName}.png",
            WidthRequest = 23,
            HeightRequest = 23,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Aspect = Aspect.AspectFit
        };
        var label = new Label
        {
            Text = title,
            FontSize = 11,
            FontAttributes = isSelected ? FontAttributes.Bold : FontAttributes.None,
            TextColor = isSelected ? Color.FromArgb("#008060") : Color.FromArgb("#4C5258"),
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center
        };
        var item = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto)
            },
            RowSpacing = 2,
            Padding = new Thickness(2, 3),
            BackgroundColor = Colors.Transparent,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };
        item.Add(icon, 0);
        Grid.SetRow(label, 1);
        item.Add(label);
        item.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await onClicked()) });
        SemanticProperties.SetDescription(item, title);
        return item;
    }

    private bool IsHomePage() => GetType().Name == "MainPage";

    private bool IsSalesPage() => GetType().Name is "SaleRegistrationPage" or "SaleHistoryPage";

    private bool IsInventoryPage() => GetType().Name is "StockConsultationPage" or "ProductManagementPage" or "CategoryManagementPage";

    private bool IsMorePage() => GetType().Name == nameof(MoreOptionsPage);

    private async Task NavigateMoreAsync()
    {
        if (IsMorePage()) return;
        var services = Handler?.MauiContext?.Services;
        if (services is null) return;
        await CloseModalPagesAsync();
        var page = services.GetRequiredService<MoreOptionsPage>();
        await Shell.Current.Navigation.PushModalAsync(new NavigationPage(page));
    }

    private async Task NavigateHomeAsync()
    {
        if (IsHomePage()) return;
        await CloseModalPagesAsync();
    }

    private async Task NavigateSalesAsync()
    {
        if (GetType().Name == nameof(SaleRegistrationPage)) return;
        var services = Handler?.MauiContext?.Services;
        if (services is null) return;
        await CloseModalPagesAsync();
        var page = services.GetRequiredService<SaleRegistrationPage>();
        await Shell.Current.Navigation.PushModalAsync(new NavigationPage(page));
    }

    private async Task NavigateToPageAsync<TPage>() where TPage : Page
    {
        if (GetType() == typeof(TPage)) return;
        var services = Handler?.MauiContext?.Services;
        if (services is null) return;
        await CloseModalPagesAsync();
        var page = services.GetRequiredService<TPage>();
        await Shell.Current.Navigation.PushModalAsync(new NavigationPage(page));
    }

    protected static async Task CloseModalPagesAsync()
    {
        var navigation = Shell.Current?.Navigation;
        if (navigation is null) return;
        while (navigation.ModalStack.Count > 0)
            await navigation.PopModalAsync(false);
    }

    protected override void OnDisappearing()
    {
        _changeNotifier.DataChanged -= OnLocalDataChanged;
        base.OnDisappearing();
    }

    private async void OnLocalDataChanged()
    {
        if (!IsVisible) return;
        await _refreshGate.WaitAsync();
        try
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                if (IsVisible) await RefreshAfterCoordinatorChangesAsync();
            });
        }
        catch
        {
            // The next screen entry or a manual refresh can reload the local data.
        }
        finally
        {
            _refreshGate.Release();
        }
    }
}
