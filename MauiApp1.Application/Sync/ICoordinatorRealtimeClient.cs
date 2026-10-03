namespace MauiApp1.Application.Sync;

public enum CoordinatorRealtimeState
{
    NotConfigured,
    Connecting,
    Connected,
    Reconnecting,
    Disconnected,
    SessionUnavailable
}

public sealed record CoordinatorChangeNotice(long Sequence, Guid OperationId);

public interface ICoordinatorRealtimeClient
{
    CoordinatorRealtimeState State { get; }
    event Action<CoordinatorRealtimeState>? StateChanged;

    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}

public interface ILocalDataChangeNotifier
{
    event Action? DataChanged;
    void NotifyDataChanged();
}

public interface ILocalCoordinatorChangeMonitor
{
    void Start();
}
