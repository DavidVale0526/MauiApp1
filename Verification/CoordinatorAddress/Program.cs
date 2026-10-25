using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using MauiApp1.Application.Authentication;
using MauiApp1.Application.Lan;
using MauiApp1.Application.Storage;
using MauiApp1.Infrastructure.Lan;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

// All credentials and storage here are synthetic; no real coordinator or device is used.
using var key = RSA.Create(2048);
var request = new CertificateRequest("CN=CoordinatorAddressTest", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
var names = new SubjectAlternativeNameBuilder();
names.AddIpAddress(IPAddress.Loopback);
names.AddDnsName("localhost");
request.CertificateExtensions.Add(names.Build());
using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
// Import into a temporary Windows key container usable by SChannel; not a certificate-store installation.
using var certificate = X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pfx), null);
var fingerprint = certificate.GetCertHashString(HashAlgorithmName.SHA256);
var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(https =>
{
    https.ServerCertificate = certificate;
    https.SslProtocols = System.Security.Authentication.SslProtocols.Tls12;
})));
await using var server = builder.Build();
var mode = "ok";
var redirectedRequests = 0;
server.MapGet("/health", (HttpContext context) =>
{
    if (mode == "redirect") return Results.Redirect("/redirected");
    if (mode == "broken") return Results.Text("invalid-json", "application/json");
    if (mode == "error") return Results.StatusCode(503);
    return Results.Json(new { status = mode });
});
server.MapGet("/redirected", () => { redirectedRequests++; return Results.Json(new { status = "ok" }); });
await server.StartAsync();
var address = server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
var deviceId = Guid.NewGuid();
var tests = 0;

CoordinatorDeviceLink Link(string? hash = null, bool confirmed = true) => new()
{
    DeviceId = deviceId, Address = "https://192.168.1.8:5080/", CertificateFingerprint = hash ?? fingerprint,
    DeviceToken = new string('x', 43), IsConfirmed = confirmed, PendingCode = "synthetic-retained-value"
};
(CoordinatorPairingClient Client, LinkStore Store, SessionStore Sessions) Setup(CoordinatorDeviceLink? link)
{
    var store = new LinkStore { Link = link };
    var sessions = new SessionStore();
    return (new CoordinatorPairingClient(store, sessions, new DeviceIdentity(deviceId)), store, sessions);
}
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAILED: " + name);
    Console.WriteLine("PASS: " + name);
    tests++;
}
void Untouched(LinkStore store, CoordinatorDeviceLink original, string name) =>
    Check(ReferenceEquals(store.Link, original) && store.Writes == 0 && store.Removes == 0, name);

