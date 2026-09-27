namespace MauiApp1.Infrastructure.Persistence.Entities;

internal enum OutboxStatus
{
    Pending = 0,
    Confirmed = 1
}

internal sealed class OutboxMessageEntity
{
    public Guid OperationId { get; set; }

    public Guid DeviceId { get; set; }

    public Guid ActorUserId { get; set; }

    public string OperationType { get; set; } = string.Empty;

    public string PayloadJson { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public OutboxStatus Status { get; set; }

    public int AttemptCount { get; set; }

    public DateTime? LastAttemptAtUtc { get; set; }

    public DateTime? ConfirmedAtUtc { get; set; }
}
