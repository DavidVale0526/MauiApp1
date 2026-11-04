using MauiApp1.Application.Lan;
using Microsoft.Extensions.Logging;

namespace MauiApp1.Infrastructure.Lan;

/// <summary>
/// Resolves the coordinator address stored in a device link and repairs it when the
/// coordinator changed IP (for example after switching Wi-Fi networks). The coordinator
/// identity is verified with the pinned certificate before the new address is stored.
/// </summary>
public sealed class CoordinatorAddressResolver(
    ICoordinatorDiscovery discovery,
    ICoordinatorLinkStorage linkStorage,
    ILogger<CoordinatorAddressResolver>? logger = null)
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan DiscoveryTimeout = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan VerifiedLifetime = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan FailureLifetime = TimeSpan.FromSeconds(20);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _verifiedAddress;
    private DateTime _verifiedAtUtc;
    private string? _failedAddress;
    private DateTime _failedAtUtc;

    public async Task<string> ResolveAsync(CoordinatorDeviceLink link, CancellationToken cancellationToken = default)
    {
        if (link is null || string.IsNullOrWhiteSpace(link.Address)) return link?.Address ?? string.Empty;
        if (!TryGetCoordinatorId(link.CertificateFingerprint, out var coordinatorId)) return link.Address;

        var now = DateTime.UtcNow;
        if (string.Equals(_verifiedAddress, link.Address, StringComparison.OrdinalIgnoreCase) &&
            now - _verifiedAtUtc < VerifiedLifetime) return link.Address;
        if (string.Equals(_failedAddress, link.Address, StringComparison.OrdinalIgnoreCase) &&
            now - _failedAtUtc < FailureLifetime) return link.Address;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            now = DateTime.UtcNow;
            if (string.Equals(_verifiedAddress, link.Address, StringComparison.OrdinalIgnoreCase) &&
                now - _verifiedAtUtc < VerifiedLifetime) return link.Address;

            if (await IsHealthyAsync(link.Address, link.CertificateFingerprint, cancellationToken).ConfigureAwait(false))
            {
                RememberVerified(link.Address);
                return link.Address;
            }

            IReadOnlyList<CoordinatorCandidate> candidates;
            try
            {
                candidates = await discovery.FindAsync(DiscoveryTimeout, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger?.LogDebug(exception, "No se pudo buscar el coordinador para actualizar su dirección.");
                RememberFailed(link.Address);
                return link.Address;
            }

            var candidate = candidates.FirstOrDefault(item =>
                item.ProtocolVersion == 1 &&
                string.Equals(item.CoordinatorId, coordinatorId, StringComparison.OrdinalIgnoreCase));
            if (candidate is null ||
                string.Equals(candidate.Address, link.Address, StringComparison.OrdinalIgnoreCase) ||
                !await IsHealthyAsync(candidate.Address, link.CertificateFingerprint, cancellationToken)
                    .ConfigureAwait(false))
            {
                RememberFailed(link.Address);
                return link.Address;
            }

            var updated = new CoordinatorDeviceLink
            {
                DeviceId = link.DeviceId,
                Address = candidate.Address,
                CertificateFingerprint = link.CertificateFingerprint,
                DeviceToken = link.DeviceToken,
                IsConfirmed = link.IsConfirmed,
                PendingCode = link.PendingCode
            };
            try
            {
                await linkStorage.SaveAsync(updated).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                // The resolved address is still usable for this call even when the safe
                // storage cannot persist the change.
                logger?.LogWarning(exception, "No se pudo guardar la dirección actualizada del coordinador.");
            }

            logger?.LogInformation("Se actualizó la dirección del coordinador de {PreviousAddress} a {CurrentAddress}.",
                link.Address, candidate.Address);
            RememberVerified(candidate.Address);
            return candidate.Address;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task<bool> IsHealthyAsync(string address, string fingerprint, CancellationToken cancellationToken)
    {
        if (!CoordinatorPairingClient.TryValidateStoredConnection(address, fingerprint, out var uri, out var normalizedFingerprint))
            return false;
        try
        {
            using var client = CoordinatorPairingClient.CreatePinnedClient(uri!, normalizedFingerprint);
            client.Timeout = ProbeTimeout;
            using var response = await client.GetAsync("health", cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return false;
        }
    }

    private static bool TryGetCoordinatorId(string fingerprint, out string coordinatorId)
    {
        coordinatorId = string.Empty;
        var normalized = new string((fingerprint ?? string.Empty).Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
        if (normalized.Length != 64) return false;
        coordinatorId = Convert.ToHexString(Convert.FromHexString(normalized).AsSpan(0, 16));
        return true;
    }

    private void RememberVerified(string address)
    {
        _verifiedAddress = address;
        _verifiedAtUtc = DateTime.UtcNow;
        _failedAddress = null;
        _failedAtUtc = DateTime.MinValue;
    }

    private void RememberFailed(string address)
    {
        _failedAddress = address;
        _failedAtUtc = DateTime.UtcNow;
    }
}
