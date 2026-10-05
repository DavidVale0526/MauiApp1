using MauiApp1.Application.Sync;
using MauiApp1.Application.Authentication;
using Microsoft.Extensions.DependencyInjection;
using MauiApp1.Domain.Users;

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
                new ColumnDefinition(GridLength.Star)
            },
            Padding = new Thickness(8, 4),
            ColumnSpacing = 4,
            BackgroundColor = Colors.White
        };

        var home = CreateNavigationButton("⌂", "Inicio", IsHomePage(), async () => await NavigateHomeAsync());
        var sales = CreateNavigationButton("▣", "Ventas", IsSalesPage(), async () => await NavigateSalesAsync());
        var more = CreateNavigationButton("•••", "Más", false, async () => await ShowMoreOptionsAsync());
        grid.Add(home, 0);
        grid.Add(sales, 1);
        grid.Add(more, 2);

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

    private static Button CreateNavigationButton(string icon, string title, bool isSelected, Func<Task> onClicked)
    {
        var button = new Button
        {
            Text = $"{icon}\n{title}",
            FontSize = 12,
            FontAttributes = isSelected ? FontAttributes.Bold : FontAttributes.None,
            TextColor = isSelected ? Color.FromArgb("#0D1B2A") : Color.FromArgb("#61738A"),
            BackgroundColor = Colors.Transparent,
            Padding = new Thickness(6, 2),
            CornerRadius = 10,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };
        button.Clicked += async (_, _) => await onClicked();
        return button;
    }

    private bool IsHomePage() => GetType().Name == "MainPage";

    private bool IsSalesPage() => GetType().Name is "SaleRegistrationPage" or "SaleHistoryPage";

    private async Task NavigateHomeAsync()
    {
        if (IsHomePage()) return;
        await CloseModalPagesAsync();
    }

    private async Task NavigateSalesAsync()
    {
        if (GetType().Name == "SaleRegistrationPage") return;
        var services = Handler?.MauiContext?.Services;
        if (services is null) return;
        await CloseModalPagesAsync();
        var page = services.GetRequiredService<SaleRegistrationPage>();
        await Shell.Current.Navigation.PushModalAsync(new NavigationPage(page));
    }

    private async Task ShowMoreOptionsAsync()
    {
        var options = new List<string>
        {
            "Productos", "Categorías", "Existencias", "Registrar compra", "Historial de compras",
            "Movimientos de inventario", "Registrar salida", "Clientes y crédito", "Crédito y pagos", "Reportes",
            "Sincronización y conexión"
        };
        var services = Handler?.MauiContext?.Services;
        if (services?.GetService<IAuthenticationService>()?.CurrentUser?.Role == UserRole.Administrator)
            options.Add("Cuentas de cajero");
        options.Add("Cerrar sesión");

        var selected = await DisplayActionSheetAsync("Más opciones", "Cancelar", null, options.ToArray());
        if (string.IsNullOrWhiteSpace(selected) || selected == "Cancelar") return;
        if (selected == "Cerrar sesión")
        {
            var signInService = services?.GetService<ICoordinatedSignInService>();
            if (signInService is not null) await signInService.SignOutAsync();
            return;
        }

        var pageType = selected switch
        {
            "Productos" => typeof(ProductManagementPage),
            "Categorías" => typeof(CategoryManagementPage),
            "Existencias" => typeof(StockConsultationPage),
            "Registrar compra" => typeof(PurchaseRegistrationPage),
            "Historial de compras" => typeof(PurchaseHistoryPage),
            "Movimientos de inventario" => typeof(InventoryMovementHistoryPage),
            "Registrar salida" => typeof(OtherExitPage),
            "Clientes y crédito" => typeof(CustomerManagementPage),
            "Crédito y pagos" => typeof(PaymentManagementPage),
            "Reportes" => typeof(ReportsPage),
            "Sincronización y conexión" => typeof(SyncManagementPage),
            "Cuentas de cajero" => typeof(CashierSetupPage),
            _ => null
        };
        if (pageType is null || services?.GetService(pageType) is not Page targetPage) return;

        await CloseModalPagesAsync();
        await Shell.Current.Navigation.PushModalAsync(new NavigationPage(targetPage));
    }

    private static async Task CloseModalPagesAsync()
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
        if (!IsVisible || !await _refreshGate.WaitAsync(0)) return;
        try
        {
            await MainThread.InvokeOnMainThreadAsync(RefreshAfterCoordinatorChangesAsync);
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
