using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;
using System.Threading.RateLimiting;
using MauiApp1.Application.Authentication;
using MauiApp1.Application.Storage;
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
        if (!Uri.TryCreate(endpointUrl, UriKind.Absolute, out var endpoint))
        {
            throw new InvalidOperationException("Coordinator:Url debe ser una URL absoluta.");
        }

        if (endpoint.Scheme == Uri.UriSchemeHttps)
        {
            var certificate = LoadCoordinatorCertificate(
                builder.Configuration["Coordinator:CertificateThumbprint"], isDevelopment);
            builder.WebHost.ConfigureKestrel(options => options.ListenAnyIP(endpoint.Port,
                listenOptions => listenOptions.UseHttps(certificate)));
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
        var operationStore = new CoordinatorOperationStore(centralDatabasePath);
        var businessOperationApplier = new CoordinatorBusinessOperationApplier();
        await operationStore.InitializeAsync();

        // This liveness endpoint exposes no account, inventory, or transaction data.
        app.MapGet("/health", () => TypedResults.Ok(new HealthResponse("ok")));
        app.MapHub<CoordinatorChangesHub>("/hubs/changes");

        app.UseRateLimiter();
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
