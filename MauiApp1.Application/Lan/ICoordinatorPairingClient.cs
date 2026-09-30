namespace MauiApp1.Application.Lan;

public interface ICoordinatorPairingClient
{
    Task<CoordinatorLinkStatus?> GetStatusAsync();
    Task<CoordinatorAvailability> CheckAvailabilityAsync();
    Task<PairingCodeResult> CreateCodeAsync(string address, string fingerprint, string userName, string password);
    Task<PairingActionResult> PairAsync(string address, string fingerprint, string code);
    Task<PairingActionResult> UnlinkAsync();
}

public sealed record CoordinatorLinkStatus(string Address, string CertificateFingerprint, bool IsConfirmed, string? PendingCode);
public sealed record CoordinatorAvailability(bool IsConfigured, bool IsAvailable, bool IsConfirmed);
public sealed record PairingCodeResult(bool Succeeded, string Message, string? Code = null, DateTime? ExpiresAtUtc = null);
public sealed record PairingActionResult(bool Succeeded, string Message);

public interface ICoordinatorLinkStorage
{
    Task<CoordinatorDeviceLink?> ReadAsync();
    Task SaveAsync(CoordinatorDeviceLink link);
    Task RemoveAsync();
}

// A class avoids generating a record ToString that could reveal the device secret.
public sealed class CoordinatorDeviceLink
{
    public Guid DeviceId { get; init; }
    public string Address { get; init; } = string.Empty;
    public string CertificateFingerprint { get; init; } = string.Empty;
    public string DeviceToken { get; init; } = string.Empty;
    public bool IsConfirmed { get; init; }
    public string? PendingCode { get; init; }
}
