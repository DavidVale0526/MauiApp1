using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using MauiApp1.Application.Authentication;
using MauiApp1.Application.Categories;
using MauiApp1.Application.Lan;
using MauiApp1.Application.Products;
using MauiApp1.Application.Storage;
using MauiApp1.Application.Sync;
using MauiApp1.Coordinator.Pairing;
using MauiApp1.Coordinator.Sync;
using MauiApp1.Domain.Users;
using MauiApp1.Domain.Values;
using MauiApp1.Infrastructure.Authentication;
using MauiApp1.Infrastructure.Lan;
using MauiApp1.Infrastructure.Persistence;
using MauiApp1.Infrastructure.Sync;
using Microsoft.Data.Sqlite;

// Isolated SQLite files and an ephemeral HTTPS server. No installed service,
// application data, credentials, or certificate store are changed.
var directory = Path.Combine(Path.GetTempPath(), "MiscelaneaSyncVerification", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
var password = Guid.NewGuid().ToString("N");
await using var windows = await Runtime.CreateAsync(Path.Combine(directory, "central.db"));
var admin = await windows.Auth.CreateInitialAdministratorAsync("Admin", password);
Require(admin.Succeeded, "central administrator creation");
var cashier1 = await windows.Auth.CreateCashierAccountAsync("Michaell", password);
var cashier2 = await windows.Auth.CreateCashierAccountAsync("David", password);
Require(cashier1.Succeeded && cashier2.Succeeded, "central cashier creation");
var store = new CoordinatorOperationStore(windows.Path);
await store.InitializeAsync();
var pairing = new CoordinatorPairingStore(windows.Path);
await pairing.InitializeAsync();

using var rsa = RSA.Create(2048);
var request = new CertificateRequest("CN=SyncVerification", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
var san = new SubjectAlternativeNameBuilder();
san.AddIpAddress(IPAddress.Loopback);
request.CertificateExtensions.Add(san.Build());
using var generatedCertificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
// Reload the key for Windows Schannel, which cannot serve the transient RSA handle.
using var certificate = X509CertificateLoader.LoadPkcs12(generatedCertificate.Export(X509ContentType.Pfx), null);
var builder = WebApplication.CreateBuilder();
builder.Logging.ClearProviders();
builder.Logging.AddConsole().SetMinimumLevel(LogLevel.Warning);
builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(certificate)));
builder.Services.AddSignalR();
builder.Services.AddSingleton(pairing);
builder.Services.AddSingleton<CoordinatorConnectionRegistry>();
builder.Services.AddHostedService(services => new CoordinatorChangeBroadcaster(windows.Path,
    services.GetRequiredService<CoordinatorConnectionRegistry>(), pairing,
    services.GetRequiredService<Microsoft.AspNetCore.SignalR.IHubContext<CoordinatorChangesHub>>(),
    services.GetRequiredService<ILogger<CoordinatorChangeBroadcaster>>()));
await using var server = builder.Build();
server.MapHub<CoordinatorChangesHub>("/hubs/changes");
server.MapGet("/api/sync/changes", async (long? afterSequence, int? limit, HttpContext http) =>
{
    var identity = await AuthorizeAsync(http, pairing);
    if (identity is null) return Results.Unauthorized();
    var cursor = afterSequence ?? 0;
    var rows = await store.GetAfterAsync(cursor, Math.Clamp(limit ?? 100, 1, 500), identity.Value.Device);
    return Results.Ok(new CoordinatorChangesPage(rows.Select(row => new CoordinatorChange(row.Sequence,
        row.OperationId, row.DeviceId, row.ActorUserId, row.ActorUserName, row.ActorRole,
        row.OperationType, row.PayloadJson, row.CreatedAtUtc, row.ConfirmedAtUtc)).ToArray(),
        rows.Count > 0 ? rows[^1].Sequence : cursor));
});
server.MapPost("/api/sync/operations", async (IncomingOperation operation, HttpContext http) =>
{
    var identity = await AuthorizeAsync(http, pairing);
    if (identity is null) return Results.Unauthorized();
    var result = await store.StoreAsync(operation, identity.Value.Device, identity.Value.User, new CoordinatorBusinessOperationApplier());
    var body = new { accepted = result.Accepted, alreadyAccepted = result.AlreadyAccepted, sequence = result.Sequence, message = result.ConflictMessage };
    return result.Accepted ? Results.Ok(body) : Results.Conflict(body);
});
await server.StartAsync();
var address = server.Urls.Single().TrimEnd('/') + "/";
var fingerprint = certificate.GetCertHashString(HashAlgorithmName.SHA256);
await using var phone1 = await Runtime.CreateAsync(Path.Combine(directory, "michaell.db"));
await using var phone2 = await Runtime.CreateAsync(Path.Combine(directory, "david.db"));
await ConnectPhoneAsync(phone1, cashier1.User!.Id.Value, "Michaell");
await ConnectPhoneAsync(phone2, cashier2.User!.Id.Value, "David");

