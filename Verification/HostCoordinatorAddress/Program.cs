using System.Reflection;
using System.Text.Json;
using System.Windows.Input;
using MauiApp1.Application.Authentication;
using MauiApp1.Application.Lan;
using MauiApp1.Configuration;
using MauiApp1.Domain.Users;
using MauiApp1.Services;

// No real database, network, certificate store, SecureStorage or pairing code is used.
var assertions = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception("FAILED: " + message);
    assertions++;
    Console.WriteLine("PASS: " + message);
}
var hostMode = (DesktopCoordinatorModeOptions)Activator.CreateInstance(typeof(DesktopCoordinatorModeOptions),
    BindingFlags.Instance | BindingFlags.NonPublic, null,
    new object?[] { DesktopCoordinatorMode.Host, "unused", null }, null)!;
T Fake<T>(Func<MethodInfo, object?[], object?>? call = null) where T : class
{
    var proxy = DispatchProxy.Create<T, ServiceProxy>();
    ((ServiceProxy)(object)proxy).Call = call ?? ((_, _) => throw new Exception("Unexpected dependency access"));
    return proxy;
}
async Task ExecuteAsync(DevicePairingViewModel vm, ICommand command)
{
    command.Execute(null);
    for (var i = 0; vm.IsBusy && i < 100; i++) await Task.Delay(10);
    Check(!vm.IsBusy, "command completes");
}
CoordinatorLanAddress Lan(string ip, string name = "Wi-Fi", bool gateway = true) => new(name, ip, gateway);
var fingerprint = new string('A', 64);

DevicePairingViewModel Create(FakeEnvironment env, PairingSpy spy, bool host = true)
{
    var auth = Fake<IAuthenticationService>((method, _) => method.Name == "get_CurrentUser"
        ? new AuthenticatedUser(new(Guid.NewGuid()), "synthetic-admin", UserRole.Administrator)
        : throw new Exception("Unexpected authentication mutation"));
    return new(spy, Fake<ICoordinatorDiscovery>(), Fake<ICoordinatorDiscoveryPairingClient>(),
        Fake<ICoordinatorDiscoveryApprovalClient>(), auth, host ? hostMode : DesktopCoordinatorModeOptions.LocalClient, env);
}

