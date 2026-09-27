namespace MauiApp1.Application.Storage;

public interface ILocalDeviceIdentity
{
    Task<Guid> GetDeviceIdAsync(CancellationToken cancellationToken = default);
}
