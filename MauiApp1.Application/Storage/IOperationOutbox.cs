namespace MauiApp1.Application.Storage;

public sealed record NewOutboxOperation(
    Guid OperationId,
    Guid ActorUserId,
    string OperationType,
    string PayloadJson);

public sealed record OutboxOperation(
    Guid OperationId,
    Guid DeviceId,
    Guid ActorUserId,
    string OperationType,
    string PayloadJson,
    DateTime CreatedAtUtc,
    int AttemptCount);

public interface IOperationOutbox
{
    /// <summary>Stages an operation; commit it with the business changes through ILocalUnitOfWork.</summary>
    Task EnqueueAsync(NewOutboxOperation operation, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OutboxOperation>> GetPendingAsync(
        int maximumCount,
        CancellationToken cancellationToken = default,
        Guid? actorUserId = null);

    Task RecordAttemptAsync(Guid operationId, CancellationToken cancellationToken = default);

    /// <summary>Call only after the coordinator has durably accepted this operation.</summary>
    Task MarkConfirmedAsync(
        Guid operationId,
        DateTime confirmedAtUtc,
        CancellationToken cancellationToken = default);

    Task MarkConflictAsync(Guid operationId, string message, CancellationToken cancellationToken = default);
}