foreach (var confirmed in new[] { false, true })
{
    var env = new FakeEnvironment(); var spy = new PairingSpy(confirmed); var vm = Create(env, spy);
    await vm.LoadAsync();
    Check(vm.Address == "https://192.168.1.38:5080/" && vm.Fingerprint == fingerprint,
        "Host uses current LAN and installed certificate, ignoring old client link");
    Check(spy.StatusReads == 0 && !vm.ShowPairForm && !vm.CanUnlink && !vm.ShowAddressUpdate && !vm.CanEditConnection,
        "Host never reads legacy link or exposes client/security-edit actions");
    await ExecuteAsync(vm, vm.PairCommand);
    Check(!(await vm.UnlinkAsync()).Succeeded && !vm.ApplyScannedQr("{}") && spy.PairCalls == 0,
        "Host cannot link itself, unlink legacy authorization or apply a client QR");
    await ExecuteAsync(vm, vm.GenerateCodeCommand);
    using var qr = JsonDocument.Parse(vm.GeneratedQrPayload);
    Check(spy.CodeCalls == 1 && spy.LastAddress == vm.Address && qr.RootElement.GetProperty("Address").GetString() == vm.Address &&
        qr.RootElement.GetProperty("Fingerprint").GetString() == vm.Fingerprint, "visible endpoint, request and QR agree");
    env.Addresses = [Lan("192.168.1.39")];
    await vm.LoadAsync();
    Check(vm.Address.Contains("192.168.1.39") && vm.GeneratedQrPayload == string.Empty, "reopening refreshes IP and clears obsolete QR");
}
{
    var env = new FakeEnvironment(); var spy = new PairingSpy(); var vm = Create(env, spy);
    await vm.LoadAsync();
    env.Addresses = [Lan("192.168.1.39")];
    await ExecuteAsync(vm, vm.GenerateCodeCommand);
    Check(spy.CodeCalls == 0 && vm.ShowHostAddressConfirmation && vm.Address.Contains("192.168.1.39"),
        "IP change immediately before request blocks code and updates visible endpoint");
    vm.ConfirmHostAddressCommand.Execute(null);
    await ExecuteAsync(vm, vm.GenerateCodeCommand);
    Check(spy.CodeCalls == 1 && spy.LastAddress == "https://192.168.1.39:5080/", "confirmed new IP used on next request");
    vm.Address = "https://192.168.1.38:5080/";
    Check(vm.GeneratedQrPayload == string.Empty && !vm.GenerateCodeCommand.CanExecute(null), "manual address edit invalidates previous QR and confirmation");
}
{
    var env = new FakeEnvironment(); var spy = new PairingSpy(); var vm = Create(env, spy);
    await vm.LoadAsync();
    spy.DuringCodeRequest = () => env.Addresses = [Lan("192.168.1.39")];
    await ExecuteAsync(vm, vm.GenerateCodeCommand);
    Check(spy.CodeCalls == 1 && vm.GeneratedQrPayload == string.Empty && vm.GeneratedCode == string.Empty && vm.ShowHostAddressConfirmation,
        "IP change during simulated request never exposes an obsolete QR or code");
}
{
    var env = new FakeEnvironment { Addresses = [Lan("192.168.1.38"), Lan("10.0.0.4", "Ethernet", false)] };
    var spy = new PairingSpy(); var vm = Create(env, spy);
    await vm.LoadAsync();
    Check(vm.Address == string.Empty && vm.ShowHostAddressConfirmation && !vm.GenerateCodeCommand.CanExecute(null),
        "multiple LAN interfaces require explicit choice, even if only one has a gateway");
    foreach (var invalid in new[] { "https://0.0.0.0:5080", "https://127.0.0.1:5080", "https://192.168.1.8:5080", "http://192.168.1.38:5080", "https://192.168.1.38:5080/path", "https://192.168.1.38:5080?x=1", "https://user@192.168.1.38:5080" })
    {
        vm.Address = invalid; vm.ConfirmHostAddressCommand.Execute(null);
        Check(!vm.GenerateCodeCommand.CanExecute(null), "invalid or nonlocal advertised address rejected: " + invalid);
    }
    vm.Address = "https://10.0.0.4:5080"; vm.ConfirmHostAddressCommand.Execute(null);
    await ExecuteAsync(vm, vm.GenerateCodeCommand);
    Check(spy.LastAddress == "https://10.0.0.4:5080/", "explicit selection of second LAN retained after revalidation");
    env.Addresses = [Lan("192.168.1.38"), Lan("10.0.0.4", "Ethernet"), Lan("172.16.2.3", "LAN 3")];
    await ExecuteAsync(vm, vm.GenerateCodeCommand);
    Check(spy.CodeCalls == 1 && vm.ShowHostAddressConfirmation, "new ambiguity requires renewed confirmation");
}
{
    var env = new FakeEnvironment(); var spy = new PairingSpy(); var vm = Create(env, spy);
    await vm.LoadAsync();
    env.Addresses = [];
    await ExecuteAsync(vm, vm.GenerateCodeCommand);
    Check(spy.CodeCalls == 0 && vm.Address == string.Empty && vm.ShowHostAddressConfirmation, "network loss blocks generation");
    env.Addresses = [Lan("192.168.1.40")];
    vm.ConfirmHostAddressCommand.Execute(null);
    Check(vm.Address == "https://192.168.1.40:5080/" && vm.GenerateCodeCommand.CanExecute(null),
        "confirmation after network recovery detects the unique LAN without reopening the screen");
}
foreach (var addresses in new[] { Array.Empty<CoordinatorLanAddress>(), new[] { Lan("127.0.0.1"), Lan("0.0.0.0"), Lan("169.254.1.2"), Lan("8.8.8.8"), Lan("::1") } })
{
    var env = new FakeEnvironment { Addresses = addresses }; var spy = new PairingSpy(); var vm = Create(env, spy);
    await vm.LoadAsync();
    Check(vm.Address == string.Empty && vm.HostAddressMessage.Contains("No se detectó") && !vm.GenerateCodeCommand.CanExecute(null),
        "no valid LAN never falls back to arbitrary, wildcard, loopback or public IP");
}
{
    var env = new FakeEnvironment { FailNetwork = true }; var vm = Create(env, new PairingSpy());
    await vm.LoadAsync();
    Check(!vm.GenerateCodeCommand.CanExecute(null) && vm.HostAddressMessage.Length > 0, "network enumeration error blocks generation");
}
foreach (var badFingerprint in new[] { "", "invalid", new string('Z', 64) })
{
    var env = new FakeEnvironment { Fingerprint = badFingerprint }; var vm = Create(env, new PairingSpy());
    await vm.LoadAsync();
    Check(!vm.GenerateCodeCommand.CanExecute(null), "invalid installed fingerprint blocks generation");
}
{
    var env = new FakeEnvironment(); var spy = new PairingSpy(); var vm = Create(env, spy);
    await vm.LoadAsync(); env.FailCertificate = true;
    await ExecuteAsync(vm, vm.GenerateCodeCommand);
    Check(spy.CodeCalls == 0 && vm.GeneratedQrPayload.Length == 0, "certificate verification failure before request blocks generation");
}
foreach (var windows in new[] { false, true })
foreach (var confirmed in new[] { false, true })
{
    var env = new FakeEnvironment { IsWindows = windows, FailNetwork = true, FailCertificate = true };
    var spy = new PairingSpy(confirmed); var vm = Create(env, spy, false);
    await vm.LoadAsync();
    Check(vm.Address == "https://192.168.1.8:5080/" && vm.Fingerprint == new string('B', 64) &&
        vm.ShowPairForm == !confirmed && vm.CanUnlink == confirmed && vm.ShowAddressUpdate == confirmed,
        (windows ? "Windows Client" : "Android Client") + " preserves existing saved-link behavior");
    Check(env.NetworkReads == 0 && env.CertificateReads == 0 && spy.StatusReads == 1,
        "clients never access Host network or certificate provider");
    Check(vm.CanGenerateCode == windows && vm.CanScanQr == (!windows && !confirmed), "client platform permissions preserved");
    if (!confirmed)
    {
        await ExecuteAsync(vm, vm.PairCommand);
        Check(spy.PairCalls == 1, "client pairing remains available");
    }
}
{
    var env = new FakeEnvironment(); var spy = new PairingSpy(); var vm = Create(env, spy);
    await vm.LoadAsync();
    spy.DuringCodeRequest = () =>
    {
        env.Addresses = [Lan("192.168.1.39")];
        vm.LoadAsync().GetAwaiter().GetResult();
    };
    await ExecuteAsync(vm, vm.GenerateCodeCommand);
    Check(vm.GeneratedQrPayload == string.Empty && vm.ShowHostAddressConfirmation,
        "concurrent reload cannot publish a QR different from the requested endpoint");
}
{
    var env = new FakeEnvironment(); var spy = new PairingSpy(); var vm = Create(env, spy);
    await vm.LoadAsync(); env.Fingerprint = new string('C', 64);
    await ExecuteAsync(vm, vm.GenerateCodeCommand);
    Check(spy.CodeCalls == 0 && vm.ShowHostAddressConfirmation && vm.Fingerprint == new string('C', 64),
        "installed fingerprint change also requires explicit confirmation without generating a code");
}
Console.WriteLine($"SUCCESS: {assertions} assertions; all dependencies simulated; no real codes or storage changes.");

