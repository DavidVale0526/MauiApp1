using System.Windows.Input;
using MauiApp1.Application.Authentication;
using MauiApp1.Application.Products;
using MauiApp1.Application.Sync;
using MauiApp1.Domain.Users;
using MauiApp1.Application.Reports;
using MauiApp1.Application.Sales;
using MauiApp1.Application.Purchases;
using MauiApp1.Application.Inventory;
using MauiApp1.Application.Credit;
using MauiApp1.Application.Lan;
using MauiApp1.Configuration;

namespace MauiApp1.Presentation.ViewModels;

public sealed class MainPageViewModel : System.ComponentModel.INotifyPropertyChanged
{
    private readonly IAuthenticationService _authenticationService;
    private readonly ICoordinatedSignInService _coordinatedSignInService;
    private readonly IProductService _productService;
    private readonly ICoordinatorRealtimeClient _realtimeClient;
    private readonly ICoordinatorPairingClient _coordinatorClient;
    private readonly ILocalOperationSyncService _headerSyncService;
    private readonly DesktopCoordinatorModeOptions _desktopMode;
    private readonly SemaphoreSlim _headerGate = new(1, 1);
    private bool _headerRefreshRequested;
    public string CoordinatorStatusText { get; private set; } = "Consultando Coordinador…";
    public string CoordinatorStatusIcon { get; private set; } = "↻";
    public string CoordinatorStatusColor { get; private set; } = "#52657D";
    public string CoordinatorStatusDisplay => $"{CoordinatorStatusIcon}  {CoordinatorStatusText}";
    public string HeaderStatusDetail { get; private set; } = "Abre Sincronización y conexión para consultar los detalles.";
    public string SyncStatusText { get; private set; } = "Consultando pendientes locales…";
    public bool IsHeaderStatusLoading { get; private set; }
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
    private readonly ISaleService _saleService;
    private readonly IPurchaseService _purchaseService;
    private readonly IInventoryMovementService _movementService;
    private readonly IPaymentService _paymentService;
    private readonly SemaphoreSlim _recentOperationsGate = new(1, 1);
    public IReadOnlyList<RecentOperationItem> RecentOperations { get; private set; } = [];
    public bool IsRecentOperationsLoading { get; private set; }
    public string RecentOperationsMessage { get; private set; } = string.Empty;
    public bool HasRecentOperationsMessage => !string.IsNullOrEmpty(RecentOperationsMessage);
    public string RecentOperationsSubtitle => RecentOperations.Count == 1
        ? "1 operación reciente" : $"{RecentOperations.Count} operaciones recientes";

