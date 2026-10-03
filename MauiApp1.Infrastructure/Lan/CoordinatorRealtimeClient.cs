using MauiApp1.Application.Authentication;
using MauiApp1.Application.Lan;
using MauiApp1.Application.Sync;
using MauiApp1.Infrastructure.Authentication;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using System.Net;

namespace MauiApp1.Infrastructure.Lan;

public sealed class CoordinatorRealtimeClient(
    ICoordinatorLinkStorage linkStorage,
    ICoordinatorUserSessionStorage sessionStorage,
    IAuthenticationService authenticationService,
    ILocalOperationSyncService operationSyncService) : ICoordinatorRealtimeClient, IAsyncDisposable
{
    private readonly object _lifecycleLock = new();
    private readonly SemaphoreSlim _syncGate = new(1, 1);
    private HubConnection? _connection;
    private CancellationTokenSource? _connectionLoopCancellation;
    private Task? _connectionLoop;
    private CoordinatorRealtimeState _state = CoordinatorRealtimeState.NotConfigured;

    public CoordinatorRealtimeState State => _state;
    public event Action<CoordinatorRealtimeState>? StateChanged;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
#if WINDOWS
        SetState(CoordinatorRealtimeState.NotConfigured);
        return Task.CompletedTask;
#else
        if (authenticationService.CurrentUser is null)
        {
            SetState(CoordinatorRealtimeState.NotConfigured);
            return Task.CompletedTask;
        }

        lock (_lifecycleLock)
        {
            if (_connectionLoopCancellation is not null) return Task.CompletedTask;
            _connectionLoopCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _connectionLoop = ConnectUntilConnectedAsync(_connectionLoopCancellation.Token);
        }
        return Task.CompletedTask;
#endif
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource? loopCancellation;
        Task? loop;
        HubConnection? connection;
        lock (_lifecycleLock)
        {
            loopCancellation = _connectionLoopCancellation;
            loop = _connectionLoop;
            connection = _connection;
            _connectionLoopCancellation = null;
            _connectionLoop = null;
            _connection = null;
        }

        if (loopCancellation is null && connection is null)
        {
            SetState(CoordinatorRealtimeState.NotConfigured);
            return;
        }

        loopCancellation?.Cancel();
        if (connection is not null)
        {
            try { await connection.StopAsync(cancellationToken); } catch { }
            await connection.DisposeAsync();
        }
        if (loop is not null)
        {
            try { await loop.WaitAsync(cancellationToken); }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
        }
        loopCancellation?.Dispose();
        SetState(CoordinatorRealtimeState.NotConfigured);
    }

    private async Task ConnectUntilConnectedAsync(CancellationToken cancellationToken)
    {
        var retry = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_connection is null)
                {
                    var connection = await CreateConnectionAsync(cancellationToken);
                    if (connection is null)
                    {
                        SetState(CoordinatorRealtimeState.NotConfigured);
                        ClearConnectionLoop(cancellationToken);
                        return;
                    }

                    lock (_lifecycleLock)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            _ = connection.DisposeAsync();
                            return;
                        }
                        _connection = connection;
                    }
                }

                SetState(CoordinatorRealtimeState.Connecting);
                await _connection!.StartAsync(cancellationToken);
                SetState(CoordinatorRealtimeState.Connected);
                await SynchronizeAfterConnectionAsync(cancellationToken);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (GetStatusCode(exception) is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                SetState(CoordinatorRealtimeState.SessionUnavailable);
                ClearConnectionLoop(cancellationToken);
                return;
            }
            catch
            {
                SetState(CoordinatorRealtimeState.Disconnected);
                var seconds = Math.Min(30, Math.Pow(2, Math.Min(retry, 5)));
                retry++;
                try { await Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
            }
        }
    }

    private async Task<HubConnection?> CreateConnectionAsync(CancellationToken cancellationToken)
    {
        var currentUser = authenticationService.CurrentUser;
        var link = await linkStorage.ReadAsync();
        var session = await sessionStorage.ReadAsync();
        if (currentUser is null || link is null || !link.IsConfirmed || session is null ||
            session.ExpiresAtUtc <= DateTime.UtcNow || session.LocalUserId != currentUser.Id.Value ||
            !CoordinatorPairingClient.TryValidateStoredConnection(
                link.Address, link.CertificateFingerprint, out _, out var fingerprint))
            return null;

        var hubAddress = new Uri(new Uri(link.Address.TrimEnd('/') + "/", UriKind.Absolute), "hubs/changes");
        var connection = new HubConnectionBuilder()
            .WithUrl(hubAddress, options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.Headers.Add("Authorization", $"Bearer {link.DeviceToken}");
                options.Headers.Add("X-Device-Id", link.DeviceId.ToString("D"));
                options.Headers.Add("X-User-Session", session.SessionToken);
                options.HttpMessageHandlerFactory = _ => CoordinatorPairingClient.CreatePinnedHandler(fingerprint);
            })
            .WithAutomaticReconnect(new ExponentialReconnectPolicy())
            .Build();

        connection.On<CoordinatorChangeNotice>("ChangeAvailable", notice => ApplyCoordinatorChangesAsync(notice, cancellationToken));
        connection.On("AuthorizationRevoked", () => StopForRevokedSessionAsync());
        connection.Reconnecting += _ =>
        {
            SetState(CoordinatorRealtimeState.Reconnecting);
            return Task.CompletedTask;
        };
        connection.Reconnected += async _ =>
        {
            SetState(CoordinatorRealtimeState.Connected);
            await SynchronizeAfterConnectionAsync(cancellationToken);
        };
        connection.Closed += _ =>
        {
            SetState(CoordinatorRealtimeState.Disconnected);
            return Task.CompletedTask;
        };
        return connection;
    }

    private void ClearConnectionLoop(CancellationToken cancellationToken)
    {
        lock (_lifecycleLock)
        {
            if (_connectionLoopCancellation?.Token != cancellationToken) return;
            _connectionLoopCancellation.Dispose();
            _connectionLoopCancellation = null;
            _connectionLoop = null;
        }
    }

    private async Task ApplyCoordinatorChangesAsync(CoordinatorChangeNotice notice, CancellationToken cancellationToken)
    {
        if (notice.Sequence <= 0 || notice.OperationId == Guid.Empty) return;
        try { await _syncGate.WaitAsync(cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }

        try
        {
            await operationSyncService.SynchronizeAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch { }
        finally
        {
            _syncGate.Release();
        }
    }

    private static HttpStatusCode? GetStatusCode(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is HttpRequestException { StatusCode: { } statusCode }) return statusCode;
        return null;
    }

    private async Task SynchronizeAfterConnectionAsync(CancellationToken cancellationToken)
    {
        await _syncGate.WaitAsync(cancellationToken);
        try { await operationSyncService.SynchronizeAsync(cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch { }
        finally { _syncGate.Release(); }
    }

    private async Task StopForRevokedSessionAsync()
    {
        SetState(CoordinatorRealtimeState.SessionUnavailable);
        await StopAsync();
        SetState(CoordinatorRealtimeState.SessionUnavailable);
    }

    private void SetState(CoordinatorRealtimeState state)
    {
        if (_state == state) return;
        _state = state;
        StateChanged?.Invoke(state);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _syncGate.Dispose();
    }

    private sealed class ExponentialReconnectPolicy : IRetryPolicy
    {
        public TimeSpan? NextRetryDelay(RetryContext retryContext)
        {
            var seconds = Math.Min(30, Math.Pow(2, Math.Min(retryContext.PreviousRetryCount, 5)));
            return TimeSpan.FromSeconds(seconds);
        }
    }
}
