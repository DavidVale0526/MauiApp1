namespace MauiApp1.Infrastructure.Persistence.Entities;

internal sealed class DeviceSyncStateEntity
{
    public Guid DeviceId { get; set; }
    public long LastSequence { get; set; }
    public DateTime? LastSuccessfulSyncAtUtc { get; set; }
}
