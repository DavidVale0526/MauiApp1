using MauiApp1.Application.Lan;
using MauiApp1.Domain.Users;
using Microsoft.Data.Sqlite;

namespace MauiApp1.Infrastructure.Lan;

/// <summary>Stores coordinator-side device presence and the last processed sync cursor.</summary>
public sealed class CoordinatorDevicePresenceStore(string centralDatabasePath)
{
    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = centralDatabasePath,
        Mode = SqliteOpenMode.ReadWrite,
        Cache = SqliteCacheMode.Shared,
        Pooling = false
    }.ToString();

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS CoordinatorDevicePresence (
                DeviceId TEXT NOT NULL PRIMARY KEY,
                DeviceName TEXT NULL,
                LastUserId TEXT NULL,
                IsOnline INTEGER NOT NULL DEFAULT 0 CHECK (IsOnline IN (0, 1)),
                LastSeenAtUtc INTEGER NULL,
                LastSyncAtUtc INTEGER NULL,
                LastProcessedSequence INTEGER NOT NULL DEFAULT 0
            );
            UPDATE CoordinatorDevicePresence SET IsOnline = 0;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public Task MarkConnectedAsync(Guid deviceId, Guid userId, string? deviceName,
        CancellationToken cancellationToken = default) =>
        UpsertPresenceAsync(deviceId, userId, deviceName, isOnline: true, cancellationToken);

    public async Task MarkDisconnectedAsync(Guid deviceId, CancellationToken cancellationToken = default)
    {
        if (deviceId == Guid.Empty) return;
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE CoordinatorDevicePresence
            SET IsOnline = 0, LastSeenAtUtc = $now
            WHERE DeviceId = $device;
            """;
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.Ticks);
        command.Parameters.AddWithValue("$device", deviceId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RecordSyncProgressAsync(Guid deviceId, Guid userId, string? deviceName,
        long processedSequence, CancellationToken cancellationToken = default)
    {
        if (deviceId == Guid.Empty || userId == Guid.Empty || processedSequence < 0) return;
        var name = NormalizeDeviceName(deviceName);
        var now = DateTime.UtcNow.Ticks;
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO CoordinatorDevicePresence
                (DeviceId, DeviceName, LastUserId, IsOnline, LastSeenAtUtc, LastSyncAtUtc, LastProcessedSequence)
            VALUES ($device, $name, $user, 0, $now, $now, $sequence)
            ON CONFLICT(DeviceId) DO UPDATE SET
                DeviceName = COALESCE(excluded.DeviceName, CoordinatorDevicePresence.DeviceName),
                LastUserId = excluded.LastUserId,
                LastSeenAtUtc = excluded.LastSeenAtUtc,
                LastSyncAtUtc = CASE
                    WHEN excluded.LastProcessedSequence >= CoordinatorDevicePresence.LastProcessedSequence
                    THEN excluded.LastSyncAtUtc ELSE CoordinatorDevicePresence.LastSyncAtUtc END,
                LastProcessedSequence = MAX(CoordinatorDevicePresence.LastProcessedSequence, excluded.LastProcessedSequence);
            """;
        command.Parameters.AddWithValue("$device", deviceId.ToString("D"));
        command.Parameters.AddWithValue("$name", (object?)name ?? DBNull.Value);
        command.Parameters.AddWithValue("$user", userId.ToString("D"));
        command.Parameters.AddWithValue("$now", now);
        command.Parameters.AddWithValue("$sequence", processedSequence);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CoordinatorDeviceSummary>> GetDevicesAsync(
        CancellationToken cancellationToken = default)
    {
        var users = await ReadUsersAsync(cancellationToken);
        var sessions = await ReadLatestActiveSessionsAsync(cancellationToken);
        var devices = new List<CoordinatorDeviceSummary>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT a.DeviceId, a.AuthorizedAtUtc,
                   p.DeviceName, p.IsOnline, p.LastSeenAtUtc, p.LastSyncAtUtc,
                   COALESCE(p.LastProcessedSequence, 0), p.LastUserId,
                   COALESCE((SELECT MAX(o.Sequence) FROM CoordinatorOperations o
                             WHERE o.Status = 1 AND o.DeviceId <> a.DeviceId COLLATE NOCASE), 0),
                   (SELECT COUNT(*) FROM CoordinatorOperations c
                    WHERE c.DeviceId = a.DeviceId COLLATE NOCASE AND c.Status = 2)
            FROM AuthorizedDevices a
            LEFT JOIN CoordinatorDevicePresence p ON p.DeviceId = a.DeviceId COLLATE NOCASE
            WHERE a.IsRevoked = 0
            ORDER BY a.AuthorizedAtUtc DESC;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!Guid.TryParse(reader.GetString(0), out var deviceId)) continue;
            var authorizedAt = new DateTime(reader.GetInt64(1), DateTimeKind.Utc);
            var deviceName = reader.IsDBNull(2) ? null : reader.GetString(2);
            var isOnline = !reader.IsDBNull(3) && reader.GetInt64(3) == 1;
            var lastSeen = ReadUtcDate(reader, 4);
            var lastSync = ReadUtcDate(reader, 5);
            var processed = reader.GetInt64(6);
            var userId = ReadGuid(reader, 7);
            if (userId is null && sessions.TryGetValue(deviceId, out var sessionUserId)) userId = sessionUserId;
            var latest = reader.GetInt64(8);
            var conflictCount = checked((int)reader.GetInt64(9));

            (string Name, string Role) user = default;
            var hasUser = userId is { } resolvedUserId && users.TryGetValue(resolvedUserId, out user);
            devices.Add(new CoordinatorDeviceSummary(deviceId, deviceName, hasUser ? user.Name : null, hasUser ? user.Role : null,
                isOnline, authorizedAt, lastSeen, lastSync, processed, latest, conflictCount));
        }

        return devices;
    }

    private async Task<Dictionary<Guid, (string Name, string Role)>> ReadUsersAsync(CancellationToken ct)
    {
        var users = new Dictionary<Guid, (string Name, string Role)>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT UserId, UserName, Role, IsEnabled FROM UserAccounts;";
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            if (reader.GetInt64(3) != 1 || !Guid.TryParse(reader.GetString(0), out var userId)) continue;
            var role = Enum.IsDefined(typeof(UserRole), reader.GetInt32(2))
                ? ((UserRole)reader.GetInt32(2)).ToString()
                : "Usuario";
            users[userId] = (reader.GetString(1), role);
        }
        return users;
    }

    private async Task<Dictionary<Guid, Guid>> ReadLatestActiveSessionsAsync(CancellationToken ct)
    {
        var sessions = new Dictionary<Guid, Guid>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT DeviceId, UserId FROM CoordinatorUserSessions
            WHERE ExpiresAtUtc > $now AND RevokedAtUtc IS NULL
            ORDER BY CreatedAtUtc DESC;
            """;
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.Ticks);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            if (Guid.TryParse(reader.GetString(0), out var deviceId) &&
                Guid.TryParse(reader.GetString(1), out var userId))
                sessions.TryAdd(deviceId, userId);
        }
        return sessions;
    }

    private async Task UpsertPresenceAsync(Guid deviceId, Guid userId, string? deviceName,
        bool isOnline, CancellationToken ct)
    {
        if (deviceId == Guid.Empty || userId == Guid.Empty) return;
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO CoordinatorDevicePresence (DeviceId, DeviceName, LastUserId, IsOnline, LastSeenAtUtc)
            VALUES ($device, $name, $user, $online, $now)
            ON CONFLICT(DeviceId) DO UPDATE SET
                DeviceName = COALESCE(excluded.DeviceName, CoordinatorDevicePresence.DeviceName),
                LastUserId = excluded.LastUserId,
                IsOnline = excluded.IsOnline,
                LastSeenAtUtc = excluded.LastSeenAtUtc;
            """;
        command.Parameters.AddWithValue("$device", deviceId.ToString("D"));
        command.Parameters.AddWithValue("$name", (object?)NormalizeDeviceName(deviceName) ?? DBNull.Value);
        command.Parameters.AddWithValue("$user", userId.ToString("D"));
        command.Parameters.AddWithValue("$online", isOnline ? 1 : 0);
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.Ticks);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static string? NormalizeDeviceName(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) || normalized.Length > 80 || normalized.Any(char.IsControl)
            ? null
            : normalized;
    }

    private static DateTime? ReadUtcDate(SqliteDataReader reader, int index) =>
        reader.IsDBNull(index) ? null : new DateTime(reader.GetInt64(index), DateTimeKind.Utc);

    private static Guid? ReadGuid(SqliteDataReader reader, int index) =>
        reader.IsDBNull(index) || !Guid.TryParse(reader.GetString(index), out var id) ? null : id;
}
