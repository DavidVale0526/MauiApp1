using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Haukcode.Mdns;
using MauiApp1.Application.Lan;

namespace MauiApp1.Infrastructure.Lan;

public sealed class WindowsCoordinatorDiscovery : ICoordinatorDiscovery
{
    public const string ServiceType = "_miscelanea-ari-v1._tcp";

    public async Task<IReadOnlyList<CoordinatorCandidate>> FindAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromSeconds(30))
            throw new ArgumentOutOfRangeException(nameof(timeout), "El tiempo de búsqueda debe estar entre 1 y 30 segundos.");

        var candidates = new ConcurrentDictionary<string, CoordinatorCandidate>(StringComparer.OrdinalIgnoreCase);
        var resolutions = new ConcurrentBag<Task>();
        using var browser = new MdnsBrowser(ServiceType);
        browser.ServiceFound += service => resolutions.Add(ResolveAsync(service, candidates, cancellationToken));
        browser.Start();

        try
        {
            await Task.Delay(timeout, cancellationToken);
        }
        finally
        {
            browser.Dispose();
        }

        await Task.WhenAll(resolutions.ToArray());
        return candidates.Values
            .OrderBy(candidate => candidate.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(candidate => candidate.Address, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static async Task ResolveAsync(
        ServiceProfile service,
        ConcurrentDictionary<string, CoordinatorCandidate> candidates,
        CancellationToken cancellationToken)
    {
        if (service.Port == 0 || service.Properties is null ||
            !service.Properties.TryGetValue("proto", out var protocol) || protocol != "1" ||
            !service.Properties.TryGetValue("id", out var coordinatorId) || !IsCoordinatorId(coordinatorId))
            return;

        var displayName = NormalizeDisplayName(service.InstanceName);
        if (displayName is null) return;

        IReadOnlyList<IPAddress>? addresses = service.Addresses;
        if (addresses is null || addresses.Count == 0)
        {
            try
            {
                var hostName = service.Hostname.TrimEnd('.');
                addresses = await Dns.GetHostAddressesAsync(hostName, cancellationToken);
            }
            catch (Exception exception) when (exception is SocketException or ArgumentException or OperationCanceledException)
            {
                return;
            }
        }

        foreach (var address in addresses.Where(IsPrivateIpv4).Distinct())
        {
            var candidate = new CoordinatorCandidate(
                coordinatorId,
                displayName,
                $"https://{address}:{service.Port}",
                1);
            candidates.TryAdd(coordinatorId, candidate);
        }
    }

    private static string? NormalizeDisplayName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var name = value.Trim();
        return name.Length <= 80 && name.All(character => !char.IsControl(character)) ? name : null;
    }

    private static bool IsCoordinatorId(string? value) => value is { Length: 32 } && value.All(Uri.IsHexDigit);

    private static bool IsPrivateIpv4(IPAddress address)
    {
        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false;
        var bytes = address.GetAddressBytes();
        return bytes[0] == 10 ||
            bytes[0] == 192 && bytes[1] == 168 ||
            bytes[0] == 172 && bytes[1] is >= 16 and <= 31;
    }
}
