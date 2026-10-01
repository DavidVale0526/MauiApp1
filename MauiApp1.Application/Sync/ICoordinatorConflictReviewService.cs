namespace MauiApp1.Application.Sync;

public interface ICoordinatorConflictReviewService
{
    Task<IReadOnlyList<CoordinatorConflictItem>> GetOpenAsync(CancellationToken cancellationToken = default);
    Task<string?> ResolveAsync(Guid operationId, bool applyIncoming, CancellationToken cancellationToken = default);
}
