using MauiApp1.Application.Authentication;
using MauiApp1.Application.Lan;
using MauiApp1.Application.Sync;
using MauiApp1.Domain.Users;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Networking;

namespace MauiApp1.Presentation.Views;

public partial class SyncManagementPage : CoordinatorRefreshPage
{
    private readonly ICoordinatorPairingClient _coordinatorClient;
    private readonly IConnectivity _connectivity;
    private readonly ILocalOperationSyncService _operationSyncService;
    private readonly ICoordinatedSignInService _coordinatedSignInService;
    private readonly ICoordinatorRealtimeClient _realtimeClient;
    private readonly bool _isAdministrator;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    public SyncManagementPage(
        ICoordinatorPairingClient coordinatorClient,
        IConnectivity connectivity,
        ILocalOperationSyncService operationSyncService,
        ICoordinatedSignInService coordinatedSignInService,
        ICoordinatorRealtimeClient realtimeClient,
        IAuthenticationService authenticationService,
        ILocalDataChangeNotifier changeNotifier) : base(changeNotifier)
    {
        InitializeComponent();
        _coordinatorClient = coordinatorClient;
        _connectivity = connectivity;
        _operationSyncService = operationSyncService;
        _coordinatedSignInService = coordinatedSignInService;
        _realtimeClient = realtimeClient;
        _isAdministrator = authenticationService.CurrentUser?.Role == UserRole.Administrator;
        ReviewConflictsButton.IsVisible = _isAdministrator;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _connectivity.ConnectivityChanged += OnConnectivityChanged;
        _realtimeClient.StateChanged += OnRealtimeStateChanged;
        await _realtimeClient.StartAsync();
        await RefreshStatusAsync();
    }

    protected override void OnDisappearing()
    {
        _connectivity.ConnectivityChanged -= OnConnectivityChanged;
        _realtimeClient.StateChanged -= OnRealtimeStateChanged;
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

    private async void OnDevicePairingClicked(object? sender, EventArgs args)
    {
        if (Handler?.MauiContext?.Services.GetRequiredService<DevicePairingPage>() is not { } page) return;
        await Navigation.PushModalAsync(new NavigationPage(page));
    }

    private async void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs args) =>
        await RefreshStatusAsync();

    private void OnRealtimeStateChanged(CoordinatorRealtimeState state) =>
        MainThread.BeginInvokeOnMainThread(() => RealtimeStatusLabel.Text = GetRealtimeStatusMessage(state));

    private async Task RefreshStatusAsync(string? syncError = null)
    {
        await _refreshGate.WaitAsync();
        try
        {
            await RefreshCoordinatorStatusAsync();
            RefreshNetworkStatus();
            RealtimeStatusLabel.Text = GetRealtimeStatusMessage(_realtimeClient.State);
            await RefreshSyncStatusAsync(syncError);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task RefreshCoordinatorStatusAsync()
    {
        CoordinatorStatusLabel.Text = "Consultando coordinador…";
        try
        {
            var status = await _coordinatorClient.CheckAvailabilityAsync();
#if WINDOWS
            CoordinatorStatusLabel.Text = status.IsAvailable
                ? "Coordinador disponible en esta PC. Este equipo utiliza la base central."
                : status.IsConfigured
                    ? "El coordinador no responde en la red local. El trabajo guardado se conserva para reintentar."
                    : "El coordinador no está configurado o no responde en esta PC.";
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

    private static string GetRealtimeStatusMessage(CoordinatorRealtimeState state)
    {
#if WINDOWS
        return "Actualización en vivo lista; esta PC comparte la base central del coordinador.";
#else
        return state switch
        {
            CoordinatorRealtimeState.Connected => "Actualización en vivo conectada al coordinador.",
            CoordinatorRealtimeState.Connecting => "Conectando avisos en vivo al coordinador…",
            CoordinatorRealtimeState.Reconnecting => "Avisos en vivo desconectados; intentando reconectar…",
            CoordinatorRealtimeState.Disconnected => "Avisos en vivo no disponibles; puedes sincronizar manualmente.",
            CoordinatorRealtimeState.SessionUnavailable => "La sesión central no está disponible. Inicia sesión de nuevo para recibir avisos en vivo.",
            _ => "Avisos en vivo disponibles al vincular el dispositivo e iniciar sesión con el coordinador."
        };
#endif
    }
}
