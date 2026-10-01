using System.Text.Json;
using MauiApp1.Application.Authentication;

namespace MauiApp1.Services;

public sealed class SecureCoordinatorUserSessionStorage : ICoordinatorUserSessionStorage
{
    private const string StorageKey = "coordinator-user-session-v1";

    public async Task<CoordinatorUserSession?> ReadAsync()
    {
        var json = await SecureStorage.Default.GetAsync(StorageKey);
        if (json is null) return null;
        return JsonSerializer.Deserialize<CoordinatorUserSession>(json) ??
            throw new InvalidDataException("La sesión central guardada no es válida.");
    }

    public Task SaveAsync(CoordinatorUserSession session) =>
        SecureStorage.Default.SetAsync(StorageKey, JsonSerializer.Serialize(session));

    public Task RemoveAsync()
    {
        SecureStorage.Default.Remove(StorageKey);
        return Task.CompletedTask;
    }
}