public class ServiceProxy : DispatchProxy
{
    public Func<MethodInfo, object?[], object?> Call { get; set; } = null!;
    protected override object? Invoke(MethodInfo? method, object?[]? args) => Call(method!, args ?? []);
}
sealed class FakeEnvironment : ICoordinatorHostEnvironment
{
    public bool IsWindows { get; set; } = true;
    public bool IsAndroid => !IsWindows;
    public IReadOnlyList<CoordinatorLanAddress> Addresses { get; set; } = [new("Wi-Fi", "192.168.1.38", true)];
    public string Fingerprint { get; set; } = new('A', 64);
    public bool FailNetwork { get; set; }
    public bool FailCertificate { get; set; }
    public int NetworkReads { get; private set; }
    public int CertificateReads { get; private set; }
    public IReadOnlyList<CoordinatorLanAddress> GetLanAddresses()
    {
        NetworkReads++;
        if (FailNetwork) throw new IOException("Synthetic network failure");
        return Addresses;
    }
    public Task<string> ReadInstalledFingerprintAsync()
    {
        CertificateReads++;
        if (FailCertificate) throw new IOException("Synthetic certificate failure");
        return Task.FromResult(Fingerprint);
    }
}
sealed class PairingSpy(bool confirmed = false) : ICoordinatorPairingClient
{
    public int StatusReads, CodeCalls, PairCalls;
    public string LastAddress = string.Empty;
    public Action? DuringCodeRequest;
    public Task<CoordinatorLinkStatus?> GetStatusAsync()
    {
        StatusReads++;
        return Task.FromResult<CoordinatorLinkStatus?>(new("https://192.168.1.8:5080/", new('B', 64), confirmed, null));
    }
    public Task<PairingCodeResult> CreateCodeAsync(string address, string fingerprint, string userName, string password)
    {
        CodeCalls++; LastAddress = address; DuringCodeRequest?.Invoke();
        return Task.FromResult(new PairingCodeResult(true, "Synthetic response", "12345678", DateTime.UtcNow.AddMinutes(5)));
    }
    public Task<PairingActionResult> PairAsync(string address, string fingerprint, string code)
    {
        PairCalls++; return Task.FromResult(new PairingActionResult(true, "Synthetic pairing"));
    }
    public Task<PairingActionResult> UnlinkAsync() => throw new Exception("Unexpected unlink");
    public Task<CoordinatorAvailability> CheckAvailabilityAsync() => throw new Exception("Unexpected real health check");
    public Task<CoordinatorAddressVerification> VerifyCoordinatorAddressAsync(string address) => throw new Exception("Unexpected link update");
    public Task<PairingActionResult> UpdateCoordinatorAddressAsync(Guid id) => throw new Exception("Unexpected credential update");
}
