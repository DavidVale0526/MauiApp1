using MauiApp1.Application.Authentication;
using MauiApp1.Application.Sync;
using MauiApp1.Domain.Users;

namespace MauiApp1.Presentation.Views;

public partial class MoreOptionsPage : CoordinatorRefreshPage
{
    private readonly IServiceProvider _services;
    private readonly IAuthenticationService _authenticationService;
    private readonly ICoordinatedSignInService _signInService;

    public MoreOptionsPage(
        IServiceProvider services,
        IAuthenticationService authenticationService,
        ICoordinatedSignInService signInService,
        ILocalDataChangeNotifier changeNotifier) : base(changeNotifier)
    {
        InitializeComponent();
        _services = services;
        _authenticationService = authenticationService;
        _signInService = signInService;

        AddOptions(AccountOptions,
        [
            new("♙", "Seguridad de la cuenta", "Configurar o renovar el código de recuperación", typeof(AccountRecoveryPage))
        ]);

        AddOptions(OperationsOptions,
        [
            new("▣", "Registrar venta", "Iniciar una nueva venta", typeof(SaleRegistrationPage)),
            new("▤", "Historial de ventas", "Consultar las ventas registradas", typeof(SaleHistoryPage))
        ]);

        AddOptions(InventoryOptions,
        [
            new("▦", "Productos", "Administrar el catálogo de productos", typeof(ProductManagementPage)),
            new("▤", "Categorías", "Administrar las categorías", typeof(CategoryManagementPage)),
            new("▣", "Existencias", "Consultar el inventario disponible", typeof(StockConsultationPage)),
            new("＋", "Registrar compra", "Agregar productos recibidos", typeof(PurchaseRegistrationPage)),
            new("↻", "Historial de compras", "Consultar compras registradas", typeof(PurchaseHistoryPage)),
            new("⇅", "Movimientos de inventario", "Consultar entradas y salidas", typeof(InventoryMovementHistoryPage)),
            new("−", "Registrar salida o merma", "Registrar una salida de producto o merma mixta", typeof(OtherExitPage))
        ]);

        AddOptions(CustomerOptions,
        [
            new("♙", "Clientes y crédito", "Consultar clientes y saldos", typeof(CustomerManagementPage)),
            new("C$", "Crédito y pagos", "Registrar abonos y consultar pagos", typeof(PaymentManagementPage)),
            new("▥", "Reportes", "Consultar los reportes disponibles", typeof(ReportsPage))
        ]);

        var administration = new List<NavigationOption>
        {
            new("⇄", "Sincronización y conexión", "Conexión con el coordinador", typeof(SyncManagementPage))
        };
        if (_authenticationService.CurrentUser?.Role == UserRole.Administrator)
            administration.Add(new("♙", "Cuentas de cajero", "Administrar cuentas de cajero", typeof(CashierSetupPage)));
        administration.Add(new("⇥", "Cerrar sesión", "Salir de la sesión actual", IsSignOut: true));
        AddOptions(AdministrationOptions, administration);
    }

    protected override Task RefreshAfterCoordinatorChangesAsync() => Task.CompletedTask;

    private void AddOptions(VerticalStackLayout container, IReadOnlyList<NavigationOption> options)
    {
        for (var index = 0; index < options.Count; index++)
        {
            var option = options[index];
            container.Add(CreateOptionRow(option));
            if (index < options.Count - 1)
                container.Add(new BoxView { HeightRequest = 1, Color = Color.FromArgb("#E9EDF1"), Margin = new Thickness(0) });
        }
    }

    private View CreateOptionRow(NavigationOption option)
    {
        var isSignOut = option.IsSignOut;
        var titleColor = isSignOut ? Color.FromArgb("#C62828") : Color.FromArgb("#191C1E");
        var content = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 16,
            Padding = new Thickness(16, 14),
            MinimumHeightRequest = 96,
            BackgroundColor = Colors.Transparent
        };

        var iconTile = new Border
        {
            WidthRequest = 58,
            HeightRequest = 58,
            BackgroundColor = Color.FromArgb("#F1F3F5"),
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 },
            VerticalOptions = LayoutOptions.Center,
            Content = new Label
            {
                Text = option.Icon,
                FontSize = option.Icon == "C$" ? 18 : 27,
                FontAttributes = FontAttributes.Bold,
                TextColor = titleColor,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center
            }
        };
        var descriptions = new VerticalStackLayout
        {
            Spacing = 3,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label { Text = option.Title, FontSize = 18, TextColor = titleColor, LineBreakMode = LineBreakMode.TailTruncation },
                new Label { Text = option.Subtitle, FontSize = 14, TextColor = Color.FromArgb("#50545A"), LineBreakMode = LineBreakMode.WordWrap }
            }
        };

        content.Add(iconTile, 0);
        content.Add(descriptions, 1);
        content.Add(new Label
        {
            Text = "›",
            FontSize = 30,
            TextColor = Color.FromArgb("#7A7F85"),
            VerticalOptions = LayoutOptions.Center,
            HorizontalTextAlignment = TextAlignment.Center
        }, 2);
        content.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await OnOptionSelectedAsync(option)) });
        SemanticProperties.SetDescription(content, $"{option.Title}. {option.Subtitle}");
        return content;
    }

    private async Task OnOptionSelectedAsync(NavigationOption option)
    {
        if (option.IsSignOut)
        {
            await CloseModalPagesAsync();
            await _signInService.SignOutAsync();
            return;
        }

        if (option.PageType is null) return;
        var page = _services.GetService(option.PageType) as Page;
        if (page is null) return;

        await CloseModalPagesAsync();
        await Shell.Current.Navigation.PushModalAsync(new NavigationPage(page));
    }

    private async void OnBackClicked(object? sender, EventArgs eventArgs)
    {
        if (Navigation.ModalStack.Count > 0) await Navigation.PopModalAsync();
        else if (Navigation.NavigationStack.Count > 1) await Navigation.PopAsync();
    }

    private sealed record NavigationOption(string Icon, string Title, string Subtitle, Type? PageType = null, bool IsSignOut = false);
}
