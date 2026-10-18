using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
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
            _advertiser = new MdnsAdvertiser(profile);
            _advertiser.Start();
            logger.LogInformation("Se anunció el coordinador Miscelánea Ari mediante DNS-SD en el puerto {Port}.", endpoint.Port);
        }
        catch (Exception exception) when (exception is CryptographicException or IOException or
                                              UnauthorizedAccessException or ArgumentException or SocketException or InvalidOperationException)
        {
            _advertiser?.Dispose();
            _advertiser = null;
            logger.LogWarning(exception, "No se pudo anunciar el coordinador por mDNS; la vinculación por QR y manual sigue disponible.");
        }
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
