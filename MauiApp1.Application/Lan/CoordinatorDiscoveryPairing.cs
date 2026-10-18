namespace MauiApp1.Application.Lan;

public interface ICoordinatorDiscoveryPairingClient
{
    Task<DiscoveryPairingStartResult> StartAsync(
        CoordinatorCandidate candidate,
        string deviceName,
        CancellationToken cancellationToken = default);
    Task<DiscoveryPairingStatusResult> GetStatusAsync(Guid requestId, CancellationToken cancellationToken = default);
    Task<PairingActionResult> CompleteAsync(Guid requestId, CancellationToken cancellationToken = default);
    Task<PairingActionResult> CancelAsync(Guid requestId, CancellationToken cancellationToken = default);
}

public interface ICoordinatorDiscoveryApprovalClient
{
    Task<CoordinatorAdminSessionResult> CreateAdminSessionAsync(
        string address,
        string fingerprint,
        string userName,
        string password,
        CancellationToken cancellationToken = default);
    Task<CoordinatorPendingPairingsResult> GetPendingAsync(
        string address,
        string fingerprint,
        string adminToken,
        CancellationToken cancellationToken = default);
    Task<PairingActionResult> ApproveAsync(
        string address,
        string fingerprint,
        string adminToken,
        Guid requestId,
        CancellationToken cancellationToken = default);
    Task<PairingActionResult> RejectAsync(
        string address,
        string fingerprint,
        string adminToken,
        Guid requestId,
        CancellationToken cancellationToken = default);
}

public enum DiscoveryPairingStatus
{
    WaitingForApproval,
    Approved,
    Rejected,
    Expired,
    Completed
}

public sealed class DiscoveryPairingStartResult(bool succeeded, string message, Guid? requestId = null,
    string? verificationCode = null, DateTime? expiresAtUtc = null)
{
    public bool Succeeded { get; } = succeeded;
    public string Message { get; } = message;
    public Guid? RequestId { get; } = requestId;
    public string? VerificationCode { get; } = verificationCode;
    public DateTime? ExpiresAtUtc { get; } = expiresAtUtc;
    public override string ToString() => $"{nameof(DiscoveryPairingStartResult)} {{ Succeeded = {Succeeded}, RequestId = {RequestId}, ExpiresAtUtc = {ExpiresAtUtc} }}";
}

public sealed class DiscoveryPairingStatusResult(bool succeeded, DiscoveryPairingStatus? status, string? message = null)
{
    public bool Succeeded { get; } = succeeded;
    public DiscoveryPairingStatus? Status { get; } = status;
    public string? Message { get; } = message;
}

public sealed class CoordinatorAdminSessionResult(bool succeeded, string message, string? token = null, DateTime? expiresAtUtc = null)
{
    public bool Succeeded { get; } = succeeded;
    public string Message { get; } = message;
    public string? Token { get; } = token;
    public DateTime? ExpiresAtUtc { get; } = expiresAtUtc;
    public override string ToString() => $"{nameof(CoordinatorAdminSessionResult)} {{ Succeeded = {Succeeded}, ExpiresAtUtc = {ExpiresAtUtc} }}";
}

public sealed class CoordinatorPendingPairing(Guid requestId, Guid deviceId, string deviceName,
    string verificationCode, DateTime expiresAtUtc)
{
    public Guid RequestId { get; } = requestId;
    public Guid DeviceId { get; } = deviceId;
    public string DeviceName { get; } = deviceName;
    public string VerificationCode { get; } = verificationCode;
    public DateTime ExpiresAtUtc { get; } = expiresAtUtc;
    public override string ToString() => $"{nameof(CoordinatorPendingPairing)} {{ RequestId = {RequestId}, DeviceId = {DeviceId}, DeviceName = {DeviceName}, ExpiresAtUtc = {ExpiresAtUtc} }}";
}

public sealed class CoordinatorPendingPairingsResult(bool succeeded, IReadOnlyList<CoordinatorPendingPairing> pairings,
    string? message = null)
{
    public bool Succeeded { get; } = succeeded;
    public IReadOnlyList<CoordinatorPendingPairing> Pairings { get; } = pairings;
    public string? Message { get; } = message;
}