for (var index = 0; index < 105; index++)
    Require((await windows.Categories.CreateAsync($"Initial {index:D3}")).Succeeded, "initial category creation");
await windows.Sync.SynchronizeAsync();
var journal = await store.GetAfterAsync(0, 500, null);
Require(journal.Count == 105 && journal.All(row => row.ActorRole == (int)UserRole.Administrator),
    $"uppercase EF IDs must join lowercase journal IDs without duplicate operations (got {journal.Count} rows)");
await using (var database = new SqliteConnection($"Data Source={windows.Path}"))
{
    await database.OpenAsync();
    await using var insert = database.CreateCommand();
    // Reproduce a duplicate left by older coordinator builds, preserving its data.
    insert.CommandText = """
        INSERT INTO CoordinatorOperations
            (OperationId, DeviceId, ActorUserId, OperationType, PayloadJson, PayloadHash, CreatedAtUtc, ReceivedAtUtc, Status)
        SELECT upper(OperationId), DeviceId, ActorUserId, OperationType, PayloadJson, PayloadHash, CreatedAtUtc, ReceivedAtUtc, Status
        FROM CoordinatorOperations ORDER BY Sequence LIMIT 1;
        """;
    await insert.ExecuteNonQueryAsync();
}
Require((await store.GetAfterAsync(0, 500, null)).Count == 105, "historical GUID casing duplicates must be excluded from replay");
await phone1.Sync.SynchronizeAsync();
await phone2.Sync.SynchronizeAsync();
Require((await phone1.Categories.GetAllAsync()).Count == 105 && (await phone2.Categories.GetAllAsync()).Count == 105,
    $"one sync pass must drain more than one page: first={(await phone1.Categories.GetAllAsync()).Count}, second={(await phone2.Categories.GetAllAsync()).Count}; first status={(await phone1.Sync.GetStatusAsync()).LatestConflictMessage}; second status={(await phone2.Sync.GetStatusAsync()).LatestConflictMessage}");
await phone1.Sync.SynchronizeAsync();
Require((await phone1.Categories.GetAllAsync()).Count == 105, "repeated downloads must remain idempotent");
Console.WriteLine("PASS: 105 initial categories downloaded by both cashier accounts; authors and repeated downloads verified.");

await phone1.Realtime.StartAsync();
await phone2.Realtime.StartAsync();
await WaitAsync(() => Task.FromResult(phone1.Realtime.State == CoordinatorRealtimeState.Connected &&
    phone2.Realtime.State == CoordinatorRealtimeState.Connected), "both real SignalR clients connected");
await windows.Worker.StartAsync();
var elapsed = Stopwatch.StartNew();
Require((await windows.Categories.CreateAsync("Windows live")).Succeeded, "Windows live category creation");
await WaitAsync(async () => await HasCategory(phone1, "Windows live") && await HasCategory(phone2, "Windows live"),
    "SignalR category delivery to both phones without polling workers or manual sync");
Console.WriteLine($"PASS: automatic Windows publication + SignalR delivery to both phones in {elapsed.ElapsedMilliseconds} ms.");

var category = (await windows.Categories.GetAllAsync()).Single(item => item.Name == "Windows live");
Require((await windows.Categories.RenameAsync(category.Id.Value, "Windows renamed")).Succeeded, "central rename");
await WaitAsync(async () => await HasCategory(phone1, "Windows renamed") && await HasCategory(phone2, "Windows renamed"), "live category rename");
Require((await windows.Products.CreateAsync(new ProductDraft("Product live", new MoneyAmount(2000), "libra", category.Id, new Quantity(1000)))).Succeeded,
    "central product creation");
