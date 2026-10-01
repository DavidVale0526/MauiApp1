using MauiApp1.Application.Storage;

namespace MauiApp1.Application.Sync;

public interface ICoordinatorOperationSender
{
    /// <summary>
    /// Sends one persisted local operation using the current central user session.
    /// Implementations must not treat ActorUserId from the local operation as server authority.
    /// </summary>
    Task<CoordinatorOperationSendResult> SendAsync(
        OutboxOperation operation,
        CancellationToken cancellationToken = default);
}

public sealed record CoordinatorOperationSendResult(
    CoordinatorOperationSendStatus Status,
    long? Sequence = null,
    string? ConflictMessage = null);

public enum CoordinatorOperationSendStatus
{
    Accepted,
    AlreadyAccepted,
    NotLinked,
    SessionUnavailable,
    IdentityMismatch,
    Unauthorized,
    Conflict,
    Rejected,
    Unavailable
}