foreach (var invalid in new[] { "", "http://192.168.1.38:5080", "https://example.org", "https://192.168.1.38/path", "https://user:pass@192.168.1.38", "https://192.168.1.38?test=1" })
{
    var original = Link(); var test = Setup(original);
    Check(!(await test.Client.VerifyCoordinatorAddressAsync(invalid)).Succeeded, "invalid address rejected");
    Untouched(test.Store, original, "invalid address preserves link");
}
foreach (var link in new CoordinatorDeviceLink?[] { null, Link(confirmed: false) })
{
    var test = Setup(link);
    Check(!(await test.Client.VerifyCoordinatorAddressAsync(address)).Succeeded && test.Store.Writes == 0,
        "requires confirmed link");
}
{
    var original = Link(new string('A', 64)); var test = Setup(original);
    Check(!(await test.Client.VerifyCoordinatorAddressAsync(address)).Succeeded, "different TLS certificate rejected");
    Untouched(test.Store, original, "certificate mismatch preserves link");
}
foreach (var response in new[] { "redirect", "broken", "error", "not-ok" })
{
    mode = response;
    var original = Link(); var test = Setup(original);
    Check(!(await test.Client.VerifyCoordinatorAddressAsync(address)).Succeeded, response + " rejected");
    Untouched(test.Store, original, response + " preserves link");
}
Check(redirectedRequests == 0, "redirects never followed");
mode = "ok";
{
    var original = Link(); var test = Setup(original);
    var verification = await test.Client.VerifyCoordinatorAddressAsync(address);
    Check(verification.Succeeded, "saved certificate accepted");
    Untouched(test.Store, original, "verification alone / cancellation does not save");
    Check(!(await test.Client.UpdateCoordinatorAddressAsync(Guid.NewGuid())).Succeeded, "unverified save rejected");
    Untouched(test.Store, original, "invalid confirmation preserves link");
}
{
    var original = Link(); var test = Setup(original);
    var verification = await test.Client.VerifyCoordinatorAddressAsync(address);
    Check((await test.Client.UpdateCoordinatorAddressAsync(verification.VerificationId!.Value)).Succeeded, "confirmed update saved");
    var updated = test.Store.Link!;
    Check(updated.Address == new Uri(address).AbsoluteUri && updated.DeviceId == original.DeviceId &&
        updated.DeviceToken == original.DeviceToken && updated.CertificateFingerprint == original.CertificateFingerprint &&
        updated.IsConfirmed == original.IsConfirmed && updated.PendingCode == original.PendingCode &&
        test.Store.Writes == 1 && test.Store.Removes == 0 && test.Sessions.Calls == 0,
        "only Address changes; credentials and session untouched");
    Check(!(await test.Client.UpdateCoordinatorAddressAsync(verification.VerificationId.Value)).Succeeded && test.Store.Writes == 1,
        "confirmation cannot be reused");
}
{
    var original = Link(); var test = Setup(original);
    var verification = await test.Client.VerifyCoordinatorAddressAsync(address);
    test.Store.Link = Link(confirmed: false);
    Check(!(await test.Client.UpdateCoordinatorAddressAsync(verification.VerificationId!.Value)).Succeeded && test.Store.Writes == 0,
        "changed link cannot be overwritten");
}
{
    var original = Link(); var test = Setup(original);
    var verification = await test.Client.VerifyCoordinatorAddressAsync(address);
    mode = "error";
    Check(!(await test.Client.UpdateCoordinatorAddressAsync(verification.VerificationId!.Value)).Succeeded,
        "health checked again after confirmation");
    Untouched(test.Store, original, "failed recheck preserves link");
    mode = "ok";
}
{
    var original = Link(); var test = Setup(original);
    var verification = await test.Client.VerifyCoordinatorAddressAsync(address);
    test.Store.FailWrite = true;
    Check(!(await test.Client.UpdateCoordinatorAddressAsync(verification.VerificationId!.Value)).Succeeded, "storage error reported");
    Untouched(test.Store, original, "failed storage preserves link");
}
await server.StopAsync();
{
    var original = Link(); var test = Setup(original);
    Check(!(await test.Client.VerifyCoordinatorAddressAsync(address)).Succeeded, "network failure reported");
    Untouched(test.Store, original, "network failure preserves link");
}
Console.WriteLine($"{tests} checks passed. No real storage, device or coordinator accessed.");

sealed class LinkStore : ICoordinatorLinkStorage
{
    public CoordinatorDeviceLink? Link;
    public int Writes, Removes;
    public bool FailWrite;
    public Task<CoordinatorDeviceLink?> ReadAsync() => Task.FromResult(Link);
    public Task SaveAsync(CoordinatorDeviceLink link)
    {
        if (FailWrite) throw new IOException("Simulated storage failure");
        Writes++; Link = link; return Task.CompletedTask;
    }
    public Task RemoveAsync() { Removes++; throw new Exception("Remove must never be called"); }
}
sealed class DeviceIdentity(Guid id) : ILocalDeviceIdentity
{
    public Task<Guid> GetDeviceIdAsync(CancellationToken cancellationToken = default) => Task.FromResult(id);
}
sealed class SessionStore : ICoordinatorUserSessionStorage
{
    public int Calls;
    public Task<CoordinatorUserSession?> ReadAsync() { Calls++; throw new Exception("Session must not be accessed"); }
    public Task SaveAsync(CoordinatorUserSession session) { Calls++; throw new Exception("Session must not change"); }
    public Task RemoveAsync() { Calls++; throw new Exception("Session must not change"); }
}