await WaitAsync(async () => (await phone1.Products.GetAllAsync()).Any(item => item.Name == "Product live") &&
    (await phone2.Products.GetAllAsync()).Any(item => item.Name == "Product live"), "live product creation");
var product = (await windows.Products.GetAllAsync()).Single(item => item.Name == "Product live");
Require((await windows.Products.UpdateAsync(product.Id.Value,
    new ProductDraft("Product updated", new MoneyAmount(2500), "libra", category.Id, new Quantity(2000)))).Succeeded, "central product edit");
await WaitAsync(async () => (await phone1.Products.GetAllAsync()).Any(item => item.Name == "Product updated") &&
    (await phone2.Products.GetAllAsync()).Any(item => item.Name == "Product updated"), "live product edit");
Console.WriteLine("PASS: category rename, product creation, and product edit received through SignalR on both phones.");

await phone1.Worker.StartAsync();
await WaitAsync(() => Task.FromResult(phone1.Realtime.State == CoordinatorRealtimeState.Connected), "phone1 worker reconnect");
Require((await phone1.Categories.CreateAsync("Michaell live")).Succeeded, "phone1 creation");
await WaitAsync(async () => await HasCategory(windows, "Michaell live") && await HasCategory(phone2, "Michaell live"), "automatic phone1 send and live phone2 receive");
await phone2.Worker.StartAsync();
await WaitAsync(() => Task.FromResult(phone2.Realtime.State == CoordinatorRealtimeState.Connected), "phone2 worker reconnect");
Require((await phone2.Categories.CreateAsync("David live")).Succeeded, "phone2 creation");
await WaitAsync(async () => await HasCategory(windows, "David live") && await HasCategory(phone1, "David live"), "automatic phone2 send and live phone1 receive");
Require(phone1.DataChangedCount > 0 && phone2.DataChangedCount > 0, "receivers must notify open views");
Console.WriteLine("PASS: both cashier accounts upload without leaving the screen; the other phone receives changes.");
await windows.Worker.StopAsync();
await phone1.Worker.StopAsync();
await phone2.Worker.StopAsync();
Require((await phone1.Categories.CreateAsync("Resume recovery")).Succeeded, "local save with worker stopped");
Require(!await HasCategory(windows, "Resume recovery"), "stopped worker must not send");
await phone1.Worker.StartAsync();
await phone2.Worker.StartAsync();
await WaitAsync(async () => await HasCategory(windows, "Resume recovery") && await HasCategory(phone2, "Resume recovery"), "resume recovery");
Console.WriteLine("PASS: local changes preserved while stopped and recovered after resume.");
await windows.Worker.StopAsync();
await phone1.Worker.StopAsync();
await phone2.Worker.StopAsync();
await server.StopAsync();
Console.WriteLine("All isolated three-device checks passed.");

async Task ConnectPhoneAsync(Runtime phone, Guid userId, string name)
{
    Require((await phone.Auth.CreateInitialAdministratorAsync(name, password)).Succeeded, "phone local account setup");
    Require((await phone.Auth.SignInWithCoordinatorUserAsync(userId, name, UserRole.Cashier, password)).Succeeded, "local-central account association");
    var deviceId = await phone.Services.GetRequiredService<ILocalDeviceIdentity>().GetDeviceIdAsync();
    var token = Secret();
    var code = await pairing.CreateCodeAsync();
    Require((await pairing.CompletePairingAsync(deviceId, code.Code, token)).IsAuthorized, "device authorization");
    var sessionToken = Secret();
    var expires = await pairing.CreateUserSessionAsync(deviceId, userId, sessionToken);
    await phone.Link.SaveAsync(new CoordinatorDeviceLink { DeviceId = deviceId, Address = address,
        CertificateFingerprint = fingerprint, DeviceToken = token, IsConfirmed = true });
    await phone.Session.SaveAsync(new CoordinatorUserSession(phone.Auth.CurrentUser!.Id.Value, userId, name, UserRole.Cashier, sessionToken, expires));
}