    public MainPageViewModel(IAuthenticationService authenticationService, ICoordinatedSignInService coordinatedSignInService, IProductService productService,
        ICoordinatorRealtimeClient realtimeClient, IReportService reportService,
        ISaleService saleService, IPurchaseService purchaseService, IInventoryMovementService movementService, IPaymentService paymentService,
        ICoordinatorPairingClient coordinatorClient, ILocalOperationSyncService headerSyncService, DesktopCoordinatorModeOptions desktopMode)
    {
        _authenticationService = authenticationService;
        _coordinatedSignInService = coordinatedSignInService;
        _productService = productService;
        _realtimeClient = realtimeClient;
        _coordinatorClient = coordinatorClient;
        _headerSyncService = headerSyncService;
        _desktopMode = desktopMode;
        _reportService = reportService;
        _saleService = saleService;
        _purchaseService = purchaseService;
        _movementService = movementService;
        _paymentService = paymentService;
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
    public string OperationalAlertsCount => !_operationalAlertsLoaded ? "—"
        : _operationalAlerts.Count == 1 ? "1 alerta" : $"{_operationalAlerts.Count} alertas";
    public string LowStockKpiValue => _isOperationalAlertsLoading ? "Cargando…"
        : _operationalAlertsLoaded ? _operationalAlerts.Count.ToString(System.Globalization.CultureInfo.CurrentCulture)
        : HasOperationalAlertsMessage ? "Error" : "—";
    public string LowStockKpiStatus => _isOperationalAlertsLoading ? "Consultando existencias"
        : _operationalAlertsLoaded ? (_operationalAlerts.Count == 1
            ? "Producto activo en el mínimo o por debajo" : "Productos activos en el mínimo o por debajo")
        : HasOperationalAlertsMessage ? "No se pudieron consultar las existencias" : "Pendiente de consulta";

    public ICommand SignOutCommand { get; }

    public async Task RefreshHeaderAsync()
    {
        // Coalesce overlapping UI events, with a final read for changes received during a check.
        _headerRefreshRequested = true;
        if (!await _headerGate.WaitAsync(0)) return;
        try
        {
            IsHeaderStatusLoading = true;
            OnPropertyChanged(nameof(IsHeaderStatusLoading));
            do
            {
                _headerRefreshRequested = false;
                OnPropertyChanged(nameof(CurrentUserName));
                OnPropertyChanged(nameof(CurrentRole));
                await Task.WhenAll(RefreshCoordinatorHeaderAsync(), RefreshSyncHeaderAsync());
            } while (_headerRefreshRequested);
        }
        finally
        {
            IsHeaderStatusLoading = false;
            OnPropertyChanged(nameof(IsHeaderStatusLoading));
            _headerGate.Release();
        }
    }

    private async Task RefreshCoordinatorHeaderAsync()
    {
        try
        {
            if (_desktopMode.IsHost)
            {
                SetCoordinatorHeader("Coordinador local", "⌂", "#52657D",
                    "Modo Host: esta aplicación comparte la base central. Este indicador no comprueba si el servicio está respondiendo.");
                return;
            }

            var status = await _coordinatorClient.CheckAvailabilityAsync();
            if (!status.IsConfigured)
                SetCoordinatorHeader("Sin vincular", "?", "#8A4B08",
                    "No hay una vinculación configurada con el Coordinador.");
            else if (!status.IsAvailable)
                SetCoordinatorHeader("No disponible", "!", "#A22B2B",
                    "No se pudo comprobar la disponibilidad HTTPS del Coordinador. Revisa los detalles de conexión.");
            else if (!status.IsConfirmed)
                SetCoordinatorHeader("Vinculación pendiente", "↗", "#8A4B08",
                    "El Coordinador responde por HTTPS, pero la vinculación guardada aún no está confirmada.");
            else
                SetCoordinatorHeader("Coordinador disponible", "✓", "#007A56",
                    "El Coordinador responde por HTTPS y la vinculación guardada está confirmada. Esto no verifica que la sesión siga autorizada.");
        }
        catch
        {
            SetCoordinatorHeader("No disponible", "!", "#A22B2B",
                "No se pudo consultar el estado del Coordinador.");
        }
    }

    private void SetCoordinatorHeader(string text, string icon, string color, string detail)
    {
        CoordinatorStatusText = text;
        CoordinatorStatusIcon = icon;
        CoordinatorStatusColor = color;
        HeaderStatusDetail = detail + " Abre Sincronización y conexión para más información.";
        OnPropertyChanged(nameof(CoordinatorStatusText));
        OnPropertyChanged(nameof(CoordinatorStatusIcon));
        OnPropertyChanged(nameof(CoordinatorStatusDisplay));
        OnPropertyChanged(nameof(CoordinatorStatusColor));
        OnPropertyChanged(nameof(HeaderStatusDetail));
    }

    private async Task RefreshSyncHeaderAsync()
    {
        try
        {
            var status = await _headerSyncService.GetStatusAsync();
            var pending = status.PendingCount == 1 ? "1 pendiente local" : $"{status.PendingCount} pendientes locales";
            var conflicts = status.ConflictCount == 1 ? "1 conflicto local" : $"{status.ConflictCount} conflictos locales";
            SyncStatusText = status.ConflictCount > 0 ? $"{pending} · {conflicts}"
                : status.PendingCount > 0 ? pending
                : !string.IsNullOrWhiteSpace(status.LatestConflictMessage) ? "Revisar sincronización"
                : "Sin pendientes locales";
        }
        catch
        {
            SyncStatusText = "Estado de sincronización no disponible";
        }
        OnPropertyChanged(nameof(SyncStatusText));
    }

    public async Task LoadRecentOperationsAsync()
    {
        await _recentOperationsGate.WaitAsync();
        try
        {
            IsRecentOperationsLoading = true;
            RecentOperationsMessage = string.Empty;
            RecentOperations = [];
            NotifyRecentOperationsChanged();
            var sales = await _saleService.GetHistoryAsync();
            var purchases = await _purchaseService.GetHistoryAsync();
            var movements = await _movementService.GetHistoryAsync();
            var payments = new List<PaymentHistoryEntry>();
            foreach (var account in await _paymentService.GetAccountsAsync())
                payments.AddRange(await _paymentService.GetHistoryAsync(account.OperationId));
            RecentOperations = RecentOperationItem.SelectLatest(sales, purchases, movements, payments);
            RecentOperationsMessage = RecentOperations.Count == 0 ? "No hay operaciones registradas." : string.Empty;
        }
        catch
        {
            RecentOperations = [];
            RecentOperationsMessage = "No se pudieron cargar las últimas operaciones. Inténtalo de nuevo.";
        }
        finally
        {
            IsRecentOperationsLoading = false;
            NotifyRecentOperationsChanged();
            _recentOperationsGate.Release();
        }
    }

    private void NotifyRecentOperationsChanged()
    {
        OnPropertyChanged(nameof(RecentOperations));
        OnPropertyChanged(nameof(RecentOperationsSubtitle));
        OnPropertyChanged(nameof(IsRecentOperationsLoading));
        OnPropertyChanged(nameof(RecentOperationsMessage));
        OnPropertyChanged(nameof(HasRecentOperationsMessage));
    }

    public IReadOnlyList<DailySalesTotal> WeeklySalesDays => _weeklySales?.Days ?? [];
    public bool HasPreviousSales => WeeklySalesDays.Any(day => !day.IsToday && day.Total > 0);
    public string WeeklySalesAverage => _weeklySales is null ? "—" : WeeklySalesSummary.FormatAmount(_weeklySales.DailyAverage);
    public bool IsWeeklySalesLoading => _isWeeklySalesLoading;
    public string WeeklySalesMessage => _weeklySalesMessage;
    public bool HasWeeklySalesMessage => !string.IsNullOrEmpty(_weeklySalesMessage);
    private DailySalesTotal? TodaySales => _weeklySales?.Days.FirstOrDefault(day => day.Date.Date == DateTime.Today);
    public string TodaySalesKpiValue => _isWeeklySalesLoading ? "Cargando…"
        : TodaySales is { } today ? today.AmountText
        : _weeklySales is null && HasWeeklySalesMessage ? "Error" : "—";
    public string TodaySalesKpiStatus => _isWeeklySalesLoading ? "Consultando ventas"
        : TodaySales is not null ? "Total del día local · contado y crédito"
        : _weeklySales is not null ? "Actualiza las ventas para el día actual"
        : HasWeeklySalesMessage ? "No se pudieron consultar las ventas" : "Pendiente de consulta";

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
        OnPropertyChanged(nameof(HasPreviousSales));
        OnPropertyChanged(nameof(WeeklySalesAverage));
        OnPropertyChanged(nameof(IsWeeklySalesLoading));
        OnPropertyChanged(nameof(WeeklySalesMessage));
        OnPropertyChanged(nameof(HasWeeklySalesMessage));
        OnPropertyChanged(nameof(TodaySalesKpiValue));
        OnPropertyChanged(nameof(TodaySalesKpiStatus));
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
        OnPropertyChanged(nameof(LowStockKpiValue));
        OnPropertyChanged(nameof(LowStockKpiStatus));
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
}
