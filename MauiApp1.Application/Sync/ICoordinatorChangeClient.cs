namespace MauiApp1.Application.Sync;

public sealed record CoordinatorChange(
    long Sequence, Guid OperationId, Guid DeviceId, Guid ActorUserId, string? ActorUserName, int? ActorRole,
    string OperationType, string PayloadJson, DateTime CreatedAtUtc, DateTime ConfirmedAtUtc);

public sealed record CoordinatorChangesPage(IReadOnlyList<CoordinatorChange> Changes, long NextSequence);

public sealed record CoordinatorConflictItem(Guid OperationId, string OperationType, string Message,
    string CurrentSummary, string ProposedSummary, bool CanApplyIncoming, bool IsLocalConflict = false);

public interface ICoordinatorChangeClient
{
    Task<CoordinatorChangesPage?> GetChangesAsync(long afterSequence, int limit = 100, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CoordinatorConflictItem>?> GetConflictsAsync(CancellationToken cancellationToken = default);
    Task<string?> ResolveConflictAsync(Guid operationId, bool applyIncoming, CancellationToken cancellationToken = default);
}
