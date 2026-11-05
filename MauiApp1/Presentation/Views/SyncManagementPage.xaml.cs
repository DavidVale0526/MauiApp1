using MauiApp1.Application.Authentication;
using MauiApp1.Application.Inventory;
using MauiApp1.Application.Lan;
using MauiApp1.Application.Sync;
using MauiApp1.Configuration;
using MauiApp1.Domain.Users;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Networking;

namespace MauiApp1.Presentation.Views;

public partial class SyncManagementPage : CoordinatorRefreshPage
{
    private readonly ICoordinatorPairingClient _coordinatorClient;
    private readonly ICoordinatorDeviceDirectory _deviceDirectory;
    private readonly IConnectivity _connectivity;
    private readonly ILocalOperationSyncService _operationSyncService;
    private readonly ICoordinatedSignInService _coordinatedSignInService;
    private readonly ICoordinatorRealtimeClient _realtimeClient;
    private readonly DesktopCoordinatorModeOptions _desktopMode;
    private readonly IInventoryDiscrepancyService _discrepancyService;
    private readonly IAuthenticationService _authenticationService;
    private readonly ICoordinatorLinkStorage _linkStorage;
    private readonly bool _isAdministrator;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly SemaphoreSlim _deviceRefreshGate = new(1, 1);
    private IDispatcherTimer? _deviceRefreshTimer;

    public SyncManagementPage(
        ICoordinatorPairingClient coordinatorClient,
        ICoordinatorDeviceDirectory deviceDirectory,
        IConnectivity connectivity,
        ILocalOperationSyncService operationSyncService,
        ICoordinatedSignInService coordinatedSignInService,
        ICoordinatorRealtimeClient realtimeClient,
        DesktopCoordinatorModeOptions desktopMode,
        IInventoryDiscrepancyService discrepancyService,
        ICoordinatorLinkStorage linkStorage,
        IAuthenticationService authenticationService,
        ILocalDataChangeNotifier changeNotifier) : base(changeNotifier)
    {
        InitializeComponent();
        _coordinatorClient = coordinatorClient;
        _deviceDirectory = deviceDirectory;
        _connectivity = connectivity;
        _operationSyncService = operationSyncService;
        _coordinatedSignInService = coordinatedSignInService;
        _realtimeClient = realtimeClient;
        _desktopMode = desktopMode;
        _discrepancyService = discrepancyService;
        _linkStorage = linkStorage;
        _authenticationService = authenticationService;
        _isAdministrator = authenticationService.CurrentUser?.Role == UserRole.Administrator;
        ReviewConflictsButton.IsVisible = _isAdministrator;
        ReviewShortagesButton.IsVisible = _isAdministrator;
        DevicesCard.IsVisible = _isAdministrator;
#if WINDOWS
        InitialImportButton.IsVisible = _isAdministrator && !_desktopMode.IsHost;
#else
        InitialImportButton.IsVisible = _isAdministrator;
#endif
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _connectivity.ConnectivityChanged += OnConnectivityChanged;
        _realtimeClient.StateChanged += OnRealtimeStateChanged;
        if (_isAdministrator)
        {
            if (_deviceRefreshTimer is null)
            {
                _deviceRefreshTimer = Dispatcher.CreateTimer();
                _deviceRefreshTimer.Interval = TimeSpan.FromSeconds(15);
                _deviceRefreshTimer.IsRepeating = true;
                _deviceRefreshTimer.Tick += OnDeviceRefreshTick;
            }
            _deviceRefreshTimer.Start();
        }
        await _realtimeClient.StartAsync();
        await RefreshStatusAsync();
    }

    protected override void OnDisappearing()
    {
        _connectivity.ConnectivityChanged -= OnConnectivityChanged;
        _realtimeClient.StateChanged -= OnRealtimeStateChanged;
        _deviceRefreshTimer?.Stop();
        base.OnDisappearing();
    }

    protected override Task RefreshAfterCoordinatorChangesAsync() => RefreshStatusAsync();

    private async void OnBackClicked(object? sender, EventArgs args) => await Navigation.PopModalAsync();

