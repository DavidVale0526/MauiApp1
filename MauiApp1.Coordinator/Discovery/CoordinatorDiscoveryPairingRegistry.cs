using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MauiApp1.Application.Lan;
using MauiApp1.Infrastructure.Lan;

namespace MauiApp1.Coordinator.Discovery;

internal sealed class CoordinatorDiscoveryPairingRegistry
{
    private static readonly TimeSpan PairingLifetime = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan AdminSessionLifetime = TimeSpan.FromMinutes(5);
    private const int MaximumPendingRequests = 32;
    private readonly ConcurrentDictionary<Guid, PendingDiscoveryPairing> _requests = new();
    private readonly ConcurrentDictionary<string, AdminApprovalSession> _adminSessions = new(StringComparer.Ordinal);
    private readonly string? _fingerprint;
    private readonly string? _coordinatorId;

    public CoordinatorDiscoveryPairingRegistry(string? certificateFingerprint)
    {
        _fingerprint = NormalizeFingerprint(certificateFingerprint);
        _coordinatorId = _fingerprint is null
            ? null
            : Convert.ToHexString(Convert.FromHexString(_fingerprint).AsSpan(0, 16));
    }

    public bool IsEnabled => _fingerprint is not null && _coordinatorId is not null;

    public CoordinatorDiscoveryStartResponse? CreateRequest(CoordinatorDiscoveryStartRequest request)
    {
        CleanupExpired();
        if (!IsEnabled || request.DeviceId == Guid.Empty || request.Version != 1 ||
            string.IsNullOrWhiteSpace(request.ClientPublicKey) || request.ClientPublicKey.Length > 2048 ||
            string.IsNullOrWhiteSpace(request.ClientNonce) || request.ClientNonce.Length > 128 ||
            string.IsNullOrWhiteSpace(request.DeviceName) || request.DeviceName.Length > 50 ||
            request.DeviceName.Any(char.IsControl) || _requests.Count >= MaximumPendingRequests)
            return null;

        byte[] clientPublicKey;
        byte[] clientNonce;
        try
        {
            clientPublicKey = Convert.FromBase64String(request.ClientPublicKey);
            clientNonce = Convert.FromBase64String(request.ClientNonce);
        }
        catch (FormatException) { return null; }
        if (clientPublicKey.Length is < 64 or > 1024 || clientNonce.Length != 32) return null;

        try
        {
            using var serverKey = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            var serverPublicKey = serverKey.ExportSubjectPublicKeyInfo();
            var serverNonce = RandomNumberGenerator.GetBytes(32);
            var requestId = Guid.NewGuid();
            var sessionKey = CoordinatorDiscoveryPairingProtocol.DeriveSessionKey(
                serverKey, clientPublicKey, clientPublicKey, serverPublicKey, requestId, request.DeviceId,
                clientNonce, serverNonce, _coordinatorId!);
            var verificationCode = CoordinatorDiscoveryPairingProtocol.ComputeVerificationCode(sessionKey);
            var expiresAtUtc = DateTime.UtcNow.Add(PairingLifetime);
            var pending = new PendingDiscoveryPairing(requestId, request.DeviceId, request.DeviceName.Trim(),
                sessionKey, verificationCode, expiresAtUtc);
            if (!_requests.TryAdd(requestId, pending))
            {
                pending.Dispose();
                return null;
            }

            var protectedPayload = CoordinatorDiscoveryPairingProtocol.Encrypt(sessionKey, requestId,
                "start-response", JsonSerializer.SerializeToUtf8Bytes(new CoordinatorDiscoveryStartProtectedPayload(
                    _fingerprint!, expiresAtUtc)));
            return new CoordinatorDiscoveryStartResponse(requestId, _coordinatorId!,
                Convert.ToBase64String(serverPublicKey), Convert.ToBase64String(serverNonce), protectedPayload);
        }
        catch (Exception exception) when (exception is CryptographicException or ArgumentException or FormatException)
        { return null; }
    }

