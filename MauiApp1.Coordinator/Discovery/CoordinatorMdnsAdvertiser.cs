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
    // El servicio arranca con Windows antes de que el adaptador Wi-Fi tenga dirección,
    // así que el anuncio no puede depender de una única comprobación al inicio.
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RecheckInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan NetworkSettleDelay = TimeSpan.FromSeconds(1);

    private readonly object _sync = new();
    private readonly SemaphoreSlim _wakeUp = new(0, 1);

    private MdnsAdvertiser? _advertiser;
    private string? _advertisedKey;
    private string? _coordinatorId;
    private string? _lastFailureMessage;
    private bool _identityFailureLogged;
    private bool _waitingForNetworkLogged;
    private volatile bool _networkChanged;
    private volatile bool _stopping;

    private CancellationTokenRegistration _startedRegistration;
    private CancellationTokenSource? _loopCancellation;
    private Task? _loopTask;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _startedRegistration = applicationLifetime.ApplicationStarted.Register(OnApplicationStarted);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _stopping = true;
        NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
        _startedRegistration.Dispose();
        _loopCancellation?.Cancel();

        if (_loopTask is not null)
        {
            try
            {
                await _loopTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
            catch (TimeoutException)
            {
            }
        }

        lock (_sync)
        {
            DisposeAdvertiser();
        }
    }

    private void OnApplicationStarted()
    {
        if (_stopping) return;

        NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
        _loopCancellation = new CancellationTokenSource();
        var token = _loopCancellation.Token;
        _loopTask = Task.Run(() => RunAsync(token), CancellationToken.None);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var endpoint = GetHttpsEndpoint();
        if (endpoint is null)
        {
            // El perfil de Development puede usar HTTP en loopback; ese modo no se anuncia.
            return;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var advertised = TryAdvertise(endpoint);
                if (advertised)
                {
                    var changed = await _wakeUp.WaitAsync(RecheckInterval, cancellationToken);
                    if (changed)
                    {
                        // Da tiempo a que Windows termine de aplicar el cambio de red.
                        await Task.Delay(NetworkSettleDelay, cancellationToken);
                    }
                }
                else
                {
                    await Task.Delay(RetryInterval, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Falló la actualización del anuncio mDNS; se reintentará.");
                try
                {
                    await Task.Delay(RetryInterval, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private bool TryAdvertise(Uri endpoint)
    {
        lock (_sync)
        {
            if (_stopping) return false;

            var networkChanged = _networkChanged;
            _networkChanged = false;

            IPAddress address;
            try
            {
                address = ResolveLocalIpv4Address();
            }
            catch (InvalidOperationException)
            {
                // Todavía no hay red con una IPv4 utilizable: se reintenta hasta que aparezca.
                if (!_waitingForNetworkLogged)
                {
                    _waitingForNetworkLogged = true;
                    logger.LogInformation(
                        "El coordinador aún no tiene una red IPv4 disponible; se anunciará por DNS-SD en cuanto se conecte.");
                }

                return false;
            }

            var key = $"{address}:{endpoint.Port}";
            if (!networkChanged && _advertiser is not null && _advertisedKey == key)
            {
                return true;
            }

            string coordinatorId;
            try
            {
                coordinatorId = GetCoordinatorId();
            }
            catch (Exception exception) when (exception is CryptographicException or IOException or
                                                  UnauthorizedAccessException or InvalidOperationException)
            {
                DisposeAdvertiser();
                if (!_identityFailureLogged)
                {
                    _identityFailureLogged = true;
                    logger.LogWarning(exception,
                        "No se pudo anunciar el coordinador por mDNS; la vinculación por QR y manual sigue disponible.");
                }

                return false;
            }

            // La dirección o el certificado cambiaron, o la red se reconectó: se recrea el anuncio.
            DisposeAdvertiser();

            try
            {
                var displayName = $"Miscelánea Ari {coordinatorId[..8]}";
                var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["id"] = coordinatorId,
                    ["proto"] = "1"
                };
                var profile = new ServiceProfile(displayName, "_miscelanea-ari-v1._tcp",
                    checked((ushort)endpoint.Port), properties);
                var advertiser = new MdnsAdvertiser(profile, address);
                advertiser.Start();
                _advertiser = advertiser;
                _advertisedKey = key;
                _lastFailureMessage = null;
                logger.LogInformation(
                    "Se anunció el coordinador Miscelánea Ari mediante DNS-SD en {Address}:{Port}.",
                    address, endpoint.Port);
                return true;
            }
            catch (Exception exception) when (exception is CryptographicException or IOException or
                                                  UnauthorizedAccessException or ArgumentException or SocketException or
                                                  InvalidOperationException)
            {
                DisposeAdvertiser();
                if (_lastFailureMessage != exception.Message)
                {
                    _lastFailureMessage = exception.Message;
                    logger.LogWarning(exception,
                        "No se pudo anunciar el coordinador por mDNS; la vinculación por QR y manual sigue disponible.");
                }

                return false;
            }
        }
    }

    private void OnNetworkAddressChanged(object? sender, EventArgs eventArgs)
    {
        _networkChanged = true;
        try
        {
            _wakeUp.Release();
        }
        catch (SemaphoreFullException)
        {
            // Ya hay un cambio pendiente por procesar.
        }
    }

    private void DisposeAdvertiser()
    {
        _advertiser?.Dispose();
        _advertiser = null;
        _advertisedKey = null;
    }

    private Uri? GetHttpsEndpoint()
    {
        return Uri.TryCreate(configuration["Coordinator:Url"], UriKind.Absolute, out var endpoint) &&
               endpoint.Scheme == Uri.UriSchemeHttps
            ? endpoint
            : null;
    }

    private string GetCoordinatorId()
    {
        if (_coordinatorId is not null) return _coordinatorId;

        using var certificate = LoadCertificate(configuration["Coordinator:CertificateThumbprint"]);
        _coordinatorId = Convert.ToHexString(SHA256.HashData(certificate.RawData).AsSpan(0, 16));
        return _coordinatorId;
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