    private async void OnRefreshClicked(object? sender, EventArgs args) => await RefreshStatusAsync();

    private async void OnSynchronizeClicked(object? sender, EventArgs args)
    {
        SynchronizeButton.IsEnabled = false;
        SynchronizeButton.Text = "Sincronizando…";
        string? error = null;
        try
        {
            await _operationSyncService.SynchronizeAsync();
        }
        catch
        {
            error = "No se pudo completar. Los cambios siguen guardados en este dispositivo y se pueden reintentar.";
        }
        finally
        {
            SynchronizeButton.Text = "Sincronizar ahora";
            SynchronizeButton.IsEnabled = true;
        }

        await RefreshStatusAsync(error);
    }

    private async void OnReviewConflictsClicked(object? sender, EventArgs args)
    {
        if (!_isAdministrator) return;
        if (Handler?.MauiContext?.Services.GetRequiredService<ConflictReviewPage>() is not { } page) return;
        await Navigation.PushModalAsync(new NavigationPage(page));
    }

    private async void OnReviewShortagesClicked(object? sender, EventArgs args)
    {
        if (!_isAdministrator) return;
        if (Handler?.MauiContext?.Services.GetRequiredService<InventoryDiscrepancyPage>() is not { } page) return;
        await Navigation.PushModalAsync(new NavigationPage(page));
    }

    private async void OnDevicePairingClicked(object? sender, EventArgs args)
    {
        if (Handler?.MauiContext?.Services.GetRequiredService<DevicePairingPage>() is not { } page) return;
        await Navigation.PushModalAsync(new NavigationPage(page));
    }

    private async void OnInitialImportClicked(object? sender, EventArgs args)
    {
        if (!_isAdministrator) return;
        if (Handler?.MauiContext?.Services.GetRequiredService<InitialDataImportPage>() is not { } page) return;
        await Navigation.PushModalAsync(new NavigationPage(page));
    }