    public PendingDiscoveryPairing? FindRequest(Guid requestId)
    {
        CleanupExpired();
        return _requests.TryGetValue(requestId, out var request) ? request : null;
    }

    public IReadOnlyList<CoordinatorPendingPairing> GetPendingRequests()
    {
        CleanupExpired();
        return _requests.Values
            .Where(request => request.GetStatus() == DiscoveryPairingStatus.WaitingForApproval)
            .OrderBy(request => request.ExpiresAtUtc)
            .Select(request => new CoordinatorPendingPairing(request.RequestId, request.DeviceId,
                request.DeviceName, request.VerificationCode, request.ExpiresAtUtc))
            .ToArray();
    }

    public bool Approve(Guid requestId)
    {
        var request = FindRequest(requestId);
        return request?.Approve() == true;
    }

    public bool Reject(Guid requestId)
    {
        var request = FindRequest(requestId);
        return request?.Reject() == true;
    }

    public bool Cancel(Guid requestId)
    {
        var request = FindRequest(requestId);
        return request?.MarkCanceled() == true;
    }

    public string CreateAdminSession(Guid administratorUserId)
    {
        CleanupExpired();
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        _adminSessions[tokenHash] = new AdminApprovalSession(administratorUserId, DateTime.UtcNow.Add(AdminSessionLifetime));
        return token;
    }

    public DateTime GetAdminSessionExpiry(string token)
    {
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        return _adminSessions.TryGetValue(tokenHash, out var session) ? session.ExpiresAtUtc : DateTime.MinValue;
    }

    public Guid? ValidateAdminSession(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length != 43 ||
            token.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
            return null;
        CleanupExpired();
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        return _adminSessions.TryGetValue(tokenHash, out var session) && session.ExpiresAtUtc > DateTime.UtcNow
            ? session.AdministratorUserId
            : null;
    }

    private void CleanupExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var (id, request) in _requests)
        {
            if (request.ExpiresAtUtc <= now && !request.IsCompletionInProgress && _requests.TryRemove(id, out var removed))
                removed.Dispose();
        }
        foreach (var (hash, session) in _adminSessions)
        {
            if (session.ExpiresAtUtc <= now)
                _adminSessions.TryRemove(hash, out _);
        }
    }

    private static string? NormalizeFingerprint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = new string(value.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
        return normalized.Length == 64 ? normalized : null;
    }

    internal sealed class PendingDiscoveryPairing(
        Guid requestId,
        Guid deviceId,
        string deviceName,
        byte[] sessionKey,
        string verificationCode,
        DateTime expiresAtUtc) : IDisposable
    {
        private readonly object _gate = new();
        private DiscoveryPairingStatus _status = DiscoveryPairingStatus.WaitingForApproval;
        private bool _completionInProgress;

        public Guid RequestId { get; } = requestId;
        public Guid DeviceId { get; } = deviceId;
        public string DeviceName { get; } = deviceName;
        public string VerificationCode { get; } = verificationCode;
        public DateTime ExpiresAtUtc { get; } = expiresAtUtc;
        public byte[] SessionKey { get; } = sessionKey;
        public bool IsCompletionInProgress
        {
            get { lock (_gate) return _completionInProgress; }
        }

        public DiscoveryPairingStatus GetStatus()
        {
            lock (_gate)
                return ExpiresAtUtc <= DateTime.UtcNow && _status is not DiscoveryPairingStatus.Completed
                    ? DiscoveryPairingStatus.Expired
                    : _status;
        }

        public bool Approve()
        {
            lock (_gate)
            {
                if (_status != DiscoveryPairingStatus.WaitingForApproval || ExpiresAtUtc <= DateTime.UtcNow) return false;
                _status = DiscoveryPairingStatus.Approved;
                return true;
            }
        }

        public bool Reject()
        {
            lock (_gate)
            {
                if (_status != DiscoveryPairingStatus.WaitingForApproval || ExpiresAtUtc <= DateTime.UtcNow) return false;
                _status = DiscoveryPairingStatus.Rejected;
                return true;
            }
        }

        public bool MarkCompleted()
        {
            lock (_gate)
            {
                if (_status == DiscoveryPairingStatus.Completed) return true;
                if (_status != DiscoveryPairingStatus.Approved || !_completionInProgress) return false;
                _status = DiscoveryPairingStatus.Completed;
                _completionInProgress = false;
                return true;
            }
        }

        public bool TryBeginCompletion()
        {
            lock (_gate)
            {
                if (_status == DiscoveryPairingStatus.Completed) return true;
                if (_status != DiscoveryPairingStatus.Approved || ExpiresAtUtc <= DateTime.UtcNow) return false;
                _completionInProgress = true;
                return true;
            }
        }

        public void MarkCompletionFailed()
        {
            lock (_gate)
                if (_status == DiscoveryPairingStatus.Approved) _completionInProgress = false;
        }

        public bool MarkCanceled()
        {
            lock (_gate)
            {
                if (_status is not (DiscoveryPairingStatus.WaitingForApproval or DiscoveryPairingStatus.Approved) ||
                    _completionInProgress ||
                    ExpiresAtUtc <= DateTime.UtcNow)
                    return false;
                _status = DiscoveryPairingStatus.Rejected;
                return true;
            }
        }

        public void Dispose() => CryptographicOperations.ZeroMemory(SessionKey);
        public override string ToString() => $"{nameof(PendingDiscoveryPairing)} {{ RequestId = {RequestId}, DeviceId = {DeviceId}, DeviceName = {DeviceName}, ExpiresAtUtc = {ExpiresAtUtc} }}";
    }

    private sealed record AdminApprovalSession(Guid AdministratorUserId, DateTime ExpiresAtUtc);
}

