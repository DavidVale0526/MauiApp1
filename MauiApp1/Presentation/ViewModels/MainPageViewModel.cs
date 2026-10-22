using System.Windows.Input;
using MauiApp1.Application.Authentication;
using MauiApp1.Application.Products;
using MauiApp1.Application.Sync;
using MauiApp1.Domain.Users;
using MauiApp1.Application.Reports;

namespace MauiApp1.Presentation.ViewModels;

public sealed class MainPageViewModel : System.ComponentModel.INotifyPropertyChanged
{
    private readonly IAuthenticationService _authenticationService;
    private readonly ICoordinatedSignInService _coordinatedSignInService;
    private readonly IProductService _productService;
    private readonly ICoordinatorRealtimeClient _realtimeClient;
    private string _lowStockAlertMessage = string.Empty;
    private readonly IReportService _reportService;
    private readonly SemaphoreSlim _weeklySalesGate = new(1, 1);
    private WeeklySalesSummary? _weeklySales;
    private bool _isWeeklySalesLoading;
    private string _weeklySalesMessage = string.Empty;
    private readonly SemaphoreSlim _alertsGate = new(1, 1);
    private IReadOnlyList<OperationalStockAlert> _operationalAlerts = [];
    private bool _isOperationalAlertsLoading;
    private bool _operationalAlertsLoaded;
    private string _operationalAlertsMessage = string.Empty;

    public MainPageViewModel(IAuthenticationService authenticationService, ICoordinatedSignInService coordinatedSignInService, IProductService productService,
        ICoordinatorRealtimeClient realtimeClient, IReportService reportService)
    {
        _authenticationService = authenticationService;
        _coordinatedSignInService = coordinatedSignInService;
        _productService = productService;
        _realtimeClient = realtimeClient;
        _reportService = reportService;
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

    public IReadOnlyList<OperationalStockAlert> OperationalAlerts => _operationalAlerts.Take(4).ToArray();
    public bool IsOperationalAlertsLoading => _isOperationalAlertsLoading;
    public string OperationalAlertsMessage => _operationalAlertsMessage;
    public bool HasOperationalAlertsMessage => !string.IsNullOrEmpty(_operationalAlertsMessage);
    public string OperationalAlertsCount => _operationalAlertsLoaded ? $"{_operationalAlerts.Count} alertas" : "—";

    public ICommand SignOutCommand { get; }

    public IReadOnlyList<DailySalesTotal> WeeklySalesDays => _weeklySales?.Days ?? [];
    public string WeeklySalesAverage => _weeklySales is null ? "—" : WeeklySalesSummary.FormatAmount(_weeklySales.DailyAverage);
    public bool IsWeeklySalesLoading => _isWeeklySalesLoading;
    public string WeeklySalesMessage => _weeklySalesMessage;
    public bool HasWeeklySalesMessage => !string.IsNullOrEmpty(_weeklySalesMessage);

    public async Task LoadWeeklySalesAsync()
    {
        await _weeklySalesGate.WaitAsync();
        try
        {
            _isWeeklySalesLoading = true;
            _weeklySalesMessage = string.Empty;
            NotifyWeeklySalesChanged();
            var today = DateTime.Today;
            var zone = TimeZoneInfo.Local;
            var fromUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(today.AddDays(-6), DateTimeKind.Unspecified), zone);
            var toUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(today.AddDays(1), DateTimeKind.Unspecified), zone);
            var sales = await _reportService.GetSalesAsync(fromUtc, toUtc, null, null);
            _weeklySales = WeeklySalesSummary.Create(sales, today, zone);
            _weeklySalesMessage = _weeklySales.HasSales ? string.Empty : "No hay ventas registradas en este período.";
        }
        catch
        {
            _weeklySales = null;
            _weeklySalesMessage = "No se pudieron cargar las ventas. Vuelve a intentarlo.";
        }
        finally
        {
            _isWeeklySalesLoading = false;
            NotifyWeeklySalesChanged();
            _weeklySalesGate.Release();
        }
    }

    private void NotifyWeeklySalesChanged()
    {
        OnPropertyChanged(nameof(WeeklySalesDays));
        OnPropertyChanged(nameof(WeeklySalesAverage));
        OnPropertyChanged(nameof(IsWeeklySalesLoading));
        OnPropertyChanged(nameof(WeeklySalesMessage));
        OnPropertyChanged(nameof(HasWeeklySalesMessage));
    }

    public async Task LoadLowStockAlertsAsync()
    {
        await _alertsGate.WaitAsync();
        try
        {
            _isOperationalAlertsLoading = true;
            _operationalAlertsMessage = string.Empty;
            NotifyOperationalAlertsChanged();
            var products = LowStockProductQuery.Select(await _productService.GetAllAsync());
            _operationalAlerts = OperationalStockAlert.Create(products);
            _operationalAlertsLoaded = true;
            _operationalAlertsMessage = products.Count == 0 ? "No hay alertas de existencias en los productos activos." : string.Empty;
            LowStockAlertMessage = products.Count == 0
                ? string.Empty
                : "Existencias en el mínimo o por debajo:\n" + string.Join("\n", products.Select(product =>
                    $"• {product.Name}: {product.Stock.ToDecimal().ToString("0.###", System.Globalization.CultureInfo.CurrentCulture)} {product.Unit} (mínimo {product.MinimumStock.ToDecimal().ToString("0.###", System.Globalization.CultureInfo.CurrentCulture)} {product.Unit})"));
        }
        catch
        {
            _operationalAlerts = [];
            _operationalAlertsLoaded = false;
            _operationalAlertsMessage = "No se pudieron consultar las alertas de existencias. Inténtalo de nuevo.";
            LowStockAlertMessage = "No se pudieron consultar las alertas de existencias.";
        }
        finally
        {
            _isOperationalAlertsLoading = false;
            NotifyOperationalAlertsChanged();
            _alertsGate.Release();
        }
    }

    private void NotifyOperationalAlertsChanged()
    {
        OnPropertyChanged(nameof(OperationalAlerts));
        OnPropertyChanged(nameof(IsOperationalAlertsLoading));
        OnPropertyChanged(nameof(OperationalAlertsMessage));
        OnPropertyChanged(nameof(HasOperationalAlertsMessage));
        OnPropertyChanged(nameof(OperationalAlertsCount));
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
}
