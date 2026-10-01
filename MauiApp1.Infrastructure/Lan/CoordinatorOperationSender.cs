using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MauiApp1.Application.Authentication;
using MauiApp1.Application.Lan;
using MauiApp1.Application.Storage;
using MauiApp1.Application.Sync;

namespace MauiApp1.Infrastructure.Lan;

public sealed class CoordinatorOperationSender(
    ICoordinatorLinkStorage linkStorage,
    ICoordinatorUserSessionStorage sessionStorage,
    IAuthenticationService authenticationService,
    ILocalDeviceIdentity deviceIdentity) : ICoordinatorOperationSender
{
    public async Task<CoordinatorOperationSendResult> SendAsync(
        OutboxOperation operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (operation.OperationId == Guid.Empty || operation.DeviceId == Guid.Empty || operation.ActorUserId == Guid.Empty ||
            string.IsNullOrWhiteSpace(operation.OperationType) || operation.OperationType.Length > 120 ||
            string.IsNullOrWhiteSpace(operation.PayloadJson))
            return new(CoordinatorOperationSendStatus.Rejected);

        var link = await linkStorage.ReadAsync().ConfigureAwait(false);
        if (link is null || !link.IsConfirmed)
            return new(CoordinatorOperationSendStatus.NotLinked);

        var session = await sessionStorage.ReadAsync().ConfigureAwait(false);
        var currentUser = authenticationService.CurrentUser;
        if (session is null || session.LocalUserId == Guid.Empty || session.CoordinatorUserId == Guid.Empty ||
            session.ExpiresAtUtc <= DateTime.UtcNow || session.SessionToken.Length != 43 ||
            !session.SessionToken.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_'))
            return new(CoordinatorOperationSendStatus.SessionUnavailable);

        var currentLocalUserId = currentUser?.Id.Value;
        var mappedCoordinatorUserId = await authenticationService
            .GetCoordinatorUserIdAsync(operation.ActorUserId, cancellationToken)
            .ConfigureAwait(false);
        if (currentLocalUserId != operation.ActorUserId || session.LocalUserId != operation.ActorUserId ||
            mappedCoordinatorUserId is null || session.CoordinatorUserId != mappedCoordinatorUserId ||
            operation.DeviceId != link.DeviceId || operation.DeviceId != await deviceIdentity.GetDeviceIdAsync(cancellationToken)
                .ConfigureAwait(false))
            return new(CoordinatorOperationSendStatus.IdentityMismatch);

        if (!CoordinatorPairingClient.TryValidateStoredConnection(
                link.Address, link.CertificateFingerprint, out var uri, out var fingerprint) ||
            link.DeviceToken.Length != 43 || !link.DeviceToken.All(character =>
                char.IsAsciiLetterOrDigit(character) || character is '-' or '_'))
            return new(CoordinatorOperationSendStatus.NotLinked);

        try
        {
            using var client = CoordinatorPairingClient.CreatePinnedClient(uri!, fingerprint);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", link.DeviceToken);
            client.DefaultRequestHeaders.Add("X-Device-Id", link.DeviceId.ToString("D"));
            client.DefaultRequestHeaders.Add("X-User-Session", session.SessionToken);

            // ActorUserId remains in the wire contract for compatibility, but it carries the mapped
            // central ID and is not trusted by the coordinator; the validated session is authoritative.
            using var response = await client.PostAsJsonAsync("api/sync/operations", new
            {
                operationId = operation.OperationId,
                deviceId = operation.DeviceId,
                actorUserId = session.CoordinatorUserId,
                operationType = operation.OperationType,
                payloadJson = operation.PayloadJson,
                createdAtUtc = DateTime.SpecifyKind(operation.CreatedAtUtc, DateTimeKind.Utc)
            }, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return new(CoordinatorOperationSendStatus.Unauthorized);
            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                var conflict = await response.Content.ReadFromJsonAsync<SyncOperationResponse>(cancellationToken)
                    .ConfigureAwait(false);
                return new(CoordinatorOperationSendStatus.Conflict, conflict?.Sequence, conflict?.Message);
            }
            if (response.StatusCode == HttpStatusCode.BadRequest)
                return new(CoordinatorOperationSendStatus.Rejected);
            if (!response.IsSuccessStatusCode)
                return new(CoordinatorOperationSendStatus.Unavailable);

            var result = await response.Content.ReadFromJsonAsync<SyncOperationResponse>(cancellationToken)
                .ConfigureAwait(false);
            if (result is null || !result.Accepted || result.Sequence is null or <= 0)
                return new(CoordinatorOperationSendStatus.Rejected);

            return new(result.AlreadyAccepted
                ? CoordinatorOperationSendStatus.AlreadyAccepted
                : CoordinatorOperationSendStatus.Accepted, result.Sequence);
        }
        catch (HttpRequestException) { return new(CoordinatorOperationSendStatus.Unavailable); }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return new(CoordinatorOperationSendStatus.Unavailable); }
        catch (System.Text.Json.JsonException) { return new(CoordinatorOperationSendStatus.Rejected); }
    }

    private sealed record SyncOperationResponse(bool Accepted, bool AlreadyAccepted, long? Sequence, string Message);
}
