using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Haukcode.Mdns;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MauiApp1.Coordinator.Discovery;

internal sealed class CoordinatorMdnsAdvertiser(
    IConfiguration configuration,
    IHostApplicationLifetime applicationLifetime,
    ILogger<CoordinatorMdnsAdvertiser> logger) : IHostedService
{
    private MdnsAdvertiser? _advertiser;
    private CancellationTokenRegistration _startedRegistration;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _startedRegistration = applicationLifetime.ApplicationStarted.Register(StartAdvertising);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _startedRegistration.Dispose();
        _advertiser?.Dispose();
        _advertiser = null;
        return Task.CompletedTask;
    }

    private void StartAdvertising()
    {
        if (!Uri.TryCreate(configuration["Coordinator:Url"], UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != Uri.UriSchemeHttps)
            return;

        try
        {
            using var certificate = LoadCertificate(configuration["Coordinator:CertificateThumbprint"]);
            var coordinatorId = Convert.ToHexString(SHA256.HashData(certificate.RawData).AsSpan(0, 16));
            var displayName = $"Miscelánea Ari {coordinatorId[..8]}";
            var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = coordinatorId,
                ["proto"] = "1"
            };
            var profile = new ServiceProfile(displayName, "_miscelanea-ari-v1._tcp",
                checked((ushort)endpoint.Port), properties);
            var localAddress = ResolveLocalIpv4Address();
            _advertiser = new MdnsAdvertiser(profile, localAddress);
            _advertiser.Start();
            logger.LogInformation(
                "Se anunció el coordinador Miscelánea Ari mediante DNS-SD en {Address}:{Port}.",
                localAddress, endpoint.Port);
        }
        catch (Exception exception) when (exception is CryptographicException or IOException or
                                              UnauthorizedAccessException or ArgumentException or SocketException or InvalidOperationException)
        {
            _advertiser?.Dispose();
            _advertiser = null;
            logger.LogWarning(exception, "No se pudo anunciar el coordinador por mDNS; la vinculación por QR y manual sigue disponible.");
        }
    }

    private static IPAddress ResolveLocalIpv4Address()
    {
        // Let Windows choose the interface used for link-local multicast. This also works
        // when NetworkInterface enumeration returns adapters the mDNS library cannot rank.
        using (var routeProbe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
        {
            try
            {
                routeProbe.Connect(new IPEndPoint(IPAddress.Parse("224.0.0.251"), 5353));
                if (routeProbe.LocalEndPoint is IPEndPoint { Address: var address } && IsUsableIpv4(address))
                    return address;
            }
            catch (SocketException)
            {
                // Fall back to an active physical adapter if there is no multicast route.
            }
        }

        var candidates = NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up &&
                              (adapter.NetworkInterfaceType is NetworkInterfaceType.Ethernet or
                                  NetworkInterfaceType.Wireless80211) &&
                              adapter.SupportsMulticast)
            .SelectMany(adapter =>
            {
                var hasIpv4Gateway = adapter.GetIPProperties().GatewayAddresses
                    .Any(gateway => gateway.Address.AddressFamily == AddressFamily.InterNetwork &&
                                    !gateway.Address.Equals(IPAddress.Any));
                return adapter.GetIPProperties().UnicastAddresses
                    .Select(unicast => new { unicast.Address, HasIpv4Gateway = hasIpv4Gateway });
            })
            .Where(candidate => IsUsableIpv4(candidate.Address))
            .OrderByDescending(candidate => candidate.HasIpv4Gateway)
            .Select(candidate => candidate.Address)
            .FirstOrDefault();

        return candidates ?? throw new InvalidOperationException(
            "No se encontró una dirección IPv4 activa para anunciar el coordinador en la red local.");
    }

    private static bool IsUsableIpv4(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address))
            return false;

        var bytes = address.GetAddressBytes();
        return bytes[0] != 0 && bytes[0] != 169;
    }

    private static X509Certificate2 LoadCertificate(string? thumbprint)
    {
        if (string.IsNullOrWhiteSpace(thumbprint))
            throw new CryptographicException("Falta el certificado TLS configurado para el coordinador.");

        var normalized = new string(thumbprint.Where(Uri.IsHexDigit).ToArray());
        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly);
        var certificates = store.Certificates.Find(X509FindType.FindByThumbprint, normalized, validOnly: false);
        if (certificates.Count == 0)
            throw new CryptographicException("No se encontró el certificado TLS configurado para el coordinador.");
        return new X509Certificate2(certificates[0]);
    }
}
