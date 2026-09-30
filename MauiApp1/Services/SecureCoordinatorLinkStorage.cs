using System.Text.Json;
using MauiApp1.Application.Lan;

namespace MauiApp1.Services;

public sealed class SecureCoordinatorLinkStorage : ICoordinatorLinkStorage
{
    private const string StorageKey = "coordinator-device-link-v1";
    public async Task<CoordinatorDeviceLink?> ReadAsync()
    {
        var json = await SecureStorage.Default.GetAsync(StorageKey);
        if (json is null) return null;
        return JsonSerializer.Deserialize<CoordinatorDeviceLink>(json) ??
            throw new InvalidDataException("La autorización guardada no es válida.");
    }

    public Task SaveAsync(CoordinatorDeviceLink link) =>
        SecureStorage.Default.SetAsync(StorageKey, JsonSerializer.Serialize(link));

    public Task RemoveAsync()
    {
        SecureStorage.Default.Remove(StorageKey);
        return Task.CompletedTask;
    }
}
