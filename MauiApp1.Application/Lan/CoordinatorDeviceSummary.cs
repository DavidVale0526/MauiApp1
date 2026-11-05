namespace MauiApp1.Application.Lan;

/// <summary>
/// Dispositivo autorizado en el coordinador. La lista es de solo consulta:
/// muestra quién lo usa, si está en línea y cuándo se conectó o sincronizó.
/// </summary>
public sealed record CoordinatorDeviceSummary(
    Guid DeviceId,
    string? DeviceName,
    string? UserName,
    string? Role,
    bool IsOnline,
    DateTime AuthorizedAtUtc,
    DateTime? LastSeenAtUtc,
    DateTime? LastSyncAtUtc,
    long LastProcessedSequence = 0,
    long LatestSequence = 0,
    int PendingConflictCount = 0)
{
    public bool IsSynchronized => LastSyncAtUtc is not null &&
                                  LastProcessedSequence >= LatestSequence &&
                                  PendingConflictCount == 0;
}

public sealed record CoordinatorDeviceListResult(
    bool Succeeded,
    IReadOnlyList<CoordinatorDeviceSummary> Devices,
    string? ErrorMessage)
{
    public static CoordinatorDeviceListResult Success(IReadOnlyList<CoordinatorDeviceSummary> devices) =>
        new(true, devices, null);

    public static CoordinatorDeviceListResult Failure(string errorMessage) =>
        new(false, Array.Empty<CoordinatorDeviceSummary>(), errorMessage);
}

/// <summary>Consulta de dispositivos contra el coordinador central.</summary>
public interface ICoordinatorDeviceClient
{
    Task<CoordinatorDeviceListResult> GetDevicesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Consulta de dispositivos según el modo del equipo: la PC coordinadora lee
/// la base central; un cliente vinculado usa la API del coordinador.
/// </summary>
public interface ICoordinatorDeviceDirectory
{
    Task<CoordinatorDeviceListResult> GetDevicesAsync(CancellationToken cancellationToken = default);
}