static string Secret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
static void Require(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException("FAILED: " + description);
}
static async Task WaitAsync(Func<Task<bool>> predicate, string description)
{
    var elapsed = Stopwatch.StartNew();
    while (elapsed.Elapsed < TimeSpan.FromSeconds(10))
    {
        if (await predicate()) return;
        await Task.Delay(100);
    }
    throw new TimeoutException("FAILED: " + description);
}
static async Task<bool> HasCategory(Runtime runtime, string name) => (await runtime.Categories.GetAllAsync()).Any(item => item.Name == name);
static async Task<(Guid Device, Guid User)?> AuthorizeAsync(HttpContext context, CoordinatorPairingStore pairing)
{
    if (!context.Request.IsHttps || !Guid.TryParse(context.Request.Headers["X-Device-Id"], out var device)) return null;
    var token = context.Request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.Ordinal);
    if (!await pairing.IsAuthorizedAsync(device, token)) return null;
    var user = await pairing.GetSessionUserIdAsync(device, context.Request.Headers["X-User-Session"].ToString());
    return user is null ? null : (device, user.Value);
}

sealed class Runtime(ServiceProvider services, string path, MemoryLink link, MemorySession session) : IAsyncDisposable
{
    public ServiceProvider Services => services;
    public string Path => path;
    public MemoryLink Link => link;
    public MemorySession Session => session;
    public IAuthenticationService Auth => services.GetRequiredService<IAuthenticationService>();
    public ICategoryService Categories => services.GetRequiredService<ICategoryService>();
    public IProductService Products => services.GetRequiredService<IProductService>();
    public ILocalOperationSyncService Sync => services.GetRequiredService<ILocalOperationSyncService>();
    public ICoordinatorRealtimeClient Realtime => services.GetRequiredService<ICoordinatorRealtimeClient>();
    public LocalOperationSyncWorker Worker => services.GetRequiredService<LocalOperationSyncWorker>();
    public int DataChangedCount;
    public static async Task<Runtime> CreateAsync(string path)
    {
        var link = new MemoryLink();
        var session = new MemorySession();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<ICoordinatorLinkStorage>(link);
        services.AddSingleton<ICoordinatorUserSessionStorage>(session);
        services.AddSingleton<CoordinatorPairingClient>();
        services.AddSingleton<ICoordinatorAuthenticationClient>(p => p.GetRequiredService<CoordinatorPairingClient>());
        services.AddSingleton<ICoordinatorChangeClient>(p => p.GetRequiredService<CoordinatorPairingClient>());
        services.AddSingleton<ICoordinatedSignInService, CoordinatedSignInService>();
        services.AddSingleton<ICoordinatorOperationSender, CoordinatorOperationSender>();
        services.AddSingleton<ICoordinatorRealtimeClient, CoordinatorRealtimeClient>();
        services.AddSingleton<LocalOperationSyncWorker>();
        services.AddLocalPersistence(path);
        var runtime = new Runtime(services.BuildServiceProvider(), path, link, session);
        await runtime.Services.GetRequiredService<ILocalDatabaseInitializer>().InitializeAsync();
        runtime.Services.GetRequiredService<ILocalDataChangeNotifier>().DataChanged += () => Interlocked.Increment(ref runtime.DataChangedCount);
        return runtime;
    }
    public ValueTask DisposeAsync() => services.DisposeAsync();
}
sealed class MemoryLink : ICoordinatorLinkStorage
{
    private CoordinatorDeviceLink? _value;
    public Task<CoordinatorDeviceLink?> ReadAsync() => Task.FromResult(_value);
    public Task SaveAsync(CoordinatorDeviceLink value) { _value = value; return Task.CompletedTask; }
    public Task RemoveAsync() { _value = null; return Task.CompletedTask; }
}
sealed class MemorySession : ICoordinatorUserSessionStorage
{
    private CoordinatorUserSession? _value;
    public Task<CoordinatorUserSession?> ReadAsync() => Task.FromResult(_value);
    public Task SaveAsync(CoordinatorUserSession value) { _value = value; return Task.CompletedTask; }
    public Task RemoveAsync() { _value = null; return Task.CompletedTask; }
}
