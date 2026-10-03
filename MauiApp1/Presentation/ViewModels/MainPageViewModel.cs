using System.Windows.Input;
using MauiApp1.Application.Authentication;
using MauiApp1.Application.Products;
using MauiApp1.Application.Lan;
using MauiApp1.Application.Sync;
using MauiApp1.Domain.Users;
using Microsoft.Maui.Networking;

namespace MauiApp1.Presentation.ViewModels;

public sealed class MainPageViewModel : System.ComponentModel.INotifyPropertyChanged
{
    private readonly IAuthenticationService _authenticationService;
    private readonly ICoordinatedSignInService _coordinatedSignInService;
    private readonly IProductService _productService;
    private readonly ICoordinatorPairingClient _coordinatorClient;
    private readonly IConnectivity _connectivity;
    private readonly ILocalOperationSyncService _operationSyncService;
    private readonly ICoordinatorRealtimeClient _realtimeClient;
    private string _lowStockAlertMessage = string.Empty;
    private string _coordinatorStatusMessage = "Consultando coordinador…";
    private string _internetStatusMessage = string.Empty;
    private string _syncStatusMessage = "Sincronización pendiente de consulta.";
    private bool _canSynchronize = true;
    private string _syncButtonText = "Sincronizar ahora";
    private string _realtimeStatusMessage = "Actualización en vivo pendiente de conexión.";

    public MainPageViewModel(IAuthenticationService authenticationService, ICoordinatedSignInService coordinatedSignInService, IProductService productService,
        ICoordinatorPairingClient coordinatorClient, IConnectivity connectivity, ILocalOperationSyncService operationSyncService,
        ICoordinatorRealtimeClient realtimeClient)
    {
        _authenticationService = authenticationService;
        _coordinatedSignInService = coordinatedSignInService;
        _productService = productService;
        _coordinatorClient = coordinatorClient;
        _connectivity = connectivity;
        _operationSyncService = operationSyncService;
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

    public string SyncStatusMessage
    {
        get => _syncStatusMessage;
        private set { if (_syncStatusMessage == value) return; _syncStatusMessage = value; OnPropertyChanged(nameof(SyncStatusMessage)); }
    }

    public bool CanSynchronize
    {
        get => _canSynchronize;
        private set { if (_canSynchronize == value) return; _canSynchronize = value; OnPropertyChanged(nameof(CanSynchronize)); }
    }

    public string SyncButtonText
    {
        get => _syncButtonText;
        private set { if (_syncButtonText == value) return; _syncButtonText = value; OnPropertyChanged(nameof(SyncButtonText)); }
    }

    public string RealtimeStatusMessage
    {
        get => _realtimeStatusMessage;
        private set { if (_realtimeStatusMessage == value) return; _realtimeStatusMessage = value; OnPropertyChanged(nameof(RealtimeStatusMessage)); }
    }

    public void SetRealtimeState(CoordinatorRealtimeState state)
    {
#if WINDOWS
        RealtimeStatusMessage = "Actualización en vivo lista; esta PC usa la base central del coordinador.";
#else
        RealtimeStatusMessage = state switch
        {
            CoordinatorRealtimeState.Connected => "Actualización en vivo conectada al coordinador.",
            CoordinatorRealtimeState.Connecting => "Conectando avisos en vivo al coordinador…",
            CoordinatorRealtimeState.Reconnecting => "Avisos en vivo desconectados; intentando reconectar…",
            CoordinatorRealtimeState.Disconnected => "Avisos en vivo no disponibles; puedes sincronizar manualmente.",
            CoordinatorRealtimeState.SessionUnavailable => "La sesión central no está disponible; inicia sesión de nuevo para recibir avisos en vivo.",
            _ => "Avisos en vivo disponibles al vincular e iniciar sesión con el coordinador."
        };
#endif
    }

    public async Task RefreshAfterCoordinatorChangesAsync()
    {
        await LoadLowStockAlertsAsync();
        var status = await _operationSyncService.GetStatusAsync();
        UpdateSyncStatus(status);
    }

    public async Task RefreshOperationSyncAsync()
    {
        if (!CanSynchronize) return;
        CanSynchronize = false;
        SyncButtonText = "Sincronizando…";
        try
        {
            var status = await _operationSyncService.SynchronizeAsync();
            UpdateSyncStatus(status);
        }
        catch
        {
            SyncStatusMessage = "No se pudo actualizar la cola. Las operaciones locales se conservan para reintentar.";
        }
        finally
        {
            SyncButtonText = "Sincronizar ahora";
            CanSynchronize = true;
        }
    }

    private void UpdateSyncStatus(LocalOperationSyncStatus status)
    {
        var last = status.LastConfirmedAtUtc is { } date
            ? date.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture)
            : "ninguna todavía";
        SyncStatusMessage = $"Pendientes de enviar: {status.PendingCount}. Conflictos por revisar: {status.ConflictCount}. Última confirmación: {last}." +
            (status.LatestConflictMessage is null ? string.Empty : $"\nDetalle de sincronización: {status.LatestConflictMessage}") +
            (_coordinatedSignInService.LastSignInStatus is { Length: > 0 } signInStatus ? $"\nEstado del acceso central: {signInStatus}" : string.Empty);
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
#if WINDOWS
            CoordinatorStatusMessage = status.IsAvailable
                ? "Coordinador disponible en esta PC. Este equipo no necesita vincularse como cliente."
                : status.IsConfigured
                    ? "El coordinador no responde en la red local. El trabajo guardado se conserva para reintentar."
                    : "El coordinador no está configurado o no responde en esta PC.";
#else
            CoordinatorStatusMessage = !status.IsConfigured
                ? "Coordinador sin configurar en este dispositivo. El trabajo local sigue disponible."
                : status.IsAvailable
                    ? status.IsConfirmed ? "Coordinador disponible en la red local." : "Coordinador disponible; vinculación pendiente."
                    : "Coordinador no responde. Puedes seguir trabajando localmente; se reintentará al volver a Inicio.";
#endif
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