    private async void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs args)
    {
        try { await _operationSyncService.SynchronizeAsync(); }
        catch { }
        await RefreshStatusAsync();
    }

    private void OnRealtimeStateChanged(CoordinatorRealtimeState state) =>
        MainThread.BeginInvokeOnMainThread(() => RealtimeStatusLabel.Text = GetRealtimeStatusMessage(state));

    private async void OnDeviceRefreshTick(object? sender, EventArgs args)
    {
        if (_isAdministrator) await RefreshDeviceListAsync();
    }

    private async Task RefreshStatusAsync(string? syncError = null)
    {
        await _refreshGate.WaitAsync();
        try
        {
            await RefreshCoordinatorStatusAsync();
            RefreshNetworkStatus();
            RealtimeStatusLabel.Text = GetRealtimeStatusMessage(_realtimeClient.State);
            await RefreshSyncStatusAsync(syncError);
            await RefreshLocalAccountStatusAsync();
            await RefreshShortageStatusAsync();
            if (_isAdministrator) await RefreshDeviceListAsync();
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task RefreshCoordinatorStatusAsync()
    {
#if WINDOWS
        if (_desktopMode.IsHost)
        {
            CoordinatorStatusLabel.Text = "Modo coordinador: esta instalación usa la base central configurada.";
            return;
        }
#endif

        CoordinatorStatusLabel.Text = "Consultando coordinador…";
        try
        {
            var status = await _coordinatorClient.CheckAvailabilityAsync();
#if WINDOWS
            CoordinatorStatusLabel.Text = !status.IsConfigured
                ? "Modo cliente Windows: vincula este equipo al coordinador para sincronizar."
                : status.IsAvailable
                    ? status.IsConfirmed ? "Cliente Windows vinculado; coordinador disponible." : "Cliente Windows: falta completar la vinculación."
                    : "Cliente Windows vinculado; el coordinador no responde en la red local. Los cambios se conservan para reintentar.";
#else
            CoordinatorStatusLabel.Text = !status.IsConfigured
                ? "Coordinador sin configurar en este dispositivo. El trabajo local sigue disponible."
                : status.IsAvailable
                    ? status.IsConfirmed ? "Coordinador disponible y dispositivo vinculado." : "Coordinador disponible; falta completar la vinculación."
                    : "Coordinador no responde. El trabajo local sigue disponible.";
#endif
        }
        catch
        {
            CoordinatorStatusLabel.Text = "No se pudo comprobar el coordinador. El trabajo local sigue disponible.";
        }
    }

    private void RefreshNetworkStatus()
    {
        try
        {
            NetworkStatusLabel.Text = _connectivity.NetworkAccess switch
            {
                NetworkAccess.Internet => "Red: Internet disponible. La sincronización usa la red local del negocio.",
                NetworkAccess.ConstrainedInternet => "Red: acceso a Internet restringido; la conexión local se comprueba por separado.",
                NetworkAccess.Local => "Red: sin salida a Internet detectada; la red local puede seguir funcionando.",
                NetworkAccess.None => "Red: no se detecta conexión. Puedes seguir trabajando con los datos locales.",
                _ => "Red: estado desconocido; el coordinador se comprueba por separado."
            };
        }
        catch
        {
            NetworkStatusLabel.Text = "No se pudo consultar el estado de la red.";
        }
    }

    private async Task RefreshSyncStatusAsync(string? syncError = null)
    {
        try
        {
            var status = await _operationSyncService.GetStatusAsync();
            var last = status.LastConfirmedAtUtc is { } date
                ? date.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture)
                : "ninguna todavía";
            SyncStatusLabel.Text = $"Pendientes de enviar: {status.PendingCount}. Conflictos por revisar: {status.ConflictCount}. Última confirmación: {last}." +
                (status.LatestConflictMessage is null ? string.Empty : $"\nDetalle: {status.LatestConflictMessage}") +
                (_coordinatedSignInService.LastSignInStatus is { Length: > 0 } signInStatus ? $"\nEstado del acceso central: {signInStatus}" : string.Empty) +
                (syncError is null ? string.Empty : $"\n{syncError}");
        }
        catch
        {
            SyncStatusLabel.Text = syncError is null
                ? "No se pudo consultar la cola. Los cambios locales se conservan."
                : $"{syncError}\nNo se pudo consultar el estado de la cola.";
        }
    }

    private async Task RefreshLocalAccountStatusAsync()
    {
        LocalAccountAlert.IsVisible = false;
        var user = _authenticationService.CurrentUser;
        if (user is null) return;

        try
        {
            var link = await _linkStorage.ReadAsync();
            if (link?.IsConfirmed != true) return;
            if (await _authenticationService.GetCoordinatorUserIdAsync(user.Id.Value) is not null) return;

            LocalAccountStatusLabel.Text =
                $"La cuenta «{user.UserName}» solo existe en este dispositivo y no está registrada en el coordinador. " +
                "Los cambios que haga esta cuenta no se envían ni se reciben. " +
                "Cierra sesión y entra con un usuario creado en Windows (Sincronización → Cuentas y roles).";
            LocalAccountAlert.IsVisible = true;
        }
        catch
        {
            // Si no se puede leer el vínculo, no se muestra una falsa alarma.
        }
    }

    private async Task RefreshShortageStatusAsync()
    {
        if (!_isAdministrator)
        {
            ShortageAlert.IsVisible = false;
            return;
        }

        try
        {
            var summary = await _discrepancyService.GetSummaryAsync();
            if (summary.OpenCount == 0)
            {
                ShortageAlert.IsVisible = false;
                return;
            }

            var units = (summary.TotalShortageMilliUnits / 1000m)
                .ToString("0.###", System.Globalization.CultureInfo.CurrentCulture);
            ShortageStatusLabel.Text = summary.OpenCount == 1
                ? $"Hay 1 venta con existencia insuficiente ({units} unidades en total). Revísala para conciliar el inventario."
                : $"Hay {summary.OpenCount} ventas con existencia insuficiente ({units} unidades en total). Revísalas para conciliar el inventario.";
            ShortageAlert.IsVisible = true;
        }
        catch
        {
            ShortageStatusLabel.Text = "No se pudieron consultar los faltantes de existencia.";
            ShortageAlert.IsVisible = true;
        }
    }

    private string GetRealtimeStatusMessage(CoordinatorRealtimeState state)
    {
#if WINDOWS
        return _desktopMode.IsHost
            ? "Modo coordinador: esta instalación comparte la base central; los avisos SignalR no aplican aquí."
            : GetRealtimeClientMessage(state);
#else
        return GetRealtimeClientMessage(state);
#endif
    }

    private static string GetRealtimeClientMessage(CoordinatorRealtimeState state) => state switch
        {
            CoordinatorRealtimeState.Connected => "Actualización en vivo conectada al coordinador.",
            CoordinatorRealtimeState.Connecting => "Conectando avisos en vivo al coordinador…",
            CoordinatorRealtimeState.Reconnecting => "Avisos en vivo desconectados; intentando reconectar…",
            CoordinatorRealtimeState.Disconnected => "Avisos en vivo no disponibles; puedes sincronizar manualmente.",
            CoordinatorRealtimeState.SessionUnavailable => "La sesión central no está disponible. Inicia sesión de nuevo para recibir avisos en vivo.",
            _ => "Avisos en vivo disponibles al vincular el dispositivo e iniciar sesión con el coordinador."
        };

    private async Task RefreshDeviceListAsync()
    {
        if (!await _deviceRefreshGate.WaitAsync(0)) return;
        try
        {
            DeviceListStatusLabel.Text = "Consultando dispositivos…";
            DeviceRows.Children.Clear();
            var result = await _deviceDirectory.GetDevicesAsync();
            if (!result.Succeeded)
            {
                DeviceListStatusLabel.Text = result.ErrorMessage ?? "No se pudo consultar la lista.";
                return;
            }
            DeviceRows.Children.Clear();
            if (result.Devices.Count == 0)
            {
                DeviceListStatusLabel.Text = "Aún no hay dispositivos vinculados.";
                return;
            }

            DeviceListStatusLabel.Text = $"{result.Devices.Count} dispositivo(s) vinculado(s). En línea y sincronizado son estados independientes.";
            foreach (var device in result.Devices)
            {
            var shortId = device.DeviceId.ToString("N")[..6].ToUpperInvariant();
            var name = string.IsNullOrWhiteSpace(device.DeviceName) ? "Dispositivo" : device.DeviceName;
            var online = device.IsOnline ? "En línea" : "Desconectado";
            var sync = device.PendingConflictCount > 0
                ? $"{device.PendingConflictCount} conflicto(s) pendiente(s)"
                : device.IsSynchronized ? "Sincronizado" : device.LastSyncAtUtc is null ? "Aún no ha sincronizado" : "Pendiente de sincronizar";
            var date = device.LastSeenAtUtc is { } seen
                ? $"Última conexión: {seen.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture)}"
                : $"Vinculado: {device.AuthorizedAtUtc.ToLocalTime().ToString("d", System.Globalization.CultureInfo.CurrentCulture)}";
            var account = string.IsNullOrWhiteSpace(device.UserName) ? "Sin sesión identificada" : $"{device.UserName} · {device.Role}";
            var stateColor = device.PendingConflictCount > 0 ? Color.FromArgb("#B54708") : device.IsSynchronized ? Color.FromArgb("#008060") : Color.FromArgb("#61738A");
            var row = new Border
            {
                Padding = new Thickness(12),
                BackgroundColor = Color.FromArgb("#F7F9FB"),
                Stroke = Color.FromArgb("#E3EAF2"),
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
                Content = new VerticalStackLayout
                {
                    Spacing = 3,
                    Children =
                    {
                        new Label { Text = $"{name} · {shortId}", FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#111D31") },
                        new Label { Text = $"{online}  ·  {sync}", TextColor = stateColor },
                        new Label { Text = $"{account}  ·  {date}", FontSize = 12, TextColor = Color.FromArgb("#61738A") }
                    }
                }
            };
                DeviceRows.Children.Add(row);
            }
        }
        finally
        {
            _deviceRefreshGate.Release();
        }
    }
}
