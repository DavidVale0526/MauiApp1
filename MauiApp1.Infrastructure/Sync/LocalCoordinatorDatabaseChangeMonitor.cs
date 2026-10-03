using MauiApp1.Application.Sync;
using MauiApp1.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MauiApp1.Infrastructure.Sync;

public sealed class LocalCoordinatorDatabaseChangeMonitor(
    IDbContextFactory<LocalDbContext> contextFactory,
    ILocalDataChangeNotifier changeNotifier,
    ILogger<LocalCoordinatorDatabaseChangeMonitor> logger) : ILocalCoordinatorChangeMonitor, IAsyncDisposable
{
    private readonly object _lock = new();
    private CancellationTokenSource? _cancellation;

    public void Start()
    {
        lock (_lock)
        {
            if (_cancellation is not null) return;
            _cancellation = new CancellationTokenSource();
            _ = MonitorAsync(_cancellation.Token);
        }
    }

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
            var connection = (SqliteConnection)context.Database.GetDbConnection();
            await connection.OpenAsync(cancellationToken);
            long? lastVersion = null;
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(400));

            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                try
                {
                    await using var command = connection.CreateCommand();
                    command.CommandText = "PRAGMA data_version;";
                    var version = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
                    if (lastVersion is not null && version != lastVersion.Value)
                        changeNotifier.NotifyDataChanged();
                    lastVersion = version;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception exception)
                {
                    logger.LogDebug(exception, "No se pudo revisar el diario local para detectar cambios externos.");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "La actualización automática de pantallas desde la base central local no quedó activa.");
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_lock)
        {
            _cancellation?.Cancel();
            _cancellation?.Dispose();
            _cancellation = null;
        }
        return ValueTask.CompletedTask;
    }
}
