using System.Windows.Input;
using MauiApp1.Application.Authentication;
using MauiApp1.Application.Products;
using MauiApp1.Application.Sync;
using MauiApp1.Domain.Users;

namespace MauiApp1.Presentation.ViewModels;

public sealed class MainPageViewModel : System.ComponentModel.INotifyPropertyChanged
{
    private readonly IAuthenticationService _authenticationService;
    private readonly ICoordinatedSignInService _coordinatedSignInService;
    private readonly IProductService _productService;
    private readonly ICoordinatorRealtimeClient _realtimeClient;
    private string _lowStockAlertMessage = string.Empty;

    public MainPageViewModel(IAuthenticationService authenticationService, ICoordinatedSignInService coordinatedSignInService, IProductService productService,
        ICoordinatorRealtimeClient realtimeClient)
    {
        _authenticationService = authenticationService;
        _coordinatedSignInService = coordinatedSignInService;
        _productService = productService;
        _realtimeClient = realtimeClient;
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

    public ICommand SignOutCommand { get; }

    public async Task LoadLowStockAlertsAsync()
    {
        try
        {
            var products = LowStockProductQuery.Select(await _productService.GetAllAsync());
            LowStockAlertMessage = products.Count == 0
                ? string.Empty
                : "Existencias en el mínimo o por debajo:\n" + string.Join("\n", products.Select(product =>
                    $"• {product.Name}: {product.Stock.ToDecimal().ToString("0.000", System.Globalization.CultureInfo.CurrentCulture)} {product.Unit} (mínimo {product.MinimumStock.ToDecimal().ToString("0.000", System.Globalization.CultureInfo.CurrentCulture)} {product.Unit})"));
        }
        catch
        {
            LowStockAlertMessage = "No se pudieron consultar las alertas de existencias.";
        }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
}
