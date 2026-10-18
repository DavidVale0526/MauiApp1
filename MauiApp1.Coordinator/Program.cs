using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;
using System.Threading.RateLimiting;
using MauiApp1.Application.Authentication;
using MauiApp1.Application.Storage;
using MauiApp1.Application.Sync;
using MauiApp1.Coordinator.Persistence;
using MauiApp1.Coordinator.Pairing;
using MauiApp1.Coordinator.Sync;
using MauiApp1.Domain.Users;
using MauiApp1.Infrastructure.Persistence;
using MauiApp1.Infrastructure.Sync;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

internal static class Program
{
    private static readonly SemaphoreSlim PairingCredentialLock = new(1, 1);

    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Contains("--initialize-database", StringComparer.OrdinalIgnoreCase))
            {
                var dataDirectoryOption = ReadOption(args, "--data-directory");
                var databaseDataDirectory = GetCoordinatorDataDirectory(dataDirectoryOption);
                await InitializeCoordinatorDatabaseAsync(databaseDataDirectory);
                return 0;
            }

            var bootstrapSource = ReadOption(args, "--bootstrap-from");
            if (bootstrapSource is not null)
            {
                var dataDirectoryOption = ReadOption(args, "--data-directory");
                var bootstrapDataDirectory = GetCoordinatorDataDirectory(dataDirectoryOption);
                var createdPath = await CoordinatorDatabaseBootstrapper.BootstrapAsync(bootstrapSource, bootstrapDataDirectory);
                Console.WriteLine($"Base central inicial creada desde Windows: {createdPath}");
                return 0;
            }

            return await RunCoordinatorAsync(args);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"No se pudo iniciar el coordinador: {exception.Message}");
            return 1;
        }
    }

    private static async Task InitializeCoordinatorDatabaseAsync(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        var databasePath = Path.Combine(dataDirectory, "miscelanea-ari-central.db");
        if (File.Exists(databasePath))
            throw new InvalidOperationException("La base central ya existe; no se sobrescribió ni se reinicializó.");

        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddLogging();
        services.AddLocalPersistence(databasePath, registerLocalSyncServices: false);
        await using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<ILocalDatabaseInitializer>().InitializeAsync();

        var pairingStore = new CoordinatorPairingStore(databasePath);
        await pairingStore.InitializeAsync();
        var operationStore = new CoordinatorOperationStore(databasePath);
        await operationStore.InitializeAsync();
        Console.WriteLine($"Base central nueva inicializada: {databasePath}");
    }

    private static async Task<int> RunCoordinatorAsync(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Host.UseWindowsService(options => options.ServiceName = "Miscelanea Ari Coordinator");
        builder.Logging.ClearProviders();
        if (OperatingSystem.IsWindows() && !Environment.UserInteractive)
        {
            builder.Logging.AddEventLog();
        }
        else
        {
            builder.Logging.AddConsole();
        }

        var isDevelopment = builder.Environment.IsDevelopment();
        var allowEmptyDevelopmentDatabase = isDevelopment &&
            builder.Configuration.GetValue<bool>("Coordinator:AllowEmptyDevelopmentDatabase");
        var endpointUrl = builder.Configuration["Coordinator:Url"] ?? "https://0.0.0.0:5080";
        X509Certificate2? coordinatorCertificate = null;
        if (!Uri.TryCreate(endpointUrl, UriKind.Absolute, out var endpoint))
        {
            throw new InvalidOperationException("Coordinator:Url debe ser una URL absoluta.");
        }

        if (endpoint.Scheme == Uri.UriSchemeHttps)
        {
            coordinatorCertificate = LoadCoordinatorCertificate(
                builder.Configuration["Coordinator:CertificateThumbprint"], isDevelopment);
            builder.WebHost.ConfigureKestrel(options => options.ListenAnyIP(endpoint.Port,
                listenOptions => listenOptions.UseHttps(coordinatorCertificate)));
        }
        else if (isDevelopment && endpoint.Scheme == Uri.UriSchemeHttp &&
                 IPAddress.TryParse(endpoint.Host, out var listenAddress) && IPAddress.IsLoopback(listenAddress))
        {
            builder.WebHost.ConfigureKestrel(options => options.Listen(listenAddress, endpoint.Port));
        }
        else
        {
            throw new InvalidOperationException(
                "El coordinador requiere HTTPS. HTTP solo se permite en Development y ligado a loopback.");
        }

        builder.Services.AddSignalR();
        builder.Services.AddHostedService<MauiApp1.Coordinator.Discovery.CoordinatorMdnsAdvertiser>();
        var coordinatorFingerprint = coordinatorCertificate?.GetCertHashString(HashAlgorithmName.SHA256);
        builder.Services.AddSingleton(new MauiApp1.Coordinator.Discovery.CoordinatorDiscoveryPairingRegistry(coordinatorFingerprint));
        builder.Services.AddRateLimiter(options =>
        {
            options.AddPolicy("device-pairing", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));
            options.AddPolicy("discovery-pairing", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 60,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));
            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.HttpContext.Response.Headers.RetryAfter = "60";
                await context.HttpContext.Response.WriteAsJsonAsync(
                    new ApiMessageResponse("Se hicieron demasiados intentos de acceso en poco tiempo. Espera un minuto y vuelve a intentarlo."),
                    cancellationToken);
            };
        });

        var configuredDataDirectory = builder.Configuration["Coordinator:DataDirectory"];
        var dataDirectory = !string.IsNullOrWhiteSpace(configuredDataDirectory)
            ? configuredDataDirectory
            : allowEmptyDevelopmentDatabase
                ? Path.Combine(AppContext.BaseDirectory, "coordinator-development")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "Miscelanea Ari", "Coordinator");
        Directory.CreateDirectory(dataDirectory);
        var centralDatabasePath = Path.Combine(dataDirectory, "miscelanea-ari-central.db");
        builder.Services.AddLocalPersistence(centralDatabasePath, registerLocalSyncServices: false);
        builder.Services.AddSingleton<CoordinatorConnectionRegistry>();
        builder.Services.AddSingleton(_ => new CoordinatorPairingStore(centralDatabasePath));
        builder.Services.AddSingleton(_ => new CoordinatorOperationStore(centralDatabasePath));
        builder.Services.AddSingleton(serviceProvider => new CoordinatorInitialDataImportService(
            centralDatabasePath,
            serviceProvider.GetRequiredService<IDbContextFactory<LocalDbContext>>(),
            serviceProvider.GetRequiredService<CoordinatorOperationStore>(),
            serviceProvider.GetRequiredService<CoordinatorBusinessOperationApplier>()));
        builder.Services.AddSingleton(serviceProvider => new CoordinatorChangeBroadcaster(
            centralDatabasePath,
            serviceProvider.GetRequiredService<CoordinatorConnectionRegistry>(),
            serviceProvider.GetRequiredService<CoordinatorPairingStore>(),
            serviceProvider.GetRequiredService<Microsoft.AspNetCore.SignalR.IHubContext<CoordinatorChangesHub>>(),
            serviceProvider.GetRequiredService<ILogger<CoordinatorChangeBroadcaster>>()));
        builder.Services.AddHostedService(serviceProvider =>
            serviceProvider.GetRequiredService<CoordinatorChangeBroadcaster>());

        var app = builder.Build();

        if (!File.Exists(centralDatabasePath) && !allowEmptyDevelopmentDatabase)
        {
            throw new InvalidOperationException(
                "La base central no existe. Inicialízala con --bootstrap-from <ruta-base-Windows> antes de iniciar el coordinador.");
        }

        await app.Services.GetRequiredService<ILocalDatabaseInitializer>().InitializeAsync();
        var pairingStore = app.Services.GetRequiredService<CoordinatorPairingStore>();
        await pairingStore.InitializeAsync();
        var operationStore = app.Services.GetRequiredService<CoordinatorOperationStore>();
        var businessOperationApplier = app.Services.GetRequiredService<CoordinatorBusinessOperationApplier>();
        await operationStore.InitializeAsync();
        var initialDataImportService = app.Services.GetRequiredService<CoordinatorInitialDataImportService>();
        await initialDataImportService.InitializeAsync();

        // This liveness endpoint exposes no account, inventory, or transaction data.
        app.MapGet("/health", () => TypedResults.Ok(new HealthResponse("ok")));
        app.MapHub<CoordinatorChangesHub>("/hubs/changes");

        app.UseRateLimiter();
        app.MapPost("/api/devices/discovery/start", (
            MauiApp1.Coordinator.Discovery.CoordinatorDiscoveryStartRequest request,
            HttpContext httpContext,
            MauiApp1.Coordinator.Discovery.CoordinatorDiscoveryPairingRegistry discoveryRegistry) =>
        {
            if (!httpContext.Request.IsHttps)
                return Results.Problem("La solicitud de vínculo requiere HTTPS.", statusCode: StatusCodes.Status400BadRequest);
            if (!discoveryRegistry.IsEnabled)
                return Results.Problem("El coordinador no tiene una identidad HTTPS disponible.", statusCode: StatusCodes.Status503ServiceUnavailable);
            var created = discoveryRegistry.CreateRequest(request);
            return created is null
                ? Results.BadRequest(new ApiMessageResponse("La solicitud no es válida o el coordinador alcanzó el límite de solicitudes pendientes."))
                : Results.Ok(created);
        }).RequireRateLimiting("device-pairing");

        app.MapPost("/api/devices/discovery/status", (
            MauiApp1.Coordinator.Discovery.CoordinatorDiscoveryStatusRequest request,
            HttpContext httpContext,
            MauiApp1.Coordinator.Discovery.CoordinatorDiscoveryPairingRegistry discoveryRegistry) =>
        {
            if (!httpContext.Request.IsHttps || request.ProtectedPayload is null) return Results.BadRequest();
            var pending = discoveryRegistry.FindRequest(request.RequestId);
            if (pending is null) return Results.NotFound();
            try
            {
                var plaintext = MauiApp1.Infrastructure.Lan.CoordinatorDiscoveryPairingProtocol.Decrypt(
                    pending.SessionKey, request.RequestId, "status-request", request.ProtectedPayload);
                MauiApp1.Coordinator.Discovery.CoordinatorDiscoveryChallengePayload? challenge;
                try { challenge = System.Text.Json.JsonSerializer.Deserialize<MauiApp1.Coordinator.Discovery.CoordinatorDiscoveryChallengePayload>(plaintext, System.Text.Json.JsonSerializerOptions.Web); }
                finally { CryptographicOperations.ZeroMemory(plaintext); }
                if (!IsValidDiscoveryChallenge(challenge?.Challenge)) return Results.BadRequest();
                var protectedPayload = MauiApp1.Infrastructure.Lan.CoordinatorDiscoveryPairingProtocol.Encrypt(
                    pending.SessionKey, request.RequestId, "status-response",
                    System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
                        new MauiApp1.Coordinator.Discovery.CoordinatorDiscoveryStatusPayload(challenge!.Challenge, pending.GetStatus())));
                return Results.Ok(new MauiApp1.Coordinator.Discovery.CoordinatorDiscoveryProtectedResponse(protectedPayload));
            }
            catch (Exception exception) when (exception is CryptographicException or FormatException or System.Text.Json.JsonException)
            { return Results.BadRequest(); }
        }).RequireRateLimiting("discovery-pairing");

        app.MapPost("/api/devices/discovery/cancel", (
            MauiApp1.Coordinator.Discovery.CoordinatorDiscoveryStatusRequest request,
            HttpContext httpContext,
            MauiApp1.Coordinator.Discovery.CoordinatorDiscoveryPairingRegistry discoveryRegistry) =>
        {
            if (!httpContext.Request.IsHttps || request.ProtectedPayload is null) return Results.BadRequest();
            var pending = discoveryRegistry.FindRequest(request.RequestId);
            if (pending is null) return Results.NotFound();
            try
            {
                var plaintext = MauiApp1.Infrastructure.Lan.CoordinatorDiscoveryPairingProtocol.Decrypt(
                    pending.SessionKey, request.RequestId, "cancel-request", request.ProtectedPayload);
                MauiApp1.Coordinator.Discovery.CoordinatorDiscoveryChallengePayload? challenge;
                try { challenge = System.Text.Json.JsonSerializer.Deserialize<MauiApp1.Coordinator.Discovery.CoordinatorDiscoveryChallengePayload>(plaintext, System.Text.Json.JsonSerializerOptions.Web); }
                finally { CryptographicOperations.ZeroMemory(plaintext); }
                if (!IsValidDiscoveryChallenge(challenge?.Challenge)) return Results.BadRequest();
                var canceled = discoveryRegistry.Cancel(request.RequestId);
                var protectedPayload = MauiApp1.Infrastructure.Lan.CoordinatorDiscoveryPairingProtocol.Encrypt(
                    pending.SessionKey, request.RequestId, "cancel-response",
                    System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new MauiApp1.Coordinator.Discovery.CoordinatorDiscoveryActionPayload(
                        challenge!.Challenge, canceled, canceled ? "Solicitud cancelada." : "La solicitud ya no está pendiente.", pending.GetStatus())));
                return Results.Ok(new MauiApp1.Coordinator.Discovery.CoordinatorDiscoveryProtectedResponse(protectedPayload));
            }
            catch (Exception exception) when (exception is CryptographicException or FormatException or System.Text.Json.JsonException)
            { return Results.BadRequest(); }
        }).RequireRateLimiting("discovery-pairing");

        app.MapPost("/api/devices/discovery/complete", async (
            MauiApp1.Coordinator.Discovery.CoordinatorDiscoveryCompletionRequest request,
            HttpContext httpContext,
            MauiApp1.Coordinator.Discovery.CoordinatorDiscoveryPairingRegistry discoveryRegistry,
            CancellationToken cancellationToken) =>
        {
            if (!httpContext.Request.IsHttps || request.ProtectedPayload is null) return Results.BadRequest();
            var pending = discoveryRegistry.FindRequest(request.RequestId);
            if (pending is null) return Results.NotFound();
            try
            {
                var plaintext = MauiApp1.Infrastructure.Lan.CoordinatorDiscoveryPairingProtocol.Decrypt(
                    pending.SessionKey, request.RequestId, "complete-request", request.ProtectedPayload);
                MauiApp1.Coordinator.Discovery.CoordinatorDiscoveryCompletionPayload? completion;
                try { completion = System.Text.Json.JsonSerializer.Deserialize<MauiApp1.Coordinator.Discovery.CoordinatorDiscoveryCompletionPayload>(plaintext, System.Text.Json.JsonSerializerOptions.Web); }
                finally { CryptographicOperations.ZeroMemory(plaintext); }
                if (completion is null || completion.DeviceId != pending.DeviceId ||
                    !IsValidDiscoveredDeviceToken(completion.DeviceToken) ||
                    !pending.TryBeginCompletion())
                    return Results.Conflict();

                MauiApp1.Coordinator.Pairing.DevicePairingResult pairingResult;
                try
                {
                    pairingResult = await pairingStore.AuthorizeDiscoveredDeviceAsync(
                        completion.DeviceId, completion.DeviceToken!, cancellationToken);
                }
                catch
                {
                    pending.MarkCompletionFailed();
                    throw;
                }
                var succeeded = pairingResult.IsAuthorized && pending.MarkCompleted();
                if (!succeeded) pending.MarkCompletionFailed();
                var encryptedResponse = MauiApp1.Infrastructure.Lan.CoordinatorDiscoveryPairingProtocol.Encrypt(
                    pending.SessionKey, request.RequestId, "complete-response",
                    System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new MauiApp1.Coordinator.Discovery.CoordinatorDiscoveryCompletionResponse(
                        succeeded, succeeded ? "Dispositivo vinculado." : "El coordinador no pudo autorizar este dispositivo.")));
                if (pairingResult.IsAlreadyAuthorized) return Results.Conflict();
                return Results.Ok(new MauiApp1.Coordinator.Discovery.CoordinatorDiscoveryProtectedResponse(encryptedResponse));
            }
            catch (Exception exception) when (exception is CryptographicException or FormatException or System.Text.Json.JsonException)
            { return Results.BadRequest(); }
        }).RequireRateLimiting("discovery-pairing");

        app.MapPost("/api/devices/discovery/admin/session", async (
            MauiApp1.Coordinator.Discovery.CoordinatorDiscoveryAdminSessionRequest request,
            HttpContext httpContext,
            IAuthenticationService authenticationService,
            MauiApp1.Coordinator.Discovery.CoordinatorDiscoveryPairingRegistry discoveryRegistry,
            CancellationToken cancellationToken) =>
        {
            if (!httpContext.Request.IsHttps) return Results.BadRequest();
            if (string.IsNullOrWhiteSpace(request.UserName) || request.UserName.Length > 80 ||
                string.IsNullOrEmpty(request.Password) || request.Password.Length > 1024) return Results.Unauthorized();
            await PairingCredentialLock.WaitAsync(cancellationToken);
            try
            {
                var authentication = await authenticationService.SignInAsync(request.UserName, request.Password, cancellationToken);
                if (!authentication.Succeeded || authentication.User?.Role != UserRole.Administrator ||
                    authentication.User.Id.Value == Guid.Empty) return Results.Unauthorized();
                var token = discoveryRegistry.CreateAdminSession(authentication.User.Id.Value);
                return Results.Ok(new MauiApp1.Coordinator.Discovery.CoordinatorDiscoveryAdminSessionResponse(
                    token, discoveryRegistry.GetAdminSessionExpiry(token)));
            }
            finally
            {
                authenticationService.SignOut();
                PairingCredentialLock.Release();
            }
        }).RequireRateLimiting("device-pairing");

        app.MapGet("/api/devices/discovery/admin/pending", (
            HttpContext httpContext,
            MauiApp1.Coordinator.Discovery.CoordinatorDiscoveryPairingRegistry discoveryRegistry) =>
        {
            if (!httpContext.Request.IsHttps) return Results.BadRequest();
            var token = ReadBearerToken(httpContext);
            if (token is null || discoveryRegistry.ValidateAdminSession(token) is null) return Results.Unauthorized();
            return Results.Ok(discoveryRegistry.GetPendingRequests());
        }).RequireRateLimiting("discovery-pairing");

        app.MapPost("/api/devices/discovery/admin/approve", (
            MauiApp1.Coordinator.Discovery.CoordinatorDiscoveryAdminActionRequest request,
            HttpContext httpContext,
            MauiApp1.Coordinator.Discovery.CoordinatorDiscoveryPairingRegistry discoveryRegistry) =>
        {
            if (!httpContext.Request.IsHttps) return Results.BadRequest();
            var token = ReadBearerToken(httpContext);
            if (token is null || discoveryRegistry.ValidateAdminSession(token) is null) return Results.Unauthorized();
            var approved = discoveryRegistry.Approve(request.RequestId);
            return approved
                ? Results.Ok(new MauiApp1.Coordinator.Discovery.CoordinatorDiscoveryAdminActionResponse(true,
                    "Solicitud aprobada. El dispositivo puede completar el vínculo."))
                : Results.Conflict(new MauiApp1.Coordinator.Discovery.CoordinatorDiscoveryAdminActionResponse(false,
                    "La solicitud venció o ya no está pendiente."));
        }).RequireRateLimiting("discovery-pairing");

        app.MapPost("/api/devices/discovery/admin/reject", (
            MauiApp1.Coordinator.Discovery.CoordinatorDiscoveryAdminActionRequest request,
            HttpContext httpContext,
            MauiApp1.Coordinator.Discovery.CoordinatorDiscoveryPairingRegistry discoveryRegistry) =>
        {
            if (!httpContext.Request.IsHttps) return Results.BadRequest();
            var token = ReadBearerToken(httpContext);
            if (token is null || discoveryRegistry.ValidateAdminSession(token) is null) return Results.Unauthorized();
            var rejected = discoveryRegistry.Reject(request.RequestId);
            return rejected
                ? Results.Ok(new MauiApp1.Coordinator.Discovery.CoordinatorDiscoveryAdminActionResponse(true,
                    "Solicitud rechazada."))
                : Results.Conflict(new MauiApp1.Coordinator.Discovery.CoordinatorDiscoveryAdminActionResponse(false,
                    "La solicitud venció o ya no está pendiente."));
        }).RequireRateLimiting("discovery-pairing");

        app.MapPost("/api/auth/session", async (
            CreateUserSessionRequest request,
            HttpContext httpContext,
            IAuthenticationService authenticationService,
            CancellationToken cancellationToken) =>
        {
            if (!httpContext.Request.IsHttps)
                return Results.Problem("La autenticación requiere HTTPS.", statusCode: StatusCodes.Status400BadRequest);
            var deviceId = await GetAuthorizedDeviceIdAsync(httpContext, pairingStore, cancellationToken);
            if (deviceId is null) return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrEmpty(request.Password) ||
                request.UserName.Length > 80 || request.Password.Length > 1024)
                return Results.Unauthorized();

            await PairingCredentialLock.WaitAsync(cancellationToken);
            try
            {
                var authentication = await authenticationService.SignInAsync(
                    request.UserName, request.Password, cancellationToken);
                if (!authentication.Succeeded || authentication.User is null ||
                    authentication.User.Role is not (UserRole.Administrator or UserRole.Cashier))
                    return Results.Unauthorized();

                var sessionToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                    .TrimEnd('=').Replace('+', '-').Replace('/', '_');
                var expiresAtUtc = await pairingStore.CreateUserSessionAsync(
                    deviceId.Value, authentication.User.Id.Value, sessionToken, cancellationToken);
                var validatedUserId = await pairingStore.GetSessionUserIdAsync(
                    deviceId.Value, sessionToken, cancellationToken);
                if (validatedUserId != authentication.User.Id.Value)
                {
                    app.Logger.LogError(
                        "A coordinator user session was created but failed its immediate validation for device {DeviceId}.",
                        deviceId.Value);
                    return Results.Json(
                        new ApiMessageResponse("El coordinador no pudo verificar la sesión recién creada. No se inició una sesión compartida; revisa la base central del coordinador."),
                        statusCode: StatusCodes.Status500InternalServerError);
                }

                return Results.Ok(new CreateUserSessionResponse(sessionToken, authentication.User.Id.Value,
                    authentication.User.UserName, authentication.User.Role.ToString(), expiresAtUtc));
            }
            finally
            {
                authenticationService.SignOut();
                PairingCredentialLock.Release();
            }
        }).RequireRateLimiting("device-pairing");

        app.MapPost("/api/devices/pairing-code", async (
            CreatePairingCodeRequest request,
            HttpContext httpContext,
            IAuthenticationService authenticationService,
            CancellationToken cancellationToken) =>
        {
            if (!httpContext.Request.IsHttps)
            {
                return Results.Problem(
                    "La vinculación de dispositivos requiere una conexión HTTPS.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrEmpty(request.Password))
            {
                return Results.Unauthorized();
            }

            await PairingCredentialLock.WaitAsync(cancellationToken);
            try
            {
                var authentication = await authenticationService.SignInAsync(
                    request.UserName, request.Password, cancellationToken);
                if (!authentication.Succeeded || authentication.User?.Role != UserRole.Administrator)
                {
                    return Results.Unauthorized();
                }

                var pairing = await pairingStore.CreateCodeAsync(cancellationToken);
                return Results.Ok(new CreatePairingCodeResponse(pairing.Code, pairing.ExpiresAtUtc));
            }
            finally
            {
                authenticationService.SignOut();
                PairingCredentialLock.Release();
            }
        }).RequireRateLimiting("device-pairing");

        app.MapPost("/api/devices/pair", async (
            PairDeviceRequest request,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!httpContext.Request.IsHttps)
            {
                return Results.Problem(
                    "La vinculación de dispositivos requiere una conexión HTTPS.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (request.Code is null || request.DeviceToken is null)
            {
                return Results.BadRequest(new PairDeviceResponse(false, "Código inválido o vencido."));
            }

            var result = await pairingStore.CompletePairingAsync(request.DeviceId, request.Code, request.DeviceToken, cancellationToken);
            if (result.IsAlreadyAuthorized)
            {
                return Results.Conflict(new PairDeviceResponse(false,
                    "Este dispositivo ya está vinculado. Solicita al Administrador que lo revoque antes de vincularlo de nuevo."));
            }

            return result.IsAuthorized
                ? Results.Ok(new PairDeviceResponse(true, "Dispositivo vinculado."))
                : Results.BadRequest(new PairDeviceResponse(false, "Código inválido o vencido."));
        }).RequireRateLimiting("device-pairing");

        app.MapPost("/api/devices/unlink", async (
            UnlinkDeviceRequest request,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!httpContext.Request.IsHttps)
                return Results.Problem("La desvinculación requiere una conexión HTTPS.", statusCode: StatusCodes.Status400BadRequest);
            var authorization = httpContext.Request.Headers["Authorization"].FirstOrDefault();
            var token = authorization is not null && authorization.Contains(' ')
                ? authorization[(authorization.IndexOf(' ') + 1)..].Trim() : string.Empty;
            if (!string.Equals(authorization?.Split(' ', 2).FirstOrDefault(), "Bearer", StringComparison.OrdinalIgnoreCase) ||
                !Guid.TryParse(httpContext.Request.Headers["X-Device-Id"].FirstOrDefault(), out var headerDeviceId) ||
                headerDeviceId == Guid.Empty || request.DeviceId != headerDeviceId)
                return Results.Unauthorized();
            var revoked = await pairingStore.RevokeDeviceAsync(headerDeviceId, token, cancellationToken);
            return revoked ? Results.Ok(new { success = true, message = "Dispositivo desvinculado." }) : Results.Unauthorized();
        }).RequireRateLimiting("device-pairing");

        app.MapPost("/api/sync/operations", async (
            IncomingOperation request,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!httpContext.Request.IsHttps)
                return Results.Problem("La sincronización requiere HTTPS.", statusCode: StatusCodes.Status400BadRequest);
            var deviceId = await GetAuthorizedDeviceIdAsync(httpContext, pairingStore, cancellationToken);
            if (deviceId is null)
                return Results.Json(new ApiMessageResponse("El teléfono ya no está autorizado. Vuelve a vincular este dispositivo con el coordinador de Windows."),
                    statusCode: StatusCodes.Status401Unauthorized);
            var userId = await GetAuthenticatedUserIdAsync(httpContext, pairingStore, deviceId.Value, cancellationToken);
            if (userId is null)
                return Results.Json(new ApiMessageResponse("La sesión compartida venció o ya no es válida. Cierra sesión en el teléfono e inicia sesión otra vez con el coordinador disponible."),
                    statusCode: StatusCodes.Status401Unauthorized);
            var stored = await operationStore.StoreAsync(request, deviceId.Value, userId.Value,
                businessOperationApplier, cancellationToken);
            if (!stored.Accepted)
                return stored == StoredOperationResult.Conflict
                    ? Results.Conflict(new SyncOperationResponse(false, false, null, "El identificador de operación ya existe con otro contenido."))
                    : stored.ConflictMessage is not null
                        ? Results.Conflict(new SyncOperationResponse(false, false, stored.Sequence, stored.ConflictMessage))
                        : Results.BadRequest(new SyncOperationResponse(false, false, null, "La operación contiene datos inválidos."));
            return Results.Ok(new SyncOperationResponse(true, stored.AlreadyAccepted, stored.Sequence,
                stored.AlreadyAccepted ? "La operación ya estaba confirmada." : "Operación guardada por el coordinador."));
        }).WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(1_100_000));

        app.MapPost("/api/sync/initial-import/preview", async (
            InitialDataImportSnapshot request,
            HttpContext httpContext,
            IDbContextFactory<LocalDbContext> contextFactory,
            CancellationToken cancellationToken) =>
        {
            if (!httpContext.Request.IsHttps)
                return Results.Problem("La importación requiere HTTPS.", statusCode: StatusCodes.Status400BadRequest);
            var deviceId = await GetAuthorizedDeviceIdAsync(httpContext, pairingStore, cancellationToken);
            if (deviceId is null) return Results.Unauthorized();
            var userId = await GetAuthenticatedUserIdAsync(httpContext, pairingStore, deviceId.Value, cancellationToken);
            if (userId is null) return Results.Unauthorized();
            await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
            if (!await context.UserAccounts.AnyAsync(item => item.UserId == userId.Value && item.IsEnabled &&
                    item.Role == (int)UserRole.Administrator, cancellationToken))
                return Results.Forbid();
            var result = await initialDataImportService.PreviewAsync(request, deviceId.Value, userId.Value, cancellationToken);
            return result.Succeeded
                ? Results.Ok(result.Value)
                : Results.BadRequest(new ApiMessageResponse(result.ErrorMessage ?? "No se pudo analizar el paquete."));
        }).WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(40L * 1024 * 1024));

        app.MapPost("/api/sync/initial-import/apply", async (
            InitialImportApplyRequest request,
            HttpContext httpContext,
            IDbContextFactory<LocalDbContext> contextFactory,
            CancellationToken cancellationToken) =>
        {
            if (!httpContext.Request.IsHttps)
                return Results.Problem("La importación requiere HTTPS.", statusCode: StatusCodes.Status400BadRequest);
            var deviceId = await GetAuthorizedDeviceIdAsync(httpContext, pairingStore, cancellationToken);
            if (deviceId is null) return Results.Unauthorized();
            var userId = await GetAuthenticatedUserIdAsync(httpContext, pairingStore, deviceId.Value, cancellationToken);
            if (userId is null) return Results.Unauthorized();
            await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
            if (!await context.UserAccounts.AnyAsync(item => item.UserId == userId.Value && item.IsEnabled &&
                    item.Role == (int)UserRole.Administrator, cancellationToken))
                return Results.Forbid();
            var result = await initialDataImportService.ApplyAsync(request, deviceId.Value, userId.Value, cancellationToken);
            return result.Value is not null
                ? Results.Ok(result.Value)
                : Results.Conflict(new ApiMessageResponse(result.ErrorMessage ?? "No se pudo confirmar la importación."));
        });

        app.MapPost("/api/users/cashiers", async (
            CreateCashierRequest request,
            HttpContext httpContext,
            IDbContextFactory<LocalDbContext> contextFactory,
            MauiApp1.Infrastructure.Authentication.Pbkdf2PasswordHasher passwordHasher,
            CancellationToken cancellationToken) =>
        {
            if (!httpContext.Request.IsHttps) return Results.Problem("La creación de cuentas requiere HTTPS.", statusCode: StatusCodes.Status400BadRequest);
            var deviceId = await GetAuthorizedDeviceIdAsync(httpContext, pairingStore, cancellationToken);
            if (deviceId is null) return Results.Unauthorized();
            var userId = await GetAuthenticatedUserIdAsync(httpContext, pairingStore, deviceId.Value, cancellationToken);
            if (userId is null) return Results.Unauthorized();
            await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
            var admin = await context.UserAccounts.SingleOrDefaultAsync(item => item.UserId == userId.Value && item.IsEnabled, cancellationToken);
            if (admin?.Role != (int)UserRole.Administrator) return Results.Json(new ApiMessageResponse("Solo el Administrador puede crear cuentas de Cajero."), statusCode: StatusCodes.Status403Forbidden);
            var userName = request.UserName?.Trim();
            if (string.IsNullOrWhiteSpace(userName) || userName.Length > 80 || string.IsNullOrEmpty(request.Password) || request.Password.Length is < 5 or > 1024)
                return Results.BadRequest(new ApiMessageResponse("El usuario debe tener entre 1 y 80 caracteres y la contraseña entre 5 y 1024."));
            var normalized = userName.ToUpperInvariant();
            if (await context.UserAccounts.AnyAsync(item => item.NormalizedUserName == normalized, cancellationToken))
                return Results.Conflict(new ApiMessageResponse("Ese nombre de usuario ya está registrado."));
            var (salt, hash) = await Task.Run(() => passwordHasher.Hash(request.Password), cancellationToken);
            context.UserAccounts.Add(new MauiApp1.Infrastructure.Persistence.Entities.UserAccountEntity
            {
                UserId = Guid.NewGuid(), UserName = userName, NormalizedUserName = normalized, PasswordSalt = salt,
                PasswordHash = hash, PasswordIterations = MauiApp1.Infrastructure.Authentication.Pbkdf2PasswordHasher.Iterations,
                Role = (int)UserRole.Cashier, IsEnabled = true, CreatedAtUtc = DateTime.UtcNow
            });
            try { await context.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateException) { return Results.Conflict(new ApiMessageResponse("Ese nombre de usuario ya está registrado.")); }
            return Results.Ok(new ApiMessageResponse("Cuenta de Cajero creada en el coordinador."));
        });

        app.MapGet("/api/users/cashiers", async (
            HttpContext httpContext,
            IDbContextFactory<LocalDbContext> contextFactory,
            CancellationToken cancellationToken) =>
        {
            if (!httpContext.Request.IsHttps)
                return Results.Problem("La consulta de cuentas requiere HTTPS.", statusCode: StatusCodes.Status400BadRequest);
            var deviceId = await GetAuthorizedDeviceIdAsync(httpContext, pairingStore, cancellationToken);
            if (deviceId is null) return Results.Unauthorized();
            var userId = await GetAuthenticatedUserIdAsync(httpContext, pairingStore, deviceId.Value, cancellationToken);
            if (userId is null) return Results.Unauthorized();

            await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
            var isAdministrator = await context.UserAccounts.AnyAsync(
                account => account.UserId == userId.Value && account.IsEnabled &&
                    account.Role == (int)UserRole.Administrator,
                cancellationToken);
            if (!isAdministrator) return Results.Forbid();

            var accounts = await context.UserAccounts
                .AsNoTracking()
                .Where(account => account.Role == (int)UserRole.Cashier)
                .OrderBy(account => account.NormalizedUserName)
                .Select(account => new CashierAccountSummary(
                    account.UserName,
                    account.IsEnabled,
                    account.CreatedAtUtc))
                .ToListAsync(cancellationToken);
            return Results.Ok(accounts);
        });

        app.MapGet("/api/sync/changes", async (
            long? afterSequence,
            int? limit,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!httpContext.Request.IsHttps)
                return Results.Problem("La consulta de cambios requiere HTTPS.", statusCode: StatusCodes.Status400BadRequest);
            var deviceId = await GetAuthorizedDeviceIdAsync(httpContext, pairingStore, cancellationToken);
            if (deviceId is null) return Results.Unauthorized();
            var userId = await GetAuthenticatedUserIdAsync(httpContext, pairingStore, deviceId.Value, cancellationToken);
            if (userId is null) return Results.Unauthorized();
            var cursor = afterSequence ?? 0;
            if (cursor < 0) return Results.BadRequest(new { message = "El cursor no puede ser negativo." });
            var pageSize = Math.Clamp(limit ?? 100, 1, 500);
            var changes = await operationStore.GetAfterAsync(cursor, pageSize, deviceId, cancellationToken);
            return Results.Ok(new SyncChangesResponse(changes, changes.Count > 0
                ? changes[^1].Sequence : cursor));
        });

        app.MapGet("/api/sync/conflicts", async (
            HttpContext httpContext,
            IDbContextFactory<LocalDbContext> contextFactory,
            CancellationToken cancellationToken) =>
        {
            if (!httpContext.Request.IsHttps) return Results.Problem("La consulta de conflictos requiere HTTPS.", statusCode: StatusCodes.Status400BadRequest);
            var deviceId = await GetAuthorizedDeviceIdAsync(httpContext, pairingStore, cancellationToken);
            if (deviceId is null) return Results.Unauthorized();
            var userId = await GetAuthenticatedUserIdAsync(httpContext, pairingStore, deviceId.Value, cancellationToken);
            if (userId is null) return Results.Unauthorized();
            await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
            if (!await context.UserAccounts.AnyAsync(item => item.UserId == userId.Value && item.IsEnabled && item.Role == (int)UserRole.Administrator, cancellationToken))
                return Results.Forbid();
            return Results.Ok(await operationStore.GetConflictsAsync(cancellationToken));
        });

        app.MapPost("/api/sync/conflicts/resolve", async (
            ResolveCoordinatorConflictRequest request,
            HttpContext httpContext,
            IDbContextFactory<LocalDbContext> contextFactory,
            CancellationToken cancellationToken) =>
        {
            if (!httpContext.Request.IsHttps) return Results.Problem("La resolución de conflictos requiere HTTPS.", statusCode: StatusCodes.Status400BadRequest);
            var deviceId = await GetAuthorizedDeviceIdAsync(httpContext, pairingStore, cancellationToken);
            if (deviceId is null) return Results.Unauthorized();
            var userId = await GetAuthenticatedUserIdAsync(httpContext, pairingStore, deviceId.Value, cancellationToken);
            if (userId is null) return Results.Unauthorized();
            await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
            if (!await context.UserAccounts.AnyAsync(item => item.UserId == userId.Value && item.IsEnabled && item.Role == (int)UserRole.Administrator, cancellationToken))
                return Results.Forbid();
            var error = await operationStore.ResolveConflictAsync(request.OperationId, request.ApplyIncoming, userId.Value, businessOperationApplier, cancellationToken);
            return error is null ? Results.Ok(new ApiMessageResponse("Conflicto resuelto.")) : Results.Conflict(new ApiMessageResponse(error));
        });

        await app.RunAsync();
        return 0;
    }

    private static string? ReadOption(string[] arguments, string name)
    {
        var optionIndex = Array.FindIndex(arguments, argument =>
            string.Equals(argument, name, StringComparison.OrdinalIgnoreCase));
        return optionIndex >= 0 && optionIndex + 1 < arguments.Length
            ? arguments[optionIndex + 1]
            : null;
    }

    private static bool IsValidDiscoveryChallenge(string? challenge)
    {
        if (string.IsNullOrWhiteSpace(challenge)) return false;
        try { return Convert.FromBase64String(challenge).Length == 24; }
        catch (FormatException) { return false; }
    }

    private static bool IsValidDiscoveredDeviceToken(string? token) => token is { Length: 43 } &&
        token.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private static string? ReadBearerToken(HttpContext context)
    {
        var authorization = context.Request.Headers.Authorization.FirstOrDefault();
        if (authorization is null || !authorization.Contains(' ') ||
            !string.Equals(authorization.Split(' ', 2)[0], "Bearer", StringComparison.OrdinalIgnoreCase))
            return null;
        var token = authorization[(authorization.IndexOf(' ') + 1)..].Trim();
        return IsValidDiscoveredDeviceToken(token) ? token : null;
    }

    private static async Task<Guid?> GetAuthorizedDeviceIdAsync(
        HttpContext context, CoordinatorPairingStore pairingStore, CancellationToken cancellationToken)
    {
        var authorization = context.Request.Headers["Authorization"].FirstOrDefault();
        if (!Guid.TryParse(context.Request.Headers["X-Device-Id"].FirstOrDefault(), out var deviceId) ||
            !string.Equals(authorization?.Split(' ', 2).FirstOrDefault(),
                "Bearer", StringComparison.OrdinalIgnoreCase)) return null;
        var token = authorization is not null && authorization.Contains(' ')
            ? authorization[(authorization.IndexOf(' ') + 1)..].Trim() : string.Empty;
        return await pairingStore.IsAuthorizedAsync(deviceId, token, cancellationToken) ? deviceId : null;
    }

    private static async Task<Guid?> GetAuthenticatedUserIdAsync(
        HttpContext context, CoordinatorPairingStore pairingStore, Guid deviceId, CancellationToken cancellationToken)
    {
        var sessionToken = context.Request.Headers["X-User-Session"].FirstOrDefault();
        return string.IsNullOrWhiteSpace(sessionToken)
            ? null
            : await pairingStore.GetSessionUserIdAsync(deviceId, sessionToken, cancellationToken);
    }

    private static string GetCoordinatorDataDirectory(string? configuredDataDirectory) =>
        string.IsNullOrWhiteSpace(configuredDataDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Miscelanea Ari", "Coordinator")
            : configuredDataDirectory;

    private static X509Certificate2 LoadCoordinatorCertificate(string? thumbprint, bool isDevelopment)
    {
        var normalizedThumbprint = thumbprint?.Replace(" ", string.Empty, StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(normalizedThumbprint))
        {
            throw new InvalidOperationException(
                "Falta Coordinator:CertificateThumbprint. Instala el certificado local antes de iniciar el servicio.");
        }

        var certificate = FindCertificate(StoreLocation.LocalMachine, normalizedThumbprint) ??
            (isDevelopment ? FindCertificate(StoreLocation.CurrentUser, normalizedThumbprint) : null);

        return certificate ?? throw new InvalidOperationException(
            isDevelopment
                ? "No se encontró el certificado coordinador con clave privada en LocalMachine/My ni CurrentUser/My."
                : "No se encontró el certificado coordinador con clave privada en LocalMachine/My.");
    }

    private static X509Certificate2? FindCertificate(StoreLocation location, string thumbprint)
    {
        using var store = new X509Store(StoreName.My, location);
        store.Open(OpenFlags.ReadOnly);
        return store.Certificates
            .Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false)
            .OfType<X509Certificate2>()
            .FirstOrDefault(item => item.HasPrivateKey);
    }
}

internal sealed record HealthResponse(string Status);
internal sealed record CreatePairingCodeRequest(string UserName, string Password);
internal sealed record CreatePairingCodeResponse(string Code, DateTime ExpiresAtUtc);
internal sealed record CreateUserSessionRequest(string UserName, string Password);
internal sealed record CreateUserSessionResponse(string SessionToken, Guid UserId, string UserName, string Role, DateTime ExpiresAtUtc);
internal sealed record PairDeviceRequest(Guid DeviceId, string? Code, string? DeviceToken);
internal sealed record UnlinkDeviceRequest(Guid DeviceId);
internal sealed record PairDeviceResponse(bool Success, string Message);
internal sealed record SyncOperationResponse(bool Accepted, bool AlreadyAccepted, long? Sequence, string Message);
internal sealed record SyncChangesResponse(IReadOnlyList<ConfirmedOperation> Changes, long NextSequence);
internal sealed record CreateCashierRequest(string? UserName, string? Password);
internal sealed record ResolveCoordinatorConflictRequest(Guid OperationId, bool ApplyIncoming);
internal sealed record ApiMessageResponse(string Message);
