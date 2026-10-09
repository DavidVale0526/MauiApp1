using MauiApp1.Application.Authentication;
using MauiApp1.Application.Sync;
using MauiApp1.Infrastructure.Persistence;
using MauiApp1.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MauiApp1.Infrastructure.Sync;

/// <summary>Drains durable local operations while the app is in the foreground.</summary>
public sealed class LocalOperationSyncWorker(
    IDbContextFactory<LocalDbContext> contextFactory,
    IAuthenticationService authentication,
    ILocalOperationSyncService sync,
    ICoordinatorRealtimeClient realtime,
    ILogger<LocalOperationSyncWorker> logger) : IAsyncDisposable
{
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private CancellationTokenSource? _cancellation;
    private Task? _loop;
    private long _authenticationRevision;

    public async Task StartAsync()
    {
        await _lifecycle.WaitAsync();
        try
        {
            if (_cancellation is not null) return;
            authentication.AuthenticationStateChanged += OnAuthenticationChanged;
            _cancellation = new CancellationTokenSource();
            var token = _cancellation.Token;
            _loop = Task.Run(() => RunAsync(token));
        }
        finally { _lifecycle.Release(); }
    }

    public async Task StopAsync()
    {
        await _lifecycle.WaitAsync();
        try
        {
            authentication.AuthenticationStateChanged -= OnAuthenticationChanged;
            if (_cancellation is not null)
            {
                _cancellation.Cancel();
                if (_loop is not null) await _loop;
                _cancellation.Dispose();
                _cancellation = null;
                _loop = null;
            }
            await realtime.StopAsync();
        }
        finally { _lifecycle.Release(); }
    }

    private void OnAuthenticationChanged(object? sender, EventArgs args) =>
        Interlocked.Increment(ref _authenticationRevision);

    private async Task RunAsync(CancellationToken ct)
    {
        var observedRevision = -1L;
        var nextRecovery = DateTime.MinValue;
        var nextSend = DateTime.MinValue;
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        try
        {
            do
            {
                try
                {
                    var revision = Interlocked.Read(ref _authenticationRevision);
                    if (observedRevision != revision)
                    {
                        await realtime.StopAsync(ct);
                        observedRevision = revision;
                        nextRecovery = nextSend = DateTime.MinValue;
                    }
                    if (authentication.CurrentUser is null) continue;

                    // Retry startup even when the first page appeared before SecureStorage
                    // finished saving the central session. SignalR remains the live receiver.
                    await realtime.StartAsync(ct);
                    await using var context = await contextFactory.CreateDbContextAsync(ct);
                    var now = DateTime.UtcNow;
                    var hasPending = now >= nextSend && await context.OutboxMessages.AsNoTracking()
                        .AnyAsync(item => item.Status == OutboxStatus.Pending, ct);
                    if (!hasPending && now < nextRecovery) continue;

                    var status = await sync.SynchronizeAsync(ct);
                    nextRecovery = DateTime.UtcNow.AddSeconds(15);
                    nextSend = status.PendingCount > 0 ? DateTime.UtcNow.AddSeconds(5) : DateTime.MinValue;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "La sincronización automática se reintentará; las operaciones locales se conservan.");
                    nextRecovery = nextSend = DateTime.UtcNow.AddSeconds(5);
                }
            } while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _lifecycle.Dispose();
    }
}
