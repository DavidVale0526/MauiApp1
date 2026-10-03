using System.Collections.Concurrent;
using MauiApp1.Coordinator.Pairing;
using MauiApp1.Application.Sync;
using Microsoft.AspNetCore.SignalR;

namespace MauiApp1.Coordinator.Sync;

internal sealed record CoordinatorAuthorizedConnection(
    string ConnectionId, Guid DeviceId, Guid UserId, string DeviceToken, string UserSessionToken);

internal sealed class CoordinatorConnectionRegistry
{
    private readonly ConcurrentDictionary<string, CoordinatorAuthorizedConnection> _connections = new();

    public void Add(CoordinatorAuthorizedConnection connection) => _connections[connection.ConnectionId] = connection;
    public void Remove(string connectionId) => _connections.TryRemove(connectionId, out _);
    public IReadOnlyList<CoordinatorAuthorizedConnection> Snapshot() => _connections.Values.ToArray();
}

internal sealed class CoordinatorChangesHub(
    CoordinatorPairingStore pairingStore,
    CoordinatorConnectionRegistry connections) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var request = Context.GetHttpContext()?.Request;
        var authorization = request?.Headers.Authorization.FirstOrDefault();
        var deviceToken = authorization is not null &&
            string.Equals(authorization.Split(' ', 2).FirstOrDefault(), "Bearer", StringComparison.OrdinalIgnoreCase) &&
            authorization.Contains(' ')
                ? authorization[(authorization.IndexOf(' ') + 1)..].Trim()
                : string.Empty;
        var sessionToken = request?.Headers["X-User-Session"].FirstOrDefault();

        if (request is null || !request.IsHttps ||
            !Guid.TryParse(request.Headers["X-Device-Id"].FirstOrDefault(), out var deviceId) ||
            deviceId == Guid.Empty || string.IsNullOrWhiteSpace(deviceToken) ||
            string.IsNullOrWhiteSpace(sessionToken) ||
            !await pairingStore.IsAuthorizedAsync(deviceId, deviceToken, Context.ConnectionAborted))
        {
            Context.Abort();
            return;
        }

        var userId = await pairingStore.GetSessionUserIdAsync(deviceId, sessionToken, Context.ConnectionAborted);
        if (userId is null)
        {
            Context.Abort();
            return;
        }

        connections.Add(new CoordinatorAuthorizedConnection(
            Context.ConnectionId, deviceId, userId.Value, deviceToken, sessionToken));
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        connections.Remove(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}

internal sealed class CoordinatorChangeBroadcaster(
    string centralDatabasePath,
    CoordinatorConnectionRegistry connections,
    CoordinatorPairingStore pairingStore,
    IHubContext<CoordinatorChangesHub> hub,
    ILogger<CoordinatorChangeBroadcaster> logger) : BackgroundService
{
    private readonly string _connectionString = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
    {
        DataSource = centralDatabasePath,
        Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly,
        Cache = Microsoft.Data.Sqlite.SqliteCacheMode.Shared
    }.ToString();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var database = new Microsoft.Data.Sqlite.SqliteConnection(_connectionString);
            await database.OpenAsync(stoppingToken);
            var lastSequence = await ReadLatestSequenceAsync(database, stoppingToken);
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(400));

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    var changes = await ReadChangesAsync(database, lastSequence, stoppingToken);
                    foreach (var change in changes)
                    {
                        if (!change.IsConfirmed)
                        {
                            lastSequence = change.Sequence;
                            continue;
                        }

                        var notice = new CoordinatorChangeNotice(change.Sequence, change.OperationId);
                        var delivered = true;
                        foreach (var connection in connections.Snapshot())
                        {
                            try
                            {
                                var isAuthorized = await pairingStore.IsAuthorizedAsync(
                                    connection.DeviceId, connection.DeviceToken, stoppingToken);
                                var currentUserId = isAuthorized
                                    ? await pairingStore.GetSessionUserIdAsync(
                                        connection.DeviceId, connection.UserSessionToken, stoppingToken)
                                    : null;
                                if (!isAuthorized || currentUserId != connection.UserId)
                                {
                                    await hub.Clients.Client(connection.ConnectionId)
                                        .SendAsync("AuthorizationRevoked", cancellationToken: stoppingToken);
                                    connections.Remove(connection.ConnectionId);
                                    continue;
                                }

                                await hub.Clients.Client(connection.ConnectionId)
                                    .SendAsync("ChangeAvailable", notice, stoppingToken);
                            }
                            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                            catch (Exception exception)
                            {
                                delivered = false;
                                logger.LogDebug(exception, "No se pudo enviar un aviso de cambio en vivo al dispositivo {DeviceId}.", connection.DeviceId);
                            }
                        }

                        if (!delivered) break;
                        lastSequence = change.Sequence;
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                catch (Exception exception)
                {
                    logger.LogDebug(exception, "Se reintentará leer el diario central para enviar avisos en vivo.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            logger.LogError(exception, "El publicador de cambios en vivo no pudo seguir consultando el diario central.");
        }
    }

    private static async Task<long> ReadLatestSequenceAsync(
        Microsoft.Data.Sqlite.SqliteConnection database, CancellationToken cancellationToken)
    {
        await using var command = database.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(Sequence), 0) FROM CoordinatorOperations;";
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<IReadOnlyList<ChangeRow>> ReadChangesAsync(
        Microsoft.Data.Sqlite.SqliteConnection database, long afterSequence, CancellationToken cancellationToken)
    {
        await using var command = database.CreateCommand();
        command.CommandText = "SELECT Sequence, OperationId, Status FROM CoordinatorOperations WHERE Sequence > $after ORDER BY Sequence LIMIT 100;";
        command.Parameters.AddWithValue("$after", afterSequence);
        var changes = new List<ChangeRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            changes.Add(new ChangeRow(reader.GetInt64(0), Guid.Parse(reader.GetString(1)), reader.GetInt32(2) == 1));
        return changes;
    }

    private sealed record ChangeRow(long Sequence, Guid OperationId, bool IsConfirmed);
}
