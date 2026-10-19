using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Text;
using Android.Content;
using Android.Net.Wifi;
using Android.Net.Nsd;
using Android.OS;
using MauiApp1.Application.Lan;

namespace MauiApp1.Platforms.Android;

internal sealed class AndroidCoordinatorDiscovery : ICoordinatorDiscovery
{
    private const string ServiceType = "_miscelanea-ari-v1._tcp";
    private static readonly ConcurrentDictionary<Guid, DiscoveryListener> ActiveDiscoveries = new();
    private static readonly ConcurrentDictionary<Guid, ServiceInfoCallbackLease> ActiveServiceInfoCallbacks = new();
    private static readonly ConcurrentDictionary<Guid, ResolveListener> ActiveResolutions = new();

    public async Task<IReadOnlyList<CoordinatorCandidate>> FindAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromSeconds(30))
            throw new ArgumentOutOfRangeException(nameof(timeout));

        var context = global::Android.App.Application.Context;
        var manager = context.GetSystemService(Context.NsdService) as NsdManager
            ?? throw new InvalidOperationException("Android no tiene disponible el descubrimiento local de servicios.");
        var wifi = context.GetSystemService(Context.WifiService) as WifiManager;
        WifiManager.MulticastLock? multicastLock = null;
        if (wifi is not null)
        {
            multicastLock = wifi.CreateMulticastLock("MiscelaneaAriCoordinatorDiscovery");
            if (multicastLock is not null)
            {
                multicastLock.SetReferenceCounted(false);
                try { multicastLock.Acquire(); }
                catch (Java.Lang.SecurityException exception)
                {
                    multicastLock.Dispose();
                    throw new UnauthorizedAccessException("Android no permitió recibir anuncios de descubrimiento local.", exception);
                }
            }
        }

        var candidates = new ConcurrentDictionary<string, CoordinatorCandidate>(StringComparer.OrdinalIgnoreCase);
        var resolutions = new ConcurrentBag<Task>();
        var discoveryStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var discoveryStopped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var listenerId = Guid.NewGuid();
        var listener = new DiscoveryListener(service =>
            resolutions.Add(ResolveAsync(manager, service, candidates, cancellationToken)), discoveryStarted,
            discoveryStopped, () => ActiveDiscoveries.TryRemove(listenerId, out _));
        ActiveDiscoveries[listenerId] = listener;

        try
        {
            try { manager.DiscoverServices(ServiceType, NsdProtocol.DnsSd, listener); }
            catch (Java.Lang.SecurityException exception)
            {
                ActiveDiscoveries.TryRemove(listenerId, out _);
                throw new UnauthorizedAccessException("Android no permitió buscar servicios en la red local.", exception);
            }
            await discoveryStarted.Task.WaitAsync(TimeSpan.FromSeconds(4), cancellationToken);
            await Task.Delay(timeout, cancellationToken);
        }
        finally
        {
            try
            {
                manager.StopServiceDiscovery(listener);
                try { await discoveryStopped.Task.WaitAsync(TimeSpan.FromSeconds(3)); }
                catch (TimeoutException) { /* Keep the listener rooted until Android sends its terminal callback. */ }
            }
            catch (Java.Lang.IllegalArgumentException)
            {
                discoveryStopped.TrySetResult(true);
                ActiveDiscoveries.TryRemove(listenerId, out _);
            }
            multicastLock?.Release();
            multicastLock?.Dispose();
            GC.KeepAlive(listener);
        }

        await Task.WhenAll(resolutions.ToArray());
        return candidates.Values
            .OrderBy(candidate => candidate.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(candidate => candidate.Address, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static async Task ResolveAsync(
        NsdManager manager,
        NsdServiceInfo discovered,
        ConcurrentDictionary<string, CoordinatorCandidate> candidates,
        CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(34))
            await ResolveWithServiceInfoCallbackAsync(manager, discovered, candidates, cancellationToken);
        else
            await ResolveWithLegacyApiAsync(manager, discovered, candidates, cancellationToken);
    }

    [SupportedOSPlatform("android34.0")]
    private static async Task ResolveWithServiceInfoCallbackAsync(
        NsdManager manager,
        NsdServiceInfo discovered,
        ConcurrentDictionary<string, CoordinatorCandidate> candidates,
        CancellationToken cancellationToken)
    {
        var resolved = new TaskCompletionSource<NsdServiceInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var unregistered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackId = Guid.NewGuid();
        var callback = new ServiceInfoListener(resolved, unregistered,
            () => ActiveServiceInfoCallbacks.TryRemove(callbackId, out _));
        var executor = Java.Util.Concurrent.Executors.NewSingleThreadExecutor()
            ?? throw new InvalidOperationException("Android no pudo crear el ejecutor de descubrimiento.");
        ActiveServiceInfoCallbacks[callbackId] = new ServiceInfoCallbackLease(callback, executor);
        try
        {
            manager.RegisterServiceInfoCallback(discovered, executor, callback);
            using var registration = cancellationToken.Register(() => resolved.TrySetCanceled(cancellationToken));
            var service = await resolved.Task.WaitAsync(TimeSpan.FromSeconds(4), cancellationToken);
            if (service is not null) AddModernCandidate(service, candidates);
        }
        catch (Exception exception) when (exception is Java.Lang.IllegalArgumentException or SocketException or
                                              ArgumentException or TimeoutException or System.OperationCanceledException)
        {
            // A service can disappear or change networks while resolution is in flight.
        }
        finally
        {
            try
            {
                manager.UnregisterServiceInfoCallback(callback);
                try { await unregistered.Task.WaitAsync(TimeSpan.FromSeconds(3)); }
                catch (TimeoutException) { /* Keep callback and executor rooted until Android confirms unregistration. */ }
            }
            catch (Java.Lang.IllegalArgumentException)
            {
                unregistered.TrySetResult(true);
                ActiveServiceInfoCallbacks.TryRemove(callbackId, out _);
            }
            if (unregistered.Task.IsCompleted)
            {
                try { executor.Shutdown(); } catch (Java.Lang.IllegalStateException) { }
            }
            GC.KeepAlive(callback);
            GC.KeepAlive(executor);
        }
    }

    [UnsupportedOSPlatform("android34.0")]
    private static async Task ResolveWithLegacyApiAsync(
        NsdManager manager,
        NsdServiceInfo discovered,
        ConcurrentDictionary<string, CoordinatorCandidate> candidates,
        CancellationToken cancellationToken)
    {
        var resolved = new TaskCompletionSource<NsdServiceInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var resolutionStopped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var listenerId = Guid.NewGuid();
        var listener = new ResolveListener(resolved, resolutionStopped,
            () => ActiveResolutions.TryRemove(listenerId, out _));
        ActiveResolutions[listenerId] = listener;
        var resolutionSubmitted = false;
        try
        {
#pragma warning disable CA1422 // Required on Android versions below API 34.
            manager.ResolveService(discovered, listener);
#pragma warning restore CA1422
            resolutionSubmitted = true;
            using var registration = cancellationToken.Register(() => resolved.TrySetCanceled(cancellationToken));
            var service = await resolved.Task.WaitAsync(TimeSpan.FromSeconds(4), cancellationToken);
            if (service is not null) AddLegacyCandidate(service, candidates);
        }
        catch (Exception exception) when (exception is Java.Lang.IllegalArgumentException or SocketException or
                                              ArgumentException or TimeoutException or System.OperationCanceledException)
        {
            // A service can disappear or change networks while resolution is in flight.
            if (!resolutionSubmitted)
            {
                resolutionStopped.TrySetResult(true);
                ActiveResolutions.TryRemove(listenerId, out _);
            }
        }
        finally
        {
            // On older Android versions the one-shot resolution callback has no stop API.
            // Keep it rooted until Android reports success or failure instead of disposing it early.
            GC.KeepAlive(listener);
        }
    }

    [SupportedOSPlatform("android34.0")]
    private static void AddModernCandidate(NsdServiceInfo service,
        ConcurrentDictionary<string, CoordinatorCandidate> candidates)
    {
        if (!TryReadServiceMetadata(service, out var coordinatorId, out var displayName)) return;
        foreach (var hostAddress in service.HostAddresses ?? [])
        {
            if (IPAddress.TryParse(hostAddress?.HostAddress, out var address) && IsPrivateIpv4(address))
            {
                candidates.TryAdd(coordinatorId, new CoordinatorCandidate(
                    coordinatorId, displayName, $"https://{address}:{service.Port}", 1));
                return;
            }
        }
    }

    [ObsoletedOSPlatform("android34.0", "Use RegisterServiceInfoCallback on Android 14 and later.")]
    private static void AddLegacyCandidate(NsdServiceInfo service,
        ConcurrentDictionary<string, CoordinatorCandidate> candidates)
    {
        if (!TryReadServiceMetadata(service, out var coordinatorId, out var displayName)) return;
#pragma warning disable CA1422 // Host is the supported resolution result on Android versions below API 34.
        var host = service.Host;
#pragma warning restore CA1422
        if (host is null || !IPAddress.TryParse(host.HostAddress, out var address) || !IsPrivateIpv4(address)) return;
        candidates.TryAdd(coordinatorId, new CoordinatorCandidate(
            coordinatorId, displayName, $"https://{address}:{service.Port}", 1));
    }

    private static bool TryReadServiceMetadata(NsdServiceInfo service, out string coordinatorId, out string displayName)
    {
        coordinatorId = string.Empty;
        displayName = string.Empty;
        if (!IsExpectedServiceType(service.ServiceType) || service.Port is <= 0 or > 65535) return false;
        var attributes = service.Attributes;
        if (attributes is null || !attributes.TryGetValue("id", out var idBytes) ||
            !attributes.TryGetValue("proto", out var protocolBytes)) return false;
        coordinatorId = Encoding.UTF8.GetString(idBytes);
        var protocol = Encoding.UTF8.GetString(protocolBytes);
        if (protocol != "1" || coordinatorId.Length != 32 || !coordinatorId.All(Uri.IsHexDigit)) return false;
        var normalizedName = NormalizeDisplayName(service.ServiceName);
        if (normalizedName is null) return false;
        displayName = normalizedName;
        return true;
    }

    private static bool IsExpectedServiceType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var normalized = value.Trim().TrimEnd('.');
        if (normalized.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[..^".local".Length];
        return string.Equals(normalized.TrimEnd('.'), ServiceType, StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeDisplayName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var name = value.Trim();
        return name.Length <= 80 && name.All(character => !char.IsControl(character)) ? name : null;
    }

    private static bool IsPrivateIpv4(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        var bytes = address.GetAddressBytes();
        return bytes[0] == 10 ||
            bytes[0] == 192 && bytes[1] == 168 ||
            bytes[0] == 172 && bytes[1] is >= 16 and <= 31;
    }

    private sealed class DiscoveryListener(Action<NsdServiceInfo> found, TaskCompletionSource<bool> discoveryStarted,
        TaskCompletionSource<bool> discoveryStopped, Action onTerminal)
        : Java.Lang.Object, NsdManager.IDiscoveryListener
    {
        public void OnDiscoveryStarted(string? serviceType) => discoveryStarted.TrySetResult(true);
        public void OnServiceFound(NsdServiceInfo? serviceInfo)
        {
            if (serviceInfo is not null) found(serviceInfo);
        }
        public void OnServiceLost(NsdServiceInfo? serviceInfo) { }
        public void OnDiscoveryStopped(string? serviceType)
        {
            discoveryStopped.TrySetResult(true);
            onTerminal();
        }
        public void OnStartDiscoveryFailed(string? serviceType, NsdFailure errorCode)
        {
            var isPermissionError = errorCode.ToString().Contains("Permission", StringComparison.OrdinalIgnoreCase);
            discoveryStarted.TrySetException(isPermissionError
                ? new UnauthorizedAccessException("Android denegó el acceso al descubrimiento de la red local.")
                : new InvalidOperationException($"Android no pudo iniciar la búsqueda local ({errorCode})."));
            discoveryStopped.TrySetResult(true);
            onTerminal();
        }
        public void OnStopDiscoveryFailed(string? serviceType, NsdFailure errorCode)
        {
            discoveryStopped.TrySetResult(true);
            onTerminal();
        }
    }

    private sealed class ResolveListener(TaskCompletionSource<NsdServiceInfo?> completion,
        TaskCompletionSource<bool> resolutionStopped, Action onTerminal)
        : Java.Lang.Object, NsdManager.IResolveListener
    {
        public void OnResolutionStopped(NsdServiceInfo? serviceInfo)
        {
            completion.TrySetResult(null);
            resolutionStopped.TrySetResult(true);
            onTerminal();
        }
        public void OnResolveFailed(NsdServiceInfo? serviceInfo, NsdFailure errorCode)
        {
            completion.TrySetResult(null);
            resolutionStopped.TrySetResult(true);
            onTerminal();
        }
        public void OnServiceResolved(NsdServiceInfo? serviceInfo)
        {
            completion.TrySetResult(serviceInfo);
            resolutionStopped.TrySetResult(true);
            onTerminal();
        }
        public void OnStopResolutionFailed(NsdServiceInfo? serviceInfo, NsdFailure errorCode) { }
    }

    [SupportedOSPlatform("android34.0")]
    private sealed class ServiceInfoListener(TaskCompletionSource<NsdServiceInfo?> completion,
        TaskCompletionSource<bool> unregistered, Action onTerminal)
        : Java.Lang.Object, NsdManager.IServiceInfoCallback
    {
        public void OnServiceInfoCallbackRegistrationFailed(NsdFailure errorCode)
        {
            completion.TrySetException(new InvalidOperationException($"Android no pudo resolver el servicio local ({errorCode})."));
            unregistered.TrySetResult(true);
            onTerminal();
        }
        public void OnServiceInfoCallbackUnregistered()
        {
            completion.TrySetResult(null);
            unregistered.TrySetResult(true);
            onTerminal();
        }
        public void OnServiceLost() => completion.TrySetResult(null);
        public void OnServiceUpdated(NsdServiceInfo? serviceInfo) => completion.TrySetResult(serviceInfo);
    }

    private sealed record ServiceInfoCallbackLease(ServiceInfoListener Listener,
        object Executor);
}
