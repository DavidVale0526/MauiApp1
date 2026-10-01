namespace MauiApp1.Infrastructure.Persistence.Entities;

internal sealed class DeviceSyncConflictEntity
{
    public Guid ConflictId { get; set; }
    public long CoordinatorSequence { get; set; }
    public Guid OperationId { get; set; }
    public string OperationType { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public bool IsResolved { get; set; }
}
