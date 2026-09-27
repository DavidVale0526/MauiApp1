namespace MauiApp1.Infrastructure.Persistence.Entities;

internal sealed class DeviceIdentityEntity
{
    public const int SingletonKeyValue = 1;

    public int SingletonKey { get; set; } = SingletonKeyValue;

    public Guid DeviceId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
