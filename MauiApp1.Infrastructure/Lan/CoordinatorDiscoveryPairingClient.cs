using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using MauiApp1.Application.Lan;
using MauiApp1.Application.Storage;

namespace MauiApp1.Infrastructure.Lan;

public sealed class CoordinatorDiscoveryPairingClient(
    ICoordinatorLinkStorage linkStorage,
    ILocalDeviceIdentity deviceIdentity) : ICoordinatorDiscoveryPairingClient, ICoordinatorDiscoveryApprovalClient
{
    private readonly ConcurrentDictionary<Guid, PairingSession> _sessions = new();

    public async Task<DiscoveryPairingStartResult> StartAsync(
        CoordinatorCandidate candidate,
        string deviceName,
        CancellationToken cancellationToken = default)
    {
        CleanupExpired();
        if (!TryValidateCandidate(candidate, out var address))
            return new(false, "El coordinador encontrado no tiene una dirección local o versión válida.");

        CoordinatorDeviceLink? existing;
        try { existing = await linkStorage.ReadAsync(); }
        catch { return new(false, "No se pudo leer la vinculación guardada en este dispositivo."); }
        if (existing?.IsConfirmed == true)
            return new(false, "Este dispositivo ya está vinculado. Desvincúlalo antes de cambiar de coordinador.");

        var deviceId = await deviceIdentity.GetDeviceIdAsync(cancellationToken);
        if (deviceId == Guid.Empty) return new(false, "No se pudo identificar esta instalación.");
        if (existing is not null && (existing.DeviceId != deviceId || !IsValidToken(existing.DeviceToken) ||
                NormalizeFingerprint(existing.CertificateFingerprint) is null))
            return new(false, "La vinculación pendiente guardada no es válida. Revisa el vínculo manual antes de continuar.");

        using var clientKey = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var clientPublicKey = clientKey.ExportSubjectPublicKeyInfo();
        var clientNonce = RandomNumberGenerator.GetBytes(32);
        var certificateCapture = new BootstrapCertificateCapture();
        try
        {
            using var client = CreateBootstrapClient(address, certificateCapture);
            using var response = await client.PostAsJsonAsync("api/devices/discovery/start", new StartRequest(
                deviceId, Convert.ToBase64String(clientPublicKey), Convert.ToBase64String(clientNonce),
                NormalizeDeviceName(deviceName), 1), cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new(false, response.StatusCode == HttpStatusCode.NotFound
                    ? "Este coordinador necesita actualizarse para habilitar el descubrimiento seguro. Usa QR o conexión manual."
                    : "No se pudo solicitar el vínculo. Comprueba que el coordinador esté actualizado y disponible.");

            var start = await response.Content.ReadFromJsonAsync<StartResponse>(cancellationToken: cancellationToken);
            if (start is null || start.RequestId == Guid.Empty || start.CoordinatorId != candidate.CoordinatorId ||
                start.ProtectedPayload is null || string.IsNullOrWhiteSpace(certificateCapture.Fingerprint) || !IsBase64(start.ServerPublicKey, 64, 1024) ||
                !IsBase64(start.ServerNonce, 32, 32))
                return new(false, "La respuesta del coordinador no corresponde al equipo seleccionado.");

            var serverPublicKey = Convert.FromBase64String(start.ServerPublicKey);
            var serverNonce = Convert.FromBase64String(start.ServerNonce);
            var sessionKey = CoordinatorDiscoveryPairingProtocol.DeriveSessionKey(clientKey, serverPublicKey,
                clientPublicKey, serverPublicKey, start.RequestId, deviceId, clientNonce, serverNonce,
                start.CoordinatorId);
            var retainedSession = false;
            try
            {
                var protectedBytes = CoordinatorDiscoveryPairingProtocol.Decrypt(sessionKey, start.RequestId,
                    "start-response", start.ProtectedPayload);
                StartProtectedPayload? protectedStart;
                try
                {
                    protectedStart = JsonSerializer.Deserialize<StartProtectedPayload>(protectedBytes,
                        JsonSerializerOptions.Web);
                }
                finally { CryptographicOperations.ZeroMemory(protectedBytes); }
                var certificateFingerprint = NormalizeFingerprint(protectedStart?.CertificateFingerprint);
                var tlsFingerprint = NormalizeFingerprint(certificateCapture.Fingerprint);
                if (certificateFingerprint is null || tlsFingerprint is null ||
                    !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(certificateFingerprint),
                        Convert.FromHexString(tlsFingerprint)) ||
                    !CoordinatorIdMatchesFingerprint(start.CoordinatorId, certificateFingerprint) ||
                    protectedStart!.ExpiresAtUtc <= DateTime.UtcNow ||
                    protectedStart.ExpiresAtUtc > DateTime.UtcNow.AddMinutes(3).AddSeconds(10))
                    return new(false, "No se pudo verificar la identidad del coordinador. No se guardó ni envió ninguna autorización.");
                if (existing is not null && !string.Equals(NormalizeFingerprint(existing.CertificateFingerprint),
                        certificateFingerprint, StringComparison.Ordinal))
                    return new(false, "Hay una vinculación pendiente con otro coordinador. Termina o elimina esa vinculación antes de cambiar de equipo.");

                var verificationCode = CoordinatorDiscoveryPairingProtocol.ComputeVerificationCode(sessionKey);
                var session = new PairingSession(start.RequestId, deviceId, address, certificateFingerprint,
                    start.CoordinatorId, sessionKey, verificationCode, protectedStart.ExpiresAtUtc,
                    existing?.DeviceToken ?? CreateDeviceToken());
                if (!_sessions.TryAdd(start.RequestId, session))
                {
                    session.Dispose();
                    return new(false, "No se pudo conservar la solicitud temporal. Inténtalo de nuevo.");
                }

                retainedSession = true;
                return new(true, "Compara este código con el que aparece en Windows. El Administrador debe aprobar el mismo código.",
                    start.RequestId, verificationCode, protectedStart.ExpiresAtUtc);
            }
            catch (Exception exception) when (exception is CryptographicException or FormatException or JsonException)
            {
                return new(false, "No se pudo verificar la respuesta segura del coordinador. No se creó el vínculo.");
            }
            finally
            {
                if (!retainedSession) CryptographicOperations.ZeroMemory(sessionKey);
            }
        }
        catch (HttpRequestException)
        {
            return new(false, "No se pudo contactar al coordinador en esta red. Comprueba que ambos equipos estén en la misma Wi-Fi.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(false, "El coordinador tardó demasiado en responder. Vuelve a buscarlo e inténtalo de nuevo.");
        }
        catch (JsonException)
        {
            return new(false, "El coordinador devolvió una respuesta no válida. Actualiza el servicio de Windows.");
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException or ArgumentException)
        {
            return new(false, "No fue posible comprobar la identidad del coordinador. El dispositivo no se vinculó.");
        }
    }

    public async Task<DiscoveryPairingStatusResult> GetStatusAsync(Guid requestId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetSession(requestId, out var session))
            return new(false, null, "La solicitud venció. Inicia una nueva búsqueda.");
        var challenge = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
        try
        {
            using var client = CoordinatorPairingClient.CreatePinnedClient(session.Address, session.Fingerprint);
            using var response = await client.PostAsJsonAsync("api/devices/discovery/status",
                new StatusRequest(requestId, CoordinatorDiscoveryPairingProtocol.Encrypt(session.SessionKey,
                    requestId, "status-request", JsonSerializer.SerializeToUtf8Bytes(new ChallengePayload(challenge)))),
                cancellationToken);
            if (!response.IsSuccessStatusCode) return new(false, null, "El coordinador no pudo consultar el estado del vínculo.");
            var status = await response.Content.ReadFromJsonAsync<ProtectedResponse>(cancellationToken: cancellationToken);
            if (status?.ProtectedPayload is null) return new(false, null, "La respuesta de estado no es válida.");
            var plain = CoordinatorDiscoveryPairingProtocol.Decrypt(session.SessionKey, requestId,
                "status-response", status.ProtectedPayload);
            try
            {
                var decoded = JsonSerializer.Deserialize<StatusPayload>(plain, JsonSerializerOptions.Web);
                return decoded?.Challenge == challenge
                    ? new(true, decoded.Status)
                    : new(false, null, "La respuesta de estado no coincide con esta solicitud.");
            }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }
        catch (HttpRequestException) { return new(false, null, "No se pudo contactar al coordinador. La solicitud sigue pendiente hasta vencer."); }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { return new(false, null, "El coordinador no respondió a tiempo."); }
        catch (Exception exception) when (exception is CryptographicException or FormatException or JsonException)
        { return new(false, null, "No se pudo verificar el estado de la solicitud."); }
    }

    public async Task<PairingActionResult> CompleteAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        if (!TryGetSession(requestId, out var session))
            return new(false, "La solicitud venció. Inicia una nueva búsqueda.");
        try
        {
            var existing = await linkStorage.ReadAsync();
            if (existing?.IsConfirmed == true)
            {
                if (existing.DeviceId == session.DeviceId && existing.DeviceToken == session.DeviceToken &&
                    string.Equals(existing.CertificateFingerprint, session.Fingerprint, StringComparison.OrdinalIgnoreCase))
                {
                    RemoveSession(requestId);
                    return new(true, "Dispositivo vinculado. Inicia sesión con tu cuenta central para sincronizar.");
                }
                return new(false, "Este dispositivo ya tiene otro vínculo confirmado. Desvincúlalo antes de continuar.");
            }
            if (existing is not null && (existing.DeviceId != session.DeviceId ||
                    existing.DeviceToken != session.DeviceToken ||
                    !string.Equals(existing.CertificateFingerprint, session.Fingerprint, StringComparison.OrdinalIgnoreCase)))
                return new(false, "Hay otra vinculación pendiente en este dispositivo. Termínala antes de confirmar esta solicitud.");

            await linkStorage.SaveAsync(new CoordinatorDeviceLink
            {
                DeviceId = session.DeviceId,
                Address = session.Address.AbsoluteUri,
                CertificateFingerprint = session.Fingerprint,
                DeviceToken = session.DeviceToken,
                IsConfirmed = false,
                PendingCode = existing?.PendingCode
            });
            if (existing is null) session.OwnsPendingLink = true;

            var payload = JsonSerializer.SerializeToUtf8Bytes(new CompletionPayload(session.DeviceId, session.DeviceToken));
            CoordinatorDiscoveryEncryptedPayload encryptedPayload;
            try
            {
                encryptedPayload = CoordinatorDiscoveryPairingProtocol.Encrypt(session.SessionKey,
                    requestId, "complete-request", payload);
            }
            finally { CryptographicOperations.ZeroMemory(payload); }
            using var client = CoordinatorPairingClient.CreatePinnedClient(session.Address, session.Fingerprint);
            using var response = await client.PostAsJsonAsync("api/devices/discovery/complete",
                new CompletionRequest(requestId, encryptedPayload), cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new(false, response.StatusCode == HttpStatusCode.Conflict
                    ? "Este dispositivo ya está autorizado con otra vinculación. Revoca la anterior desde el Administrador."
                    : "El coordinador no confirmó el vínculo. Puedes volver a intentar mientras la solicitud no venza.");
            var result = await response.Content.ReadFromJsonAsync<ProtectedResponse>(cancellationToken: cancellationToken);
            if (result?.ProtectedPayload is null) return new(false, "El coordinador devolvió una confirmación no válida.");
            var plain = CoordinatorDiscoveryPairingProtocol.Decrypt(session.SessionKey, requestId,
                "complete-response", result.ProtectedPayload);
            CompletionResult? completion;
            try { completion = JsonSerializer.Deserialize<CompletionResult>(plain, JsonSerializerOptions.Web); }
            finally { CryptographicOperations.ZeroMemory(plain); }
            if (completion?.Success != true) return new(false, completion?.Message ?? "El vínculo no fue confirmado.");

            try
            {
                await linkStorage.SaveAsync(new CoordinatorDeviceLink
                {
                    DeviceId = session.DeviceId,
                    Address = session.Address.AbsoluteUri,
                    CertificateFingerprint = session.Fingerprint,
                    DeviceToken = session.DeviceToken,
                    IsConfirmed = true
                });
            }
            catch
            {
                return new(false, "El coordinador autorizó el dispositivo, pero no se pudo guardar el vínculo seguro. Reintenta antes de que venza la solicitud.");
            }

            RemoveSession(requestId);
            return new(true, "Dispositivo vinculado. Inicia sesión con tu cuenta central para sincronizar.");
        }
        catch (HttpRequestException) { return new(false, "No se pudo contactar al coordinador. Puedes reintentar la confirmación."); }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { return new(false, "El coordinador no respondió a tiempo. Puedes reintentar la confirmación."); }
        catch (Exception exception) when (exception is CryptographicException or FormatException or JsonException)
        { return new(false, "No se pudo verificar la confirmación del vínculo."); }
        catch { return new(false, "No se pudo guardar o confirmar el vínculo. Si la solicitud sigue aprobada, puedes reintentar antes de que venza."); }
    }

    public async Task<PairingActionResult> CancelAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        if (!TryGetSession(requestId, out var session)) return new(true, "La solicitud ya venció o fue cancelada.");
        var challenge = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
        try
        {
            using var client = CoordinatorPairingClient.CreatePinnedClient(session.Address, session.Fingerprint);
            using var response = await client.PostAsJsonAsync("api/devices/discovery/cancel",
                new StatusRequest(requestId, CoordinatorDiscoveryPairingProtocol.Encrypt(session.SessionKey,
                    requestId, "cancel-request", JsonSerializer.SerializeToUtf8Bytes(new ChallengePayload(challenge)))),
                cancellationToken);
            if (!response.IsSuccessStatusCode) return new(false, "No se pudo cancelar la solicitud en el coordinador.");
            var result = await response.Content.ReadFromJsonAsync<ProtectedResponse>(cancellationToken: cancellationToken);
            if (result?.ProtectedPayload is null) return new(false, "El coordinador devolvió una confirmación no válida.");
            var plain = CoordinatorDiscoveryPairingProtocol.Decrypt(session.SessionKey, requestId,
                "cancel-response", result.ProtectedPayload);
            try
            {
                var payload = JsonSerializer.Deserialize<ActionResultPayload>(plain, JsonSerializerOptions.Web);
                if (payload?.Challenge != challenge) return new(false, "La confirmación de cancelación no corresponde a esta solicitud.");
                if (payload.Status == DiscoveryPairingStatus.Completed)
                    return new(false, "El vínculo ya se completó. No se canceló el dispositivo.");
                if (payload.Status is not (DiscoveryPairingStatus.Rejected or DiscoveryPairingStatus.Expired) && !payload.Success)
                    return new(false, "La solicitud no se pudo cancelar.");
            }
            finally { CryptographicOperations.ZeroMemory(plain); }
            if (session.OwnsPendingLink)
            {
                var pendingLink = await linkStorage.ReadAsync();
                if (pendingLink is { IsConfirmed: false } && pendingLink.DeviceId == session.DeviceId &&
                    pendingLink.DeviceToken == session.DeviceToken)
                    await linkStorage.RemoveAsync();
            }
            RemoveSession(requestId);
            return new(true, "Solicitud cancelada. El dispositivo sigue sin estar vinculado.");
        }
        catch (HttpRequestException) { return new(false, "No se pudo contactar al coordinador para cancelar la solicitud."); }
        catch (Exception exception) when (exception is CryptographicException or FormatException or JsonException)
        { return new(false, "No se pudo verificar la cancelación."); }
        catch { return new(false, "No se pudo completar la cancelación. La solicitud conserva su vencimiento automático."); }
    }

    public async Task<CoordinatorAdminSessionResult> CreateAdminSessionAsync(string address, string fingerprint,
        string userName, string password, CancellationToken cancellationToken = default)
    {
        if (!TryCreatePinnedClient(address, fingerprint, out var client, out var error))
            return new(false, error);
        using (client)
        {
            try
            {
                using var response = await client!.PostAsJsonAsync("api/devices/discovery/admin/session",
                    new AdminSessionRequest(userName, password), cancellationToken);
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                    return new(false, "El usuario o contraseña no corresponde a una cuenta Administrador del coordinador.");
                if (!response.IsSuccessStatusCode) return new(false, "No se pudo abrir la aprobación segura en el coordinador.");
                var result = await response.Content.ReadFromJsonAsync<AdminSessionResponse>(cancellationToken: cancellationToken);
                if (result is null || !IsValidToken(result.Token) || result.ExpiresAtUtc <= DateTime.UtcNow ||
                    result.ExpiresAtUtc > DateTime.UtcNow.AddMinutes(5).AddSeconds(10))
                    return new(false, "El coordinador devolvió una sesión de Administrador no válida.");
                return new(true, "Sesión de aprobación abierta por cinco minutos.", result.Token, result.ExpiresAtUtc);
            }
            catch (HttpRequestException) { return new(false, "No se pudo contactar al coordinador."); }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { return new(false, "El coordinador no respondió a tiempo."); }
            catch (JsonException) { return new(false, "La respuesta del coordinador no es válida."); }
        }
    }

    public async Task<CoordinatorPendingPairingsResult> GetPendingAsync(string address, string fingerprint,
        string adminToken, CancellationToken cancellationToken = default)
    {
        if (!IsValidToken(adminToken)) return new(false, [], "La sesión de Administrador no es válida. Vuelve a autenticarte.");
        if (!TryCreatePinnedClient(address, fingerprint, out var client, out var error))
            return new(false, [], error);
        using (client)
        {
            try
            {
                client!.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
                using var response = await client.GetAsync("api/devices/discovery/admin/pending", cancellationToken);
                if (!response.IsSuccessStatusCode) return new(false, [], response.StatusCode == HttpStatusCode.Unauthorized
                    ? "La sesión de aprobación venció. Vuelve a autenticarte como Administrador."
                    : "No se pudieron consultar las solicitudes pendientes.");
                var pending = await response.Content.ReadFromJsonAsync<List<PendingPairingWire>>(cancellationToken: cancellationToken);
                var results = (pending ?? []).Where(item => item.RequestId != Guid.Empty && item.DeviceId != Guid.Empty &&
                        item.DeviceName is { Length: > 0 and <= 50 } && item.VerificationCode is { Length: 8 } &&
                        item.VerificationCode.All(char.IsAsciiDigit) && item.ExpiresAtUtc > DateTime.UtcNow)
                    .Select(item => new CoordinatorPendingPairing(item.RequestId, item.DeviceId, item.DeviceName!,
                        item.VerificationCode!, item.ExpiresAtUtc)).ToArray();
                return new(true, results);
            }
            catch (HttpRequestException) { return new(false, [], "No se pudo contactar al coordinador."); }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { return new(false, [], "El coordinador no respondió a tiempo."); }
            catch (JsonException) { return new(false, [], "La lista de solicitudes devuelta por el coordinador no es válida."); }
        }
    }

    public Task<PairingActionResult> ApproveAsync(string address, string fingerprint, string adminToken,
        Guid requestId, CancellationToken cancellationToken = default) =>
        SendAdminActionAsync("approve", address, fingerprint, adminToken, requestId, cancellationToken);

    public Task<PairingActionResult> RejectAsync(string address, string fingerprint, string adminToken,
        Guid requestId, CancellationToken cancellationToken = default) =>
        SendAdminActionAsync("reject", address, fingerprint, adminToken, requestId, cancellationToken);

    private async Task<PairingActionResult> SendAdminActionAsync(string action, string address, string fingerprint,
        string adminToken, Guid requestId, CancellationToken cancellationToken)
    {
        if (!IsValidToken(adminToken)) return new(false, "La sesión de Administrador no es válida. Vuelve a autenticarte.");
        if (!TryCreatePinnedClient(address, fingerprint, out var client, out var error)) return new(false, error);
        using (client)
        {
            try
            {
                client!.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
                using var response = await client.PostAsJsonAsync($"api/devices/discovery/admin/{action}",
                    new AdminActionRequest(requestId), cancellationToken);
                var result = await response.Content.ReadFromJsonAsync<ActionResponse>(cancellationToken: cancellationToken);
                return response.IsSuccessStatusCode && result?.Success == true
                    ? new(true, result.Message)
                    : new(false, result?.Message ?? (response.StatusCode == HttpStatusCode.Unauthorized
                        ? "La sesión de aprobación venció. Vuelve a autenticarte como Administrador."
                        : "La solicitud ya venció, cambió de estado o no existe."));
            }
            catch (HttpRequestException) { return new(false, "No se pudo contactar al coordinador."); }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { return new(false, "El coordinador no respondió a tiempo."); }
            catch (JsonException) { return new(false, "La respuesta del coordinador no es válida."); }
        }
    }

    private bool TryGetSession(Guid requestId, out PairingSession session)
    {
        CleanupExpired();
        if (_sessions.TryGetValue(requestId, out session!) && session.ExpiresAtUtc > DateTime.UtcNow) return true;
        session = null!;
        return false;
    }

    private void CleanupExpired()
    {
        foreach (var (id, session) in _sessions)
            if (session.ExpiresAtUtc <= DateTime.UtcNow) RemoveSession(id);
    }

    private void RemoveSession(Guid requestId)
    {
        if (_sessions.TryRemove(requestId, out var session)) session.Dispose();
    }

    private static HttpClient CreateBootstrapClient(Uri address, BootstrapCertificateCapture certificateCapture)
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
            {
                if (certificate is null || DateTime.UtcNow < certificate.NotBefore.ToUniversalTime() ||
                    DateTime.UtcNow > certificate.NotAfter.ToUniversalTime()) return false;
                return certificateCapture.Capture(certificate);
            }
        };
        return new HttpClient(handler) { BaseAddress = address, Timeout = TimeSpan.FromSeconds(12) };
    }

    private sealed class BootstrapCertificateCapture
    {
        private string? _fingerprint;
        public string? Fingerprint => _fingerprint;
        public bool Capture(X509Certificate2 certificate)
        {
            var fingerprint = certificate.GetCertHashString(HashAlgorithmName.SHA256);
            if (_fingerprint is not null && !string.Equals(_fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase))
                return false;
            _fingerprint = fingerprint;
            return true;
        }
    }

    private static bool TryCreatePinnedClient(string address, string fingerprint, out HttpClient? client, out string error)
    {
        client = null;
        if (!CoordinatorPairingClient.TryValidateStoredConnection(address, fingerprint, out var uri, out var normalized))
        {
            error = "La dirección HTTPS local o la huella del coordinador no son válidas.";
            return false;
        }
        client = CoordinatorPairingClient.CreatePinnedClient(uri!, normalized);
        error = string.Empty;
        return true;
    }

    private static bool TryValidateCandidate(CoordinatorCandidate candidate, out Uri address)
    {
        address = null!;
        if (candidate is null || candidate.ProtocolVersion != 1 || string.IsNullOrWhiteSpace(candidate.CoordinatorId) || candidate.CoordinatorId.Length != 32 ||
            !candidate.CoordinatorId.All(Uri.IsHexDigit) ||
            !Uri.TryCreate(candidate.Address, UriKind.Absolute, out var parsed) ||
            parsed.Scheme != Uri.UriSchemeHttps || parsed.AbsolutePath != "/" || parsed.UserInfo.Length != 0 ||
            parsed.Query.Length != 0 || parsed.Fragment.Length != 0 ||
            !IPAddress.TryParse(parsed.Host, out var ip) || !IsPrivateIpv4(ip) || parsed.Port is < 1 or > 65535)
            return false;
        address = parsed;
        return true;
    }

    private static bool IsPrivateIpv4(IPAddress address)
    {
        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork || IPAddress.IsLoopback(address)) return false;
        var bytes = address.GetAddressBytes();
        return bytes[0] == 10 || bytes[0] == 192 && bytes[1] == 168 ||
            bytes[0] == 172 && bytes[1] is >= 16 and <= 31;
    }

    private static string? NormalizeFingerprint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = new string(value.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
        return normalized.Length == 64 ? normalized : null;
    }

    private static bool CoordinatorIdMatchesFingerprint(string coordinatorId, string fingerprint) =>
        string.Equals(coordinatorId, Convert.ToHexString(Convert.FromHexString(fingerprint).AsSpan(0, 16)),
            StringComparison.OrdinalIgnoreCase);

    private static bool IsBase64(string value, int minimum, int maximum)
    {
        try { var bytes = Convert.FromBase64String(value); return bytes.Length >= minimum && bytes.Length <= maximum; }
        catch (FormatException) { return false; }
    }

    private static bool IsValidToken(string? token) => token is { Length: 43 } && token.All(character =>
        char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private static string NormalizeDeviceName(string name)
    {
        var normalized = new string((name ?? string.Empty).Where(character => !char.IsControl(character)).Take(50).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(normalized) ? "Dispositivo nuevo" : normalized;
    }

    private static string CreateDeviceToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class PairingSession(Guid requestId, Guid deviceId, Uri address, string fingerprint,
        string coordinatorId, byte[] sessionKey, string verificationCode, DateTime expiresAtUtc, string deviceToken) : IDisposable
    {
        public Guid RequestId { get; } = requestId;
        public Guid DeviceId { get; } = deviceId;
        public Uri Address { get; } = address;
        public string Fingerprint { get; } = fingerprint;
        public string CoordinatorId { get; } = coordinatorId;
        public byte[] SessionKey { get; } = sessionKey;
        public string VerificationCode { get; } = verificationCode;
        public DateTime ExpiresAtUtc { get; } = expiresAtUtc;
        public string DeviceToken { get; } = deviceToken;
        public bool OwnsPendingLink { get; set; }
        public void Dispose() => CryptographicOperations.ZeroMemory(SessionKey);
        public override string ToString() => $"{nameof(PairingSession)} {{ RequestId = {RequestId}, ExpiresAtUtc = {ExpiresAtUtc} }}";
    }

    private sealed record StartRequest(Guid DeviceId, string ClientPublicKey, string ClientNonce, string DeviceName, int Version);
    private sealed record StartResponse(Guid RequestId, string CoordinatorId, string ServerPublicKey, string ServerNonce,
        CoordinatorDiscoveryEncryptedPayload ProtectedPayload);
    private sealed record StartProtectedPayload(string CertificateFingerprint, DateTime ExpiresAtUtc);
    private sealed record StatusRequest(Guid RequestId, CoordinatorDiscoveryEncryptedPayload ProtectedPayload);
    private sealed record ChallengePayload(string Challenge);
    private sealed record StatusPayload(string Challenge, DiscoveryPairingStatus Status);
    private sealed record ProtectedResponse(CoordinatorDiscoveryEncryptedPayload ProtectedPayload);
    private sealed record CompletionPayload(Guid DeviceId, string DeviceToken);
    private sealed record CompletionRequest(Guid RequestId, CoordinatorDiscoveryEncryptedPayload ProtectedPayload);
    private sealed record CompletionResult(bool Success, string Message);
    private sealed record ActionResultPayload(string Challenge, bool Success, string Message, DiscoveryPairingStatus Status);
    private sealed record AdminSessionRequest(string UserName, string Password);
    private sealed record AdminSessionResponse(string Token, DateTime ExpiresAtUtc);
    private sealed record PendingPairingWire(Guid RequestId, Guid DeviceId, string? DeviceName,
        string? VerificationCode, DateTime ExpiresAtUtc);
    private sealed record AdminActionRequest(Guid RequestId);
    private sealed record ActionResponse(bool Success, string Message);
}
