using System.Windows.Input;
using MauiApp1.Application.Authentication;
using MauiApp1.Application.Dashboard;
using MauiApp1.Application.Products;
using MauiApp1.Application.Sync;
using MauiApp1.Domain.Users;
using System.Globalization;

namespace MauiApp1.Presentation.ViewModels;

public sealed class MainPageViewModel : System.ComponentModel.INotifyPropertyChanged
{
    private readonly IAuthenticationService _authenticationService;
    private readonly ICoordinatedSignInService _coordinatedSignInService;
    private readonly IProductService _productService;
    private readonly IDashboardService _dashboardService;
    private readonly ICoordinatorRealtimeClient _realtimeClient;
    private string _lowStockAlertMessage = string.Empty;
    private string _salesTodayText = "C$ 0.00";
    private string _salesTodayCountText = "0 ventas";
    private string _salesTodayCountValueText = "0";
    private string _salesMonthText = "C$ 0.00";
    private string _lowStockCountText = "0";
    private string _dashboardErrorMessage = string.Empty;
    private bool _hasDashboardData;
    private IReadOnlyList<DashboardSalesPoint> _salesLastSevenDays = [];
    private IReadOnlyList<DashboardTopProductCard> _topProducts = [];
    private IReadOnlyList<DashboardRecentSaleCard> _recentSales = [];

    public MainPageViewModel(IAuthenticationService authenticationService, ICoordinatedSignInService coordinatedSignInService, IProductService productService,
        ICoordinatorRealtimeClient realtimeClient, IDashboardService dashboardService)
    {
        _authenticationService = authenticationService;
        _coordinatedSignInService = coordinatedSignInService;
        _productService = productService;
        _realtimeClient = realtimeClient;
        _dashboardService = dashboardService;
        SignOutCommand = new Command(async () =>
        {
            await _realtimeClient.StopAsync();
            await _coordinatedSignInService.SignOutAsync();
        });
    }

    public string Title => "Miscelánea Ari";

    public string CurrentUserName => _authenticationService.CurrentUser?.UserName ?? string.Empty;

    public string CurrentRole => _authenticationService.CurrentUser?.Role switch
    {
        UserRole.Administrator => "Administrador",
        UserRole.Cashier => "Cajero",
        _ => string.Empty
    };

    public bool IsAdministrator => _authenticationService.CurrentUser?.Role == UserRole.Administrator;
    public string DataNotice => "Datos locales de este dispositivo; pueden incluir operaciones aún no sincronizadas.";

    public string SalesTodayText { get => _salesTodayText; private set => SetValue(ref _salesTodayText, value); }
    public string SalesTodayCountText { get => _salesTodayCountText; private set => SetValue(ref _salesTodayCountText, value); }
    public string SalesTodayCountValueText { get => _salesTodayCountValueText; private set => SetValue(ref _salesTodayCountValueText, value); }
    public string SalesMonthText { get => _salesMonthText; private set => SetValue(ref _salesMonthText, value); }
    public string LowStockCountText { get => _lowStockCountText; private set => SetValue(ref _lowStockCountText, value); }
    public string DashboardErrorMessage
    {
        get => _dashboardErrorMessage;
        private set
        {
            if (SetValue(ref _dashboardErrorMessage, value)) OnPropertyChanged(nameof(HasDashboardError));
        }
    }
    public bool HasDashboardError => !string.IsNullOrWhiteSpace(DashboardErrorMessage);
    public bool HasDashboardData { get => _hasDashboardData; private set => SetValue(ref _hasDashboardData, value); }
    public IReadOnlyList<DashboardSalesPoint> SalesLastSevenDays { get => _salesLastSevenDays; private set => SetValue(ref _salesLastSevenDays, value); }
    public IReadOnlyList<DashboardTopProductCard> TopProducts
    {
        get => _topProducts;
        private set
        {
            if (SetValue(ref _topProducts, value)) OnPropertyChanged(nameof(HasNoTopProducts));
        }
    }
    public bool HasNoTopProducts => HasDashboardData && TopProducts.Count == 0;
    public IReadOnlyList<DashboardRecentSaleCard> RecentSales
    {
        get => _recentSales;
        private set
        {
            if (SetValue(ref _recentSales, value)) OnPropertyChanged(nameof(HasNoRecentSales));
        }
    }
    public bool HasNoRecentSales => HasDashboardData && RecentSales.Count == 0;

