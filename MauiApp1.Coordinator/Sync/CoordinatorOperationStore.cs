using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace MauiApp1.Coordinator.Sync;

internal sealed class CoordinatorOperationStore(string databasePath)
{
    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = databasePath,
        Mode = SqliteOpenMode.ReadWrite,
        Pooling = false
    }.ToString();

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS CoordinatorOperations (
                Sequence INTEGER PRIMARY KEY AUTOINCREMENT,
                OperationId TEXT NOT NULL UNIQUE,
                DeviceId TEXT NOT NULL,
                ActorUserId TEXT NOT NULL,
                OperationType TEXT NOT NULL,
                PayloadJson TEXT NOT NULL,
                PayloadHash BLOB NOT NULL,
                CreatedAtUtc INTEGER NOT NULL,
                ReceivedAtUtc INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_CoordinatorOperations_Sequence_Device
                ON CoordinatorOperations (Sequence, DeviceId);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<StoredOperationResult> StoreAsync(
        IncomingOperation operation, Guid authenticatedDeviceId, CancellationToken cancellationToken = default)
    {
        if (operation.OperationId == Guid.Empty || operation.ActorUserId == Guid.Empty ||
            operation.DeviceId != authenticatedDeviceId || string.IsNullOrWhiteSpace(operation.OperationType) ||
            operation.OperationType.Length > 120 || string.IsNullOrWhiteSpace(operation.PayloadJson) ||
            operation.PayloadJson.Length > 1_000_000 || operation.CreatedAtUtc.Kind != DateTimeKind.Utc)
            return StoredOperationResult.Invalid;

        byte[] payloadHash;
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(operation.PayloadJson);
            if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return StoredOperationResult.Invalid;
            payloadHash = SHA256.HashData(Encoding.UTF8.GetBytes(operation.PayloadJson));
        }
        catch (System.Text.Json.JsonException)
        {
            return StoredOperationResult.Invalid;
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction(deferred: false);
        await using (var existing = connection.CreateCommand())
        {
            existing.Transaction = transaction;
            existing.CommandText = "SELECT Sequence, DeviceId, ActorUserId, OperationType, PayloadHash FROM CoordinatorOperations WHERE OperationId = $id LIMIT 1;";
            existing.Parameters.AddWithValue("$id", operation.OperationId.ToString("D"));
            await using var reader = await existing.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var same = Guid.Parse(reader.GetString(1)) == authenticatedDeviceId &&
                    Guid.Parse(reader.GetString(2)) == operation.ActorUserId &&
                    string.Equals(reader.GetString(3), operation.OperationType, StringComparison.Ordinal) &&
                    CryptographicOperations.FixedTimeEquals((byte[])reader[4], payloadHash);
                var sequence = reader.GetInt64(0);
                await reader.DisposeAsync();
                await transaction.RollbackAsync(cancellationToken);
                return same ? new StoredOperationResult(true, true, sequence) : StoredOperationResult.Conflict;
            }
        }

        var now = DateTime.UtcNow;
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO CoordinatorOperations
                    (OperationId, DeviceId, ActorUserId, OperationType, PayloadJson, PayloadHash, CreatedAtUtc, ReceivedAtUtc)
                VALUES ($id, $device, $actor, $type, $payload, $hash, $created, $received);
                SELECT last_insert_rowid();
                """;
            insert.Parameters.AddWithValue("$id", operation.OperationId.ToString("D"));
            insert.Parameters.AddWithValue("$device", authenticatedDeviceId.ToString("D"));
            insert.Parameters.AddWithValue("$actor", operation.ActorUserId.ToString("D"));
            insert.Parameters.AddWithValue("$type", operation.OperationType);
            insert.Parameters.AddWithValue("$payload", operation.PayloadJson);
            insert.Parameters.AddWithValue("$hash", payloadHash);
            insert.Parameters.AddWithValue("$created", operation.CreatedAtUtc.Ticks);
            insert.Parameters.AddWithValue("$received", now.Ticks);
            var sequence = Convert.ToInt64(await insert.ExecuteScalarAsync(cancellationToken));
            await transaction.CommitAsync(cancellationToken);
            return new StoredOperationResult(true, false, sequence);
        }
    }

    public async Task<IReadOnlyList<ConfirmedOperation>> GetAfterAsync(
        long afterSequence, int limit, Guid? excludingDeviceId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Sequence, OperationId, DeviceId, ActorUserId, OperationType, PayloadJson, CreatedAtUtc, ReceivedAtUtc
            FROM CoordinatorOperations
            WHERE Sequence > $after AND ($exclude IS NULL OR DeviceId <> $exclude)
            ORDER BY Sequence LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$after", afterSequence);
        command.Parameters.AddWithValue("$exclude", excludingDeviceId?.ToString("D") is { } excluded ? excluded : DBNull.Value);
        command.Parameters.AddWithValue("$limit", limit);
        var operations = new List<ConfirmedOperation>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            operations.Add(new ConfirmedOperation(reader.GetInt64(0), Guid.Parse(reader.GetString(1)),
                Guid.Parse(reader.GetString(2)), Guid.Parse(reader.GetString(3)), reader.GetString(4),
                reader.GetString(5), new DateTime(reader.GetInt64(6), DateTimeKind.Utc),
                new DateTime(reader.GetInt64(7), DateTimeKind.Utc)));
        }
        return operations;
    }
}

internal sealed record IncomingOperation(Guid OperationId, Guid DeviceId, Guid ActorUserId,
    string OperationType, string PayloadJson, DateTime CreatedAtUtc);
internal sealed record ConfirmedOperation(long Sequence, Guid OperationId, Guid DeviceId, Guid ActorUserId,
    string OperationType, string PayloadJson, DateTime CreatedAtUtc, DateTime ConfirmedAtUtc);
internal sealed record StoredOperationResult(bool Accepted, bool AlreadyAccepted, long? Sequence)
{
    public static StoredOperationResult Invalid => new(false, false, null);
    public static StoredOperationResult Conflict => new(false, false, null);
}
