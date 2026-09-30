using System.Windows.Input;
using MauiApp1.Application.Authentication;
using MauiApp1.Application.Products;
using MauiApp1.Application.Lan;
using MauiApp1.Domain.Users;
using Microsoft.Maui.Networking;

namespace MauiApp1.Presentation.ViewModels;

public sealed class MainPageViewModel : System.ComponentModel.INotifyPropertyChanged
{
    private readonly IAuthenticationService _authenticationService;
    private readonly IProductService _productService;
    private readonly ICoordinatorPairingClient _coordinatorClient;
    private readonly IConnectivity _connectivity;
    private string _lowStockAlertMessage = string.Empty;
    private string _coordinatorStatusMessage = "Consultando coordinador…";
    private string _internetStatusMessage = string.Empty;

    public MainPageViewModel(IAuthenticationService authenticationService, IProductService productService,
        ICoordinatorPairingClient coordinatorClient, IConnectivity connectivity)
    {
        _authenticationService = authenticationService;
        _productService = productService;
        _coordinatorClient = coordinatorClient;
        _connectivity = connectivity;
        SignOutCommand = new Command(_authenticationService.SignOut);
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

    public string CoordinatorStatusMessage
    {
        get => _coordinatorStatusMessage;
        private set { if (_coordinatorStatusMessage == value) return; _coordinatorStatusMessage = value; OnPropertyChanged(nameof(CoordinatorStatusMessage)); }
    }

    public string InternetStatusMessage
    {
        get => _internetStatusMessage;
        private set { if (_internetStatusMessage == value) return; _internetStatusMessage = value; OnPropertyChanged(nameof(InternetStatusMessage)); }
    }

    public void RefreshInternetStatus()
    {
        try
        {
            InternetStatusMessage = _connectivity.NetworkAccess switch
            {
                NetworkAccess.Internet => "Internet: disponible según el dispositivo.",
                NetworkAccess.ConstrainedInternet => "Internet: acceso restringido; el acceso local se comprueba por separado.",
                NetworkAccess.Local => "Internet: no detectado; la red local puede seguir funcionando.",
                NetworkAccess.None => "Internet: sin acceso detectado; las operaciones locales continúan disponibles.",
                _ => "Internet: estado desconocido; la LAN se verifica por separado."
            };
        }
        catch
        {
            InternetStatusMessage = "Internet: estado no disponible; la LAN se verifica por separado.";
        }
    }

    public async Task RefreshCoordinatorStatusAsync()
    {
        CoordinatorStatusMessage = "Consultando coordinador…";
        try
        {
            var status = await _coordinatorClient.CheckAvailabilityAsync();
            CoordinatorStatusMessage = !status.IsConfigured
                ? "Coordinador sin configurar en este dispositivo. El trabajo local sigue disponible."
                : status.IsAvailable
                    ? status.IsConfirmed ? "Coordinador disponible en la red local." : "Coordinador disponible; vinculación pendiente."
                    : "Coordinador no responde. Puedes seguir trabajando localmente; se reintentará al volver a Inicio.";
        }
        catch
        {
            CoordinatorStatusMessage = "No se pudo comprobar el coordinador. El trabajo local sigue disponible.";
        }
    }

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
