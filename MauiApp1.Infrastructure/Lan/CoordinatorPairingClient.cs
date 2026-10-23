using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using MauiApp1.Application.Lan;
using MauiApp1.Application.Authentication;
using MauiApp1.Application.Storage;
using MauiApp1.Application.Sync;
using MauiApp1.Domain.Users;
using MauiApp1.Infrastructure.Authentication;
using Microsoft.Extensions.Logging;

namespace MauiApp1.Infrastructure.Lan;

public sealed class CoordinatorPairingClient(
    ICoordinatorLinkStorage storage,
    ICoordinatorUserSessionStorage sessionStorage,
    ILocalDeviceIdentity deviceIdentity,
    ILogger<CoordinatorPairingClient>? logger = null) : ICoordinatorPairingClient, ICoordinatorAuthenticationClient, ICoordinatorCashierAccountClient, ICoordinatorPasswordRecoveryClient, ICoordinatorChangeClient, IInitialDataImportClient
{
    private readonly SemaphoreSlim _pairingLock = new(1, 1);
    public string? LastChangeError { get; private set; }

    public async Task<CoordinatorChangesPage?> GetChangesAsync(long afterSequence, int limit = 100, CancellationToken cancellationToken = default)
    {
        LastChangeError = null;
        var link = await ReadLinkAsync();
        var session = await sessionStorage.ReadAsync();
        if (link is null || !link.IsConfirmed || session is null || session.ExpiresAtUtc <= DateTime.UtcNow || afterSequence < 0)
        {
            LastChangeError = "No hay una sesión central activa para descargar cambios. Inicia sesión con el coordinador disponible.";
            return null;
        }
        if (!TryValidateConnection(link.Address, link.CertificateFingerprint, out var uri, out var fingerprint, out var validationError))
        {
            LastChangeError = validationError;
            return null;
        }
        try
        {
            using var client = CreatePinnedClient(uri!, fingerprint);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", link.DeviceToken);
            client.DefaultRequestHeaders.Add("X-Device-Id", link.DeviceId.ToString("D"));
            client.DefaultRequestHeaders.Add("X-User-Session", session.SessionToken);
            using var response = await client.GetAsync($"api/sync/changes?afterSequence={afterSequence}&limit={Math.Clamp(limit, 1, 500)}", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                LastChangeError = response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => "El coordinador rechazó la sesión de descarga. Cierra sesión e inicia sesión de nuevo con el coordinador disponible.",
                    HttpStatusCode.NotFound => "El coordinador instalado no ofrece la descarga de cambios. Actualiza el servicio de Windows.",
                    _ => $"El coordinador no pudo devolver los cambios (respuesta {(int)response.StatusCode}). Reintenta la sincronización."
                };
                return null;
            }
            return await response.Content.ReadFromJsonAsync<CoordinatorChangesPage>(cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            logger?.LogWarning(exception, "No se pudieron descargar los cambios del coordinador.");
            LastChangeError = "No se pudo contactar al coordinador para descargar cambios. Comprueba la conexión LAN y la dirección de Windows.";
            return null;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
        catch (System.Text.Json.JsonException exception)
        {
            logger?.LogWarning(exception, "El coordinador devolvió una página de cambios inválida.");
            LastChangeError = "Los cambios recibidos tienen un formato incompatible. Actualiza el coordinador y la aplicación.";
            return null;
        }
    }

    public async Task<IReadOnlyList<CoordinatorConflictItem>?> GetConflictsAsync(CancellationToken cancellationToken = default)
    {
        var auth = await CreateSessionClientAsync();
        if (auth is null) return null;
        using (auth)
        using (var response = await auth.GetAsync("api/sync/conflicts", cancellationToken))
        {
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<IReadOnlyList<CoordinatorConflictItem>>(cancellationToken);
        }
    }

    public async Task<string?> ResolveConflictAsync(Guid operationId, bool applyIncoming, CancellationToken cancellationToken = default)
    {
        var auth = await CreateSessionClientAsync();
        if (auth is null) return "El coordinador no está disponible o la sesión venció.";
        using (auth)
        using (var response = await auth.PostAsJsonAsync("api/sync/conflicts/resolve",
                   new { operationId, applyIncoming }, cancellationToken))
        {
            var result = await response.Content.ReadFromJsonAsync<ApiMessageResponse>(cancellationToken);
            return response.IsSuccessStatusCode ? null : result?.Message ?? "El coordinador no pudo resolver el conflicto.";
        }
    }

    public async Task<InitialImportClientResult<InitialImportPreview>> PreviewAsync(
        InitialDataImportSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        var client = await CreateSessionClientAsync();
        if (client is null)
            return new(false, null, "Vincula este dispositivo e inicia sesión central como Administrador con el coordinador disponible.");
        client.Timeout = TimeSpan.FromMinutes(5);
        using (client)
        {
            try
            {
                using var response = await client.PostAsJsonAsync("api/sync/initial-import/preview", snapshot, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var preview = await response.Content.ReadFromJsonAsync<InitialImportPreview>(cancellationToken);
                    return preview is null
                        ? new(false, null, "El coordinador devolvió una vista previa vacía.")
                        : new(true, preview, null);
                }

                var error = await response.Content.ReadFromJsonAsync<ApiMessageResponse>(cancellationToken);
                return new(false, null, error?.Message ?? response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => "La sesión del Administrador venció. Inicia sesión nuevamente.",
                    HttpStatusCode.Forbidden => "Solo el Administrador puede importar datos previos.",
                    HttpStatusCode.NotFound => "Actualiza el coordinador para habilitar la importación de datos previos.",
                    HttpStatusCode.RequestEntityTooLarge => "El historial es demasiado grande para analizarlo en un solo envío. No se modificó ninguna base.",
                    _ => "El coordinador no pudo analizar los datos de este dispositivo. No se importó nada."
                });
            }
            catch (HttpRequestException)
            {
                return new(false, null, "No se pudo contactar al coordinador en la red local. No se modificaron los datos.");
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new(false, null, "El coordinador tardó demasiado. Puedes volver a analizar los datos; aún no se importó nada.");
            }
            catch (System.Text.Json.JsonException)
            {
                return new(false, null, "La respuesta del coordinador no es válida. Actualiza el coordinador y la aplicación.");
            }
        }
    }

    public async Task<InitialImportClientResult<InitialImportApplyResult>> ApplyAsync(
        InitialImportApplyRequest request, CancellationToken cancellationToken = default)
    {
        var client = await CreateSessionClientAsync();
        if (client is null)
            return new(false, null, "Vincula este dispositivo e inicia sesión central como Administrador con el coordinador disponible.");
        client.Timeout = TimeSpan.FromMinutes(5);
        using (client)
        {
            try
            {
                using var response = await client.PostAsJsonAsync("api/sync/initial-import/apply", request, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<InitialImportApplyResult>(cancellationToken);
                    return result is null
                        ? new(false, null, "El coordinador devolvió un resultado de importación vacío.")
                        : new(result.Completed, result, result.Completed ? null : result.Message);
                }
                var error = await response.Content.ReadFromJsonAsync<ApiMessageResponse>(cancellationToken);
                return new(false, null, error?.Message ?? response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => "La sesión del Administrador venció. Inicia sesión nuevamente.",
                    HttpStatusCode.Forbidden => "Solo el Administrador puede confirmar la importación.",
                    HttpStatusCode.NotFound => "No se encontró la vista previa. Analiza nuevamente los datos.",
                    HttpStatusCode.Conflict => "La base central cambió desde la vista previa. Analiza nuevamente antes de confirmar.",
                    _ => "El coordinador no pudo completar la importación. Revisa el detalle y vuelve a intentar."
                });
            }
            catch (HttpRequestException)
            {
                return new(false, null, "Se perdió la conexión durante la importación. Puedes reintentar la misma vista previa; no se duplicarán los registros confirmados.");
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new(false, null, "La importación tardó demasiado. Reintenta la misma vista previa para continuar sin duplicar registros.");
            }
            catch (System.Text.Json.JsonException)
            {
                return new(false, null, "La respuesta del coordinador no es válida. Actualiza el coordinador y la aplicación.");
            }
        }
    }

    private async Task<HttpClient?> CreateSessionClientAsync()
    {
        var link = await ReadLinkAsync();
        var session = await sessionStorage.ReadAsync();
        if (link is null || !link.IsConfirmed || session is null || session.ExpiresAtUtc <= DateTime.UtcNow ||
            !TryValidateConnection(link.Address, link.CertificateFingerprint, out var uri, out var fingerprint, out _)) return null;
        var client = CreatePinnedClient(uri!, fingerprint);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", link.DeviceToken);
        client.DefaultRequestHeaders.Add("X-Device-Id", link.DeviceId.ToString("D"));
        client.DefaultRequestHeaders.Add("X-User-Session", session.SessionToken);
        return client;
    }

    public async Task<AccountProvisioningResult> CreateCashierAsync(
        string userName, string password, string recoveryCode, CancellationToken cancellationToken = default)
    {
        var link = await ReadLinkAsync();
        var session = await sessionStorage.ReadAsync();
        if (link is null || !link.IsConfirmed || session is null || session.ExpiresAtUtc <= DateTime.UtcNow ||
            session.LocalUserId == Guid.Empty || session.CoordinatorUserId == Guid.Empty)
            return AccountProvisioningResult.Failure("Conecta este dispositivo al coordinador e inicia sesión central como Administrador para crear cuentas.");
        if (!TryValidateConnection(link.Address, link.CertificateFingerprint, out var uri, out var fingerprint, out var error))
            return AccountProvisioningResult.Failure(error ?? "La conexión del coordinador no es válida.");
        if (!RecoveryCodeGenerator.TryNormalize(recoveryCode, out _))
            return AccountProvisioningResult.Failure("El código de recuperación no es válido.");
        try
        {
            using var client = CreatePinnedClient(uri!, fingerprint);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", link.DeviceToken);
            client.DefaultRequestHeaders.Add("X-Device-Id", link.DeviceId.ToString("D"));
            client.DefaultRequestHeaders.Add("X-User-Session", session.SessionToken);
            using var response = await client.PostAsJsonAsync(
                "api/users/cashiers", new { userName, password, recoveryCode }, cancellationToken);
            if (response.IsSuccessStatusCode) return AccountProvisioningResult.Success(recoveryCode);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return AccountProvisioningResult.Failure("La sesión del Administrador venció. Inicia sesión de nuevo.");
            var errorResult = await response.Content.ReadFromJsonAsync<ApiMessageResponse>(cancellationToken);
            return AccountProvisioningResult.Failure(errorResult?.Message ?? "El coordinador no pudo crear la cuenta.");
        }
        catch (HttpRequestException) { return AccountProvisioningResult.Unknown(recoveryCode, "No se recibió confirmación del coordinador. Conserva el código y actualiza la lista de cajeros antes de volver a crear la cuenta."); }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return AccountProvisioningResult.Unknown(recoveryCode, "El coordinador no confirmó el resultado. Conserva el código y actualiza la lista antes de volver a crear la cuenta."); }
        catch (System.Text.Json.JsonException) { return AccountProvisioningResult.Unknown(recoveryCode, "La respuesta del coordinador no es válida. Conserva el código y actualiza la lista antes de volver a crear la cuenta."); }
    }

    public async Task<CoordinatorPasswordRecoveryResult> ResetPasswordAsync(
        string userName,
        string recoveryCode,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        CoordinatorDeviceLink? link;
        try { link = await ReadLinkAsync(); }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        { return CoordinatorPasswordRecoveryResult.Rejected("La vinculación guardada no es válida. Vuelve a vincular este dispositivo."); }

        if (link is null || !link.IsConfirmed) return CoordinatorPasswordRecoveryResult.NotLinked();
        if (!TryValidateConnection(link.Address, link.CertificateFingerprint, out var uri, out var fingerprint, out var error))
            return CoordinatorPasswordRecoveryResult.Rejected(error);

        try
        {
            using var client = CreatePinnedClient(uri!, fingerprint);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", link.DeviceToken);
            client.DefaultRequestHeaders.Add("X-Device-Id", link.DeviceId.ToString("D"));
            using var response = await client.PostAsJsonAsync("api/auth/recovery/reset",
                new { userName, recoveryCode, newPassword }, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<RecoveryCodeResponse>(cancellationToken);
                return !string.IsNullOrWhiteSpace(result?.RecoveryCode)
                    ? CoordinatorPasswordRecoveryResult.Completed(result.RecoveryCode)
                    : CoordinatorPasswordRecoveryResult.OutcomeUnknown("El coordinador pudo cambiar la contraseña, pero no se recibió el código nuevo. Intenta iniciar sesión con la contraseña nueva y, al entrar, genera otro código desde Seguridad de la cuenta.");
            }

            var errorResult = await ReadApiMessageAsync(response, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.ServiceUnavailable)
                return CoordinatorPasswordRecoveryResult.Unavailable(errorResult?.Message ?? "Actualiza o inicia el coordinador del negocio y vuelve a intentarlo.");
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                return CoordinatorPasswordRecoveryResult.Rejected(errorResult?.Message ?? "Se hicieron demasiados intentos. Espera un minuto y vuelve a intentarlo.");
            return response.StatusCode is >= HttpStatusCode.InternalServerError
                ? CoordinatorPasswordRecoveryResult.OutcomeUnknown(errorResult?.Message ?? "El coordinador no confirmó el resultado. El cambio pudo completarse; intenta iniciar sesión con la contraseña nueva.")
                : CoordinatorPasswordRecoveryResult.Rejected(errorResult?.Message ??
                    "El usuario o el código no son correctos, o esta cuenta aún no tiene recuperación configurada.");
        }
        catch (HttpRequestException) { return CoordinatorPasswordRecoveryResult.OutcomeUnknown("Se perdió la conexión antes de confirmar el resultado. El cambio pudo completarse; intenta iniciar sesión con la contraseña nueva. Si logras entrar, genera otro código desde Seguridad de la cuenta."); }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return CoordinatorPasswordRecoveryResult.OutcomeUnknown("El coordinador tardó demasiado para confirmar. El cambio pudo completarse; intenta iniciar sesión con la contraseña nueva. Si logras entrar, genera otro código desde Seguridad de la cuenta."); }
        catch (System.Text.Json.JsonException)
        { return CoordinatorPasswordRecoveryResult.OutcomeUnknown("La respuesta del coordinador no se pudo leer. El cambio pudo completarse; intenta iniciar sesión con la contraseña nueva. Si logras entrar, genera otro código desde Seguridad de la cuenta."); }
    }

    public async Task<CoordinatorPasswordRecoveryResult> ConfigureRecoveryCodeAsync(
        string currentPassword,
        CancellationToken cancellationToken = default)
    {
        CoordinatorDeviceLink? link;
        try { link = await ReadLinkAsync(); }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        { return CoordinatorPasswordRecoveryResult.Rejected("La vinculación guardada no es válida. Vuelve a vincular este dispositivo."); }
        if (link is null || !link.IsConfirmed) return CoordinatorPasswordRecoveryResult.NotLinked();

        var client = await CreateSessionClientAsync();
        if (client is null)
            return CoordinatorPasswordRecoveryResult.Unavailable("Inicia sesión con el coordinador y vuelve a intentarlo.");

        using (client)
        {
            try
            {
                using var response = await client.PostAsJsonAsync(
                    "api/auth/recovery/enroll", new { currentPassword }, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<RecoveryCodeResponse>(cancellationToken);
                    return !string.IsNullOrWhiteSpace(result?.RecoveryCode)
                        ? CoordinatorPasswordRecoveryResult.Completed(result.RecoveryCode)
                        : CoordinatorPasswordRecoveryResult.OutcomeUnknown("El código pudo guardarse, pero no se recibió. Vuelve a generarlo para obtener uno nuevo.");
                }

                var error = await ReadApiMessageAsync(response, cancellationToken);
                return response.StatusCode == HttpStatusCode.Unauthorized
                    ? CoordinatorPasswordRecoveryResult.Rejected(error?.Message ?? "La contraseña actual no es correcta o la sesión venció.")
                    : CoordinatorPasswordRecoveryResult.Unavailable(error?.Message ?? "El coordinador no pudo guardar el código.");
            }
            catch (HttpRequestException) { return CoordinatorPasswordRecoveryResult.OutcomeUnknown("Se perdió la conexión y no se confirmó el resultado. El código pudo guardarse; vuelve a generarlo para obtener uno nuevo."); }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            { return CoordinatorPasswordRecoveryResult.OutcomeUnknown("El coordinador tardó demasiado. El código pudo guardarse; vuelve a generarlo para obtener uno nuevo."); }
            catch (System.Text.Json.JsonException)
            { return CoordinatorPasswordRecoveryResult.OutcomeUnknown("La respuesta no se pudo leer. El código pudo guardarse; vuelve a generarlo para obtener uno nuevo."); }
        }
    }

    private static async Task<ApiMessageResponse?> ReadApiMessageAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try { return await response.Content.ReadFromJsonAsync<ApiMessageResponse>(cancellationToken); }
        catch (System.Text.Json.JsonException) { return null; }
    }

    public async Task<CashierAccountListResult> GetCashiersAsync(CancellationToken cancellationToken = default)
    {
        var client = await CreateSessionClientAsync();
        if (client is null)
            return CashierAccountListResult.Failure(
                "Vincula este dispositivo e inicia sesión central como Administrador para consultar los cajeros.");

        using (client)
        {
            try
            {
                using var response = await client.GetAsync("api/users/cashiers", cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var accounts = await response.Content.ReadFromJsonAsync<IReadOnlyList<CashierAccountSummary>>(cancellationToken);
                    return accounts is null
                        ? CashierAccountListResult.Failure("El coordinador devolvió una lista vacía no válida.")
                        : CashierAccountListResult.Success(accounts);
                }

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                    return CashierAccountListResult.Failure("La sesión del Administrador venció. Inicia sesión de nuevo.");
                if (response.StatusCode == HttpStatusCode.Forbidden)
                    return CashierAccountListResult.Failure("Solo el Administrador puede consultar las cuentas de cajero.");

                var error = await response.Content.ReadFromJsonAsync<ApiMessageResponse>(cancellationToken);
                return CashierAccountListResult.Failure(
                    error?.Message ?? "El coordinador no pudo consultar las cuentas de cajero.");
            }
            catch (HttpRequestException)
            {
                return CashierAccountListResult.Failure(
                    "No se pudo contactar al coordinador por la red local. Comprueba la conexión e inténtalo de nuevo.");
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return CashierAccountListResult.Failure("El coordinador no respondió a tiempo. Inténtalo de nuevo.");
            }
            catch (System.Text.Json.JsonException)
            {
                return CashierAccountListResult.Failure("La respuesta del coordinador no tiene un formato válido.");
            }
        }
    }

    public async Task<CoordinatorLinkStatus?> GetStatusAsync()
    {
        var link = await ReadLinkAsync();
        return link is null ? null : new(link.Address, link.CertificateFingerprint, link.IsConfirmed, link.PendingCode);
    }

    public async Task<CoordinatorAvailability> CheckAvailabilityAsync()
    {
        CoordinatorDeviceLink? link;
        try { link = await ReadLinkAsync(); }
        catch { return new(true, false, false); }
        if (link is null) return new(false, false, false);

        if (!TryValidateConnection(link.Address, link.CertificateFingerprint, out var uri, out var fingerprint, out _))
            return new(true, false, link.IsConfirmed);
        try
        {
            using var client = CreateClient(uri!, fingerprint);
            var health = await client.GetFromJsonAsync<HealthResponse>("health");
            return new(true, health?.Status == "ok", link.IsConfirmed);
        }
        catch (HttpRequestException) { return new(true, false, link.IsConfirmed); }
        catch (TaskCanceledException) { return new(true, false, link.IsConfirmed); }
        catch (System.Text.Json.JsonException) { return new(true, false, link.IsConfirmed); }
    }

    public async Task<PairingCodeResult> CreateCodeAsync(string address, string fingerprint, string userName, string password)
    {
        if (!TryValidateConnection(address, fingerprint, out var uri, out var normalizedFingerprint, out var error))
            return new(false, error);
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrEmpty(password))
            return new(false, "Escribe el usuario y la contraseña del Administrador del coordinador.");

        try
        {
            using var client = CreateClient(uri!, normalizedFingerprint);
            using var response = await client.PostAsJsonAsync("api/devices/pairing-code", new { userName, password });
            if (!response.IsSuccessStatusCode) return new(false, ErrorFor(response.StatusCode));
            var result = await response.Content.ReadFromJsonAsync<CodeResponse>();
            if (result is null || !IsValidCode(result.Code) || result.ExpiresAtUtc <= DateTime.UtcNow)
                return new(false, "El coordinador no devolvió un código válido.");
            return new(true, "Código generado.", result.Code, result.ExpiresAtUtc);
        }
        catch (HttpRequestException) { return new(false, ConnectionError); }
        catch (TaskCanceledException) { return new(false, "El coordinador no respondió a tiempo. Comprueba que esté encendido y en la misma red."); }
        catch (System.Text.Json.JsonException) { return new(false, "La respuesta del coordinador no es válida."); }
    }

    public async Task<PairingActionResult> PairAsync(string address, string fingerprint, string code)
    {
        await _pairingLock.WaitAsync();
        try { return await PairCoreAsync(address, fingerprint, code); }
        finally { _pairingLock.Release(); }
    }

    public async Task<PairingActionResult> UnlinkAsync()
    {
        await _pairingLock.WaitAsync();
        try
        {
            CoordinatorDeviceLink? link;
            try { link = await ReadLinkAsync(); }
            catch { return new(false, "No se pudo leer la autorización segura. No se modificó la vinculación."); }
            if (link is null) return new(true, "Este dispositivo ya está desvinculado.");
            if (!link.IsConfirmed) return new(false, "La vinculación está pendiente. Reintenta o corrige sus datos antes de desvincular.");
            if (!TryValidateConnection(link.Address, link.CertificateFingerprint, out var uri, out var fingerprint, out _))
                return new(false, "La conexión guardada no es válida. No se borraron las credenciales.");

            try
            {
                using var client = CreateClient(uri!, fingerprint);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", link.DeviceToken);
                client.DefaultRequestHeaders.Add("X-Device-Id", link.DeviceId.ToString("D"));
                using var response = await client.PostAsJsonAsync("api/devices/unlink", new { deviceId = link.DeviceId });
                if (!response.IsSuccessStatusCode)
                    return new(false, response.StatusCode == HttpStatusCode.NotFound
                        ? "El coordinador de Windows aún no tiene habilitada la desvinculación. Hay que actualizar el servicio coordinador como Administrador; este dispositivo sigue vinculado y sus datos no se modificaron."
                        : response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                        ? "El coordinador no confirmó la revocación. Las credenciales locales se conservaron; comprueba la conexión y vuelve a intentar."
                        : ErrorFor(response.StatusCode));

                try
                {
                    await sessionStorage.RemoveAsync();
                    await storage.RemoveAsync();
                }
                catch { return new(false, "El coordinador revocó el dispositivo, pero no se pudo borrar la credencial local. Vuelve a intentar con el coordinador disponible."); }
                return new(true, "Dispositivo desvinculado. Los productos, ventas y demás datos guardados en este dispositivo se conservaron.");
            }
            catch (HttpRequestException) { return new(false, "No se pudo contactar al coordinador. Conéctate a la red local y vuelve a intentar; no se borró ninguna credencial."); }
            catch (TaskCanceledException) { return new(false, "El coordinador no respondió a tiempo. No se borró ninguna credencial; vuelve a intentar cuando esté disponible."); }
        }
        finally { _pairingLock.Release(); }
    }

    public async Task<CoordinatorSignInResult> SignInAsync(
        string userName, string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrEmpty(password))
            return new(CoordinatorSignInStatus.Rejected);

        CoordinatorDeviceLink? link;
        try { link = await ReadLinkAsync(); }
        catch { return new(CoordinatorSignInStatus.Unavailable); }
        if (link is null || !link.IsConfirmed)
            return new(CoordinatorSignInStatus.NotLinked);
        if (!TryValidateConnection(link.Address, link.CertificateFingerprint, out var uri, out var fingerprint, out _))
            return new(CoordinatorSignInStatus.Unavailable);

        try
        {
            using var client = CreateClient(uri!, fingerprint);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", link.DeviceToken);
            client.DefaultRequestHeaders.Add("X-Device-Id", link.DeviceId.ToString("D"));
            using var response = await client.PostAsJsonAsync("api/auth/session", new { userName, password }, cancellationToken);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var error = await response.Content.ReadFromJsonAsync<ApiMessageResponse>(cancellationToken: cancellationToken);
                return new(CoordinatorSignInStatus.RetryLater,
                    Message: error?.Message ?? "Se hicieron demasiados intentos. Espera un minuto y vuelve a iniciar sesión.");
            }
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return new(CoordinatorSignInStatus.Rejected);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadFromJsonAsync<ApiMessageResponse>(cancellationToken: cancellationToken);
                return new(CoordinatorSignInStatus.Unavailable,
                    Message: error?.Message ?? $"El coordinador respondió con el error HTTP {(int)response.StatusCode}. Se usó el acceso local; no se creó una sesión compartida.");
            }

            var result = await response.Content.ReadFromJsonAsync<SessionResponse>(cancellationToken: cancellationToken);
            if (result is null || result.UserId == Guid.Empty || string.IsNullOrWhiteSpace(result.UserName) ||
                result.UserName.Trim().Length > 80 || !Enum.TryParse<UserRole>(result.Role, true, out var role) ||
                role is not (UserRole.Administrator or UserRole.Cashier) ||
                string.IsNullOrEmpty(result.SessionToken) || result.SessionToken.Length != 43 || !result.SessionToken.All(character =>
                    char.IsAsciiLetterOrDigit(character) || character is '-' or '_') ||
                result.ExpiresAtUtc <= DateTime.UtcNow)
                return new(CoordinatorSignInStatus.Unavailable);

            return new(CoordinatorSignInStatus.Authenticated, result.UserId, result.UserName, role,
                result.SessionToken, result.ExpiresAtUtc);
        }
        catch (HttpRequestException) { return new(CoordinatorSignInStatus.Unavailable); }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return new(CoordinatorSignInStatus.Unavailable); }
        catch (System.Text.Json.JsonException) { return new(CoordinatorSignInStatus.Unavailable); }
    }

    private async Task<PairingActionResult> PairCoreAsync(string address, string fingerprint, string code)
    {
        if (!TryValidateConnection(address, fingerprint, out var uri, out var normalizedFingerprint, out var error))
            return new(false, error);
        code = code.Trim();
        if (!IsValidCode(code)) return new(false, "Escribe el código de ocho dígitos generado en Windows.");

        CoordinatorDeviceLink? existing;
        try { existing = await ReadLinkAsync(); }
        catch { return new(false, "No se pudo leer la autorización guardada en este dispositivo."); }
        if (existing?.IsConfirmed == true) return new(true, "Este dispositivo ya está vinculado.");
        if (existing is not null && existing.CertificateFingerprint != normalizedFingerprint)
            return new(false, "Hay una vinculación pendiente. Reintenta con el código de seguridad del coordinador guardado.");

        // Verify the pinned server before locking in a pending authorization. A typing
        // error in the fingerprint must not leave the device bound to unusable settings.
        try
        {
            using var probe = CreateClient(uri!, normalizedFingerprint);
            var health = await probe.GetFromJsonAsync<HealthResponse>("health");
            if (health?.Status != "ok") return new(false, "La dirección no corresponde a un coordinador disponible.");
        }
        catch (HttpRequestException) { return new(false, ConnectionError); }
        catch (TaskCanceledException) { return new(false, "El coordinador no respondió a tiempo. Comprueba su dirección y la red local."); }
        catch (System.Text.Json.JsonException) { return new(false, "La dirección no devolvió una respuesta válida del coordinador."); }

        var pending = new CoordinatorDeviceLink
        {
            DeviceId = await deviceIdentity.GetDeviceIdAsync(),
            Address = uri!.AbsoluteUri,
            CertificateFingerprint = normalizedFingerprint,
            DeviceToken = existing?.DeviceToken ?? Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_'),
            IsConfirmed = false,
            PendingCode = code
        };
        try { await storage.SaveAsync(pending); }
        catch { return new(false, "No se pudo guardar la autorización de forma segura. No se envió la vinculación."); }

        try
        {
            using var client = CreateClient(uri, normalizedFingerprint);
            using var response = await client.PostAsJsonAsync("api/devices/pair", new
            {
                deviceId = pending.DeviceId, code, deviceToken = pending.DeviceToken
            });
            if (!response.IsSuccessStatusCode) return new(false, response.StatusCode == HttpStatusCode.BadRequest
                ? "El código es inválido o venció. Solicita uno nuevo al Administrador y vuelve a intentar."
                : ErrorFor(response.StatusCode));
            var result = await response.Content.ReadFromJsonAsync<PairResponse>();
            if (result?.Success != true) return new(false, "El coordinador no confirmó la vinculación.");
            try
            {
                await storage.SaveAsync(new CoordinatorDeviceLink
                {
                    DeviceId = pending.DeviceId, Address = pending.Address,
                    CertificateFingerprint = pending.CertificateFingerprint, DeviceToken = pending.DeviceToken,
                    IsConfirmed = true
                });
            }
            catch { return new(false, "El coordinador autorizó el dispositivo, pero falta guardar la confirmación. Reintenta la vinculación."); }
            return new(true, "Dispositivo vinculado. La sincronización de cuentas y datos se incorporará en los siguientes pasos.");
        }
        catch (HttpRequestException) { return new(false, ConnectionError + " La vinculación queda pendiente para reintentar."); }
        catch (TaskCanceledException) { return new(false, "El coordinador no respondió a tiempo. Reintenta para recuperar la confirmación."); }
        catch (System.Text.Json.JsonException) { return new(false, "La respuesta no es válida. Reintenta la vinculación."); }
    }

    private async Task<CoordinatorDeviceLink?> ReadLinkAsync()
    {
        var link = await storage.ReadAsync();
        if (link is not null && (!TryValidateConnection(link.Address, link.CertificateFingerprint, out _, out _, out _) ||
            link.DeviceToken.Length != 43 || !link.DeviceToken.All(character =>
                char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
            throw new InvalidDataException("La autorización guardada no es válida.");
        if (link is not null && link.DeviceId != await deviceIdentity.GetDeviceIdAsync())
            throw new InvalidDataException("La autorización guardada pertenece a otra instalación.");
        return link;
    }

    private static HttpClient CreateClient(Uri address, string fingerprint)
    {
        return new HttpClient(CreatePinnedHandler(fingerprint))
            { BaseAddress = address, Timeout = TimeSpan.FromSeconds(10) };
    }

    internal static HttpClientHandler CreatePinnedHandler(string fingerprint)
    {
        var expectedHash = Convert.FromHexString(fingerprint);
        return new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            ServerCertificateCustomValidationCallback = (_, certificate, _, _) => certificate is not null &&
                DateTime.UtcNow >= certificate.NotBefore.ToUniversalTime() &&
                DateTime.UtcNow <= certificate.NotAfter.ToUniversalTime() &&
                CryptographicOperations.FixedTimeEquals(certificate.GetCertHash(HashAlgorithmName.SHA256), expectedHash)
        };
    }

    internal static HttpClient CreatePinnedClient(Uri address, string fingerprint) => CreateClient(address, fingerprint);

    internal static bool TryValidateStoredConnection(
        string address, string fingerprint, out Uri? uri, out string normalizedFingerprint) =>
        TryValidateConnection(address, fingerprint, out uri, out normalizedFingerprint, out _);

    private static bool TryValidateConnection(string address, string fingerprint, out Uri? uri,
        out string normalizedFingerprint, out string error)
    {
        uri = null;
        normalizedFingerprint = fingerprint.Trim().Replace(" ", string.Empty).Replace(":", string.Empty).ToUpperInvariant();
        error = "Usa una dirección HTTPS con la IP local de Windows, por ejemplo https://192.168.1.10:5080.";
        if (!Uri.TryCreate(address.Trim(), UriKind.Absolute, out var parsed) || parsed.Scheme != Uri.UriSchemeHttps ||
            parsed.AbsolutePath != "/" || parsed.UserInfo.Length != 0 || parsed.Query.Length != 0 || parsed.Fragment.Length != 0 ||
            !IPAddress.TryParse(parsed.Host, out var ip) || !IsLocalAddress(ip)) return false;
        error = "Copia el código de seguridad SHA-256 del coordinador: debe contener 64 caracteres hexadecimales.";
        if (normalizedFingerprint.Length != 64 || !normalizedFingerprint.All(Uri.IsHexDigit)) return false;
        uri = parsed;
        error = string.Empty;
        return true;
    }

    private static bool IsLocalAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return true;
        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false;
        var bytes = address.GetAddressBytes();
        return bytes[0] == 10 || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
               (bytes[0] == 192 && bytes[1] == 168);
    }

    private static bool IsValidCode(string? code) => code?.Length == 8 && code.All(character => character is >= '0' and <= '9');
    private const string ConnectionError = "No se pudo conectar con el coordinador. Comprueba la red local, la dirección y el código de seguridad del certificado.";
    private static string ErrorFor(HttpStatusCode code) => code switch
    {
        HttpStatusCode.Unauthorized => "Las credenciales del Administrador del coordinador no son correctas o no tienen ese rol.",
        HttpStatusCode.Conflict => "El dispositivo ya tiene otra autorización en el coordinador. Solicita ayuda al Administrador.",
        HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable => "Hay demasiados intentos. Espera un minuto y vuelve a intentar.",
        _ => "El coordinador no pudo completar la solicitud. Inténtalo de nuevo."
    };
    private sealed record CodeResponse(string Code, DateTime ExpiresAtUtc);
    private sealed record PairResponse(bool Success, string Message);
    private sealed record HealthResponse(string Status);
    private sealed record SessionResponse(string SessionToken, Guid UserId, string UserName, string Role, DateTime ExpiresAtUtc);
    private sealed record RecoveryCodeResponse(string? RecoveryCode);
    private sealed record ApiMessageResponse(string Message);
}


//agregar exepcion al cambiar direccion IP 