    public string LowStockAlertMessage
    {
        get => _lowStockAlertMessage;
        private set
        {
            if (string.Equals(_lowStockAlertMessage, value, StringComparison.Ordinal)) return;
            _lowStockAlertMessage = value;
            OnPropertyChanged(nameof(LowStockAlertMessage));
            OnPropertyChanged(nameof(HasLowStockAlert));
        }
    }

    public bool HasLowStockAlert => !string.IsNullOrWhiteSpace(LowStockAlertMessage);

    public ICommand SignOutCommand { get; }

    public async Task LoadDashboardAsync()
    {
        HasDashboardData = false;
        DashboardErrorMessage = string.Empty;
        try
        {
            var snapshot = await _dashboardService.GetSnapshotAsync();
            SalesTodayText = FormatCurrency(snapshot.SalesTodayMinorUnits);
            SalesTodayCountValueText = snapshot.SalesTodayCount.ToString("N0", CultureInfo.CurrentCulture);
            SalesTodayCountText = $"{snapshot.SalesTodayCount.ToString("N0", CultureInfo.CurrentCulture)} {(snapshot.SalesTodayCount == 1 ? "venta" : "ventas")}";
            SalesMonthText = FormatCurrency(snapshot.SalesMonthMinorUnits);
            SalesLastSevenDays = snapshot.SalesLastSevenDays;
            TopProducts = snapshot.TopProducts
                .Select((product, index) => new DashboardTopProductCard(index + 1, product))
                .ToArray();
            RecentSales = snapshot.RecentSales.Select(sale => new DashboardRecentSaleCard(sale)).ToArray();
            HasDashboardData = true;
            OnPropertyChanged(nameof(HasNoTopProducts));
            OnPropertyChanged(nameof(HasNoRecentSales));
        }
        catch
        {
            DashboardErrorMessage = "No se pudo cargar el resumen. Los accesos de operación siguen disponibles.";
        }

        await LoadLowStockAlertsAsync();
    }

    public async Task LoadLowStockAlertsAsync()
    {
        try
        {
            var products = LowStockProductQuery.Select(await _productService.GetAllAsync());
            LowStockCountText = products.Count.ToString("N0", CultureInfo.CurrentCulture);
            LowStockAlertMessage = products.Count == 0
                ? string.Empty
                : "Existencias en el mínimo o por debajo:\n" + string.Join("\n", products.Select(product =>
                    $"• {product.Name}: {product.Stock.ToDecimal().ToString("0.###", System.Globalization.CultureInfo.CurrentCulture)} {product.Unit} (mínimo {product.MinimumStock.ToDecimal().ToString("0.###", System.Globalization.CultureInfo.CurrentCulture)} {product.Unit})"));
        }
        catch
        {
            LowStockCountText = "—";
            LowStockAlertMessage = "No se pudieron consultar las alertas de existencias.";
        }
    }

    private static string FormatCurrency(long minorUnits) =>
        $"C$ {(minorUnits / 100m).ToString("N2", CultureInfo.CurrentCulture)}";

    private bool SetValue<T>(ref T storage, T value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value)) return false;
        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string? propertyName) =>
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
}

public sealed record DashboardTopProductCard(int Position, DashboardTopProduct Product)
{
    public string Name => Product.ProductName;
    public string QuantityText => $"{(Product.QuantityMilliUnits / 1000m).ToString("0.###", CultureInfo.CurrentCulture)} {Product.Unit}";
}

public sealed record DashboardRecentSaleCard(DashboardRecentSale Sale)
{
    public string DateText => Sale.OccurredAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
    public string TypeText => Sale.IsCredit ? "Crédito" : "Contado";
    public string CustomerText => $"Cliente: {Sale.CustomerName ?? "Público general"}";
    public string ActorText => $"Usuario: {Sale.ActorUserName}";
    public string ItemsText => $"{Sale.LineCount.ToString("N0", CultureInfo.CurrentCulture)} {(Sale.LineCount == 1 ? "producto" : "productos")}";
    public string TotalText => $"C$ {(Sale.TotalMinorUnits / 100m).ToString("N2", CultureInfo.CurrentCulture)}";
}