internal sealed record CoordinatorDiscoveryStartRequest(Guid DeviceId, string ClientPublicKey,
    string ClientNonce, string DeviceName, int Version);
internal sealed record CoordinatorDiscoveryStartResponse(Guid RequestId, string CoordinatorId,
    string ServerPublicKey, string ServerNonce, CoordinatorDiscoveryEncryptedPayload ProtectedPayload);
internal sealed record CoordinatorDiscoveryStartProtectedPayload(string CertificateFingerprint,
    DateTime ExpiresAtUtc);
internal sealed record CoordinatorDiscoveryStatusRequest(Guid RequestId, CoordinatorDiscoveryEncryptedPayload ProtectedPayload);
internal sealed record CoordinatorDiscoveryStatusResponse(DiscoveryPairingStatus Status);
internal sealed record CoordinatorDiscoveryProtectedResponse(CoordinatorDiscoveryEncryptedPayload ProtectedPayload);
internal sealed record CoordinatorDiscoveryCompletionRequest(Guid RequestId, CoordinatorDiscoveryEncryptedPayload ProtectedPayload);
internal sealed record CoordinatorDiscoveryCompletionPayload(Guid DeviceId, string? DeviceToken);
internal sealed record CoordinatorDiscoveryCompletionResponse(bool Success, string Message);
internal sealed record CoordinatorDiscoveryChallengePayload(string Challenge);
internal sealed record CoordinatorDiscoveryStatusPayload(string Challenge, DiscoveryPairingStatus Status);
internal sealed record CoordinatorDiscoveryActionPayload(string Challenge, bool Success, string Message,
    DiscoveryPairingStatus Status);
internal sealed record CoordinatorDiscoveryAdminSessionRequest(string UserName, string Password);
internal sealed record CoordinatorDiscoveryAdminSessionResponse(string Token, DateTime ExpiresAtUtc);
internal sealed record CoordinatorDiscoveryAdminActionRequest(Guid RequestId);
internal sealed record CoordinatorDiscoveryAdminActionResponse(bool Success, string Message);
