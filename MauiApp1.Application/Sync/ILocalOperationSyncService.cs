namespace MauiApp1.Application.Sync;

public sealed record LocalOperationSyncStatus(int PendingCount, int ConflictCount, DateTime? LastConfirmedAtUtc, string? LatestConflictMessage);

public interface ILocalOperationSyncService
{
    Task<LocalOperationSyncStatus> SynchronizeAsync(CancellationToken cancellationToken = default);
    Task<LocalOperationSyncStatus> GetStatusAsync(CancellationToken cancellationToken = default);
}
