using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
#if WINDOWS
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using Microsoft.Win32;
#endif

namespace MauiApp1.Services;

public sealed record CoordinatorLanAddress(string InterfaceName, string IpAddress, bool HasGateway)
{
    public string Address => $"https://{IpAddress}:5080/";
}

public interface ICoordinatorHostEnvironment
{
    bool IsWindows { get; }
    bool IsAndroid { get; }
    IReadOnlyList<CoordinatorLanAddress> GetLanAddresses();
    Task<string> ReadInstalledFingerprintAsync();
}

public sealed class CoordinatorHostEnvironment : ICoordinatorHostEnvironment
{
    public bool IsWindows => OperatingSystem.IsWindows();
    public bool IsAndroid => OperatingSystem.IsAndroid();

    public IReadOnlyList<CoordinatorLanAddress> GetLanAddresses() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up &&
            adapter.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211)
        .SelectMany(adapter =>
        {
            var properties = adapter.GetIPProperties();
            var gateway = OperatingSystem.IsWindows() && properties.GatewayAddresses.Any(item =>
                item.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.Any.Equals(item.Address));
            return properties.UnicastAddresses.Where(item => item.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(item => new CoordinatorLanAddress(adapter.Name, item.Address.ToString(), gateway));
        }).ToArray();

    public async Task<string> ReadInstalledFingerprintAsync()
    {
#if WINDOWS
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        using var service = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\MiscelaneaAriCoordinator");
        var imagePath = service?.GetValue("ImagePath") as string ?? string.Empty;
        var match = Regex.Match(imagePath, @"--Coordinator:CertificateThumbprint=(?:""(?<hash>[^""]+)""|(?<hash>\S+))",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success) throw new InvalidDataException("No se encontró el certificado configurado en el servicio Coordinador.");
        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly);
        var certificates = store.Certificates.Find(X509FindType.FindByThumbprint, match.Groups["hash"].Value, false);
        if (certificates.Count != 1) throw new InvalidDataException("No se encontró el certificado instalado del Coordinador.");
        string actual;
        using (var certificate = certificates[0]) actual = certificate.GetCertHashString(HashAlgorithmName.SHA256);
        var dataDirectory = Regex.Match(imagePath, @"--Coordinator:DataDirectory=(?:""(?<path>[^""]+)""|(?<path>\S+))",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!dataDirectory.Success) throw new InvalidDataException("No se encontró la carpeta configurada en el servicio Coordinador.");
        var path = Path.Combine(Path.GetFullPath(dataDirectory.Groups["path"].Value), "coordinator-certificate-fingerprint.txt");
        var recorded = (await File.ReadAllTextAsync(path)).Trim();
        if (!string.Equals(actual, recorded, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("La huella instalada no coincide con el certificado del servicio Coordinador.");
        return actual;
#else
        await Task.CompletedTask;
        throw new PlatformNotSupportedException();
#endif
    }
}

public static class CoordinatorHostAddressPolicy
{
    public static IReadOnlyList<CoordinatorLanAddress> Candidates(IEnumerable<CoordinatorLanAddress> addresses)
    {
        var valid = addresses.Where(item => IsPrivateIpv4(item.IpAddress)).ToArray();
        return valid.DistinctBy(item => item.IpAddress).OrderBy(item => item.IpAddress, StringComparer.Ordinal).ToArray();
    }

    public static bool IsCandidate(string address, IReadOnlyList<CoordinatorLanAddress> candidates) =>
        Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
        IsPrivateIpv4(uri.Host) && uri.Port == 5080 && uri.AbsolutePath == "/" && string.IsNullOrEmpty(uri.Query) &&
        string.IsNullOrEmpty(uri.Fragment) && string.IsNullOrEmpty(uri.UserInfo) &&
        candidates.Any(item => item.Address == uri.AbsoluteUri);

    private static bool IsPrivateIpv4(string value)
    {
        if (!IPAddress.TryParse(value, out var address) || address.AddressFamily != AddressFamily.InterNetwork) return false;
        var bytes = address.GetAddressBytes();
        return bytes[0] == 10 || bytes[0] == 192 && bytes[1] == 168 ||
            bytes[0] == 172 && bytes[1] is >= 16 and <= 31;
    }
}
