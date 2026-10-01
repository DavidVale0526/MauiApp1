using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using MauiApp1.Infrastructure.Sync;
using MauiApp1.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using MauiApp1.Application.Sync;
using MauiApp1.Application.Products;
using MauiApp1.Application.Categories;
using System.Text.Json;

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
                ReceivedAtUtc INTEGER NOT NULL,
                Status INTEGER NOT NULL DEFAULT 1,
                ConflictMessage TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_CoordinatorOperations_Sequence_Device
                ON CoordinatorOperations (Sequence, DeviceId);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await EnsureColumnAsync(connection, "Status", "INTEGER NOT NULL DEFAULT 1", cancellationToken);
        await EnsureColumnAsync(connection, "ConflictMessage", "TEXT NULL", cancellationToken);
        await using var index = connection.CreateCommand();
        index.CommandText = "CREATE INDEX IF NOT EXISTS IX_CoordinatorOperations_Status_Sequence ON CoordinatorOperations (Status, Sequence);";
        await index.ExecuteNonQueryAsync(cancellationToken);
        await SeedExistingOutboxAsync(connection, cancellationToken);
    }

    public async Task<StoredOperationResult> StoreAsync(
        IncomingOperation operation, Guid authenticatedDeviceId, Guid authenticatedUserId,
        CoordinatorBusinessOperationApplier businessApplier,
        CancellationToken cancellationToken = default)
    {
        if (operation.OperationId == Guid.Empty || operation.ActorUserId == Guid.Empty ||
            operation.DeviceId != authenticatedDeviceId || authenticatedUserId == Guid.Empty || string.IsNullOrWhiteSpace(operation.OperationType) ||
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
            existing.CommandText = "SELECT Sequence, DeviceId, ActorUserId, OperationType, PayloadHash, Status, ConflictMessage FROM CoordinatorOperations WHERE OperationId = $id LIMIT 1;";
            existing.Parameters.AddWithValue("$id", operation.OperationId.ToString("D"));
            await using var reader = await existing.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var same = Guid.Parse(reader.GetString(1)) == authenticatedDeviceId &&
                    Guid.Parse(reader.GetString(2)) == authenticatedUserId &&
                    string.Equals(reader.GetString(3), operation.OperationType, StringComparison.Ordinal) &&
                    CryptographicOperations.FixedTimeEquals((byte[])reader[4], payloadHash);
                var sequence = reader.GetInt64(0);
                var status = reader.GetInt32(5);
                var conflictMessage = reader.IsDBNull(6) ? null : reader.GetString(6);
                await reader.DisposeAsync();
                await transaction.RollbackAsync(cancellationToken);
                return same ? status == 2
                    ? StoredOperationResult.BusinessConflict(conflictMessage ?? "La operación requiere resolución del Administrador.", sequence)
                    : new StoredOperationResult(true, true, sequence)
                    : StoredOperationResult.Conflict;
            }
        }

        var businessConflict = await businessApplier.ApplyAsync(connection, transaction, operation.OperationId,
            operation.OperationType, operation.PayloadJson, operation.CreatedAtUtc, authenticatedUserId, cancellationToken);

        var now = DateTime.UtcNow;
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO CoordinatorOperations
                    (OperationId, DeviceId, ActorUserId, OperationType, PayloadJson, PayloadHash, CreatedAtUtc, ReceivedAtUtc, Status, ConflictMessage)
                VALUES ($id, $device, $actor, $type, $payload, $hash, $created, $received, $status, $conflictMessage);
                SELECT last_insert_rowid();
                """;
            insert.Parameters.AddWithValue("$id", operation.OperationId.ToString("D"));
            insert.Parameters.AddWithValue("$device", authenticatedDeviceId.ToString("D"));
            insert.Parameters.AddWithValue("$actor", authenticatedUserId.ToString("D"));
            insert.Parameters.AddWithValue("$type", operation.OperationType);
            insert.Parameters.AddWithValue("$payload", operation.PayloadJson);
            insert.Parameters.AddWithValue("$hash", payloadHash);
            insert.Parameters.AddWithValue("$created", operation.CreatedAtUtc.Ticks);
            insert.Parameters.AddWithValue("$received", now.Ticks);
            insert.Parameters.AddWithValue("$status", businessConflict is null ? 1 : 2);
            insert.Parameters.AddWithValue("$conflictMessage", businessConflict is null ? DBNull.Value : businessConflict);
            var sequence = Convert.ToInt64(await insert.ExecuteScalarAsync(cancellationToken));
            await transaction.CommitAsync(cancellationToken);
            return businessConflict is null
                ? new StoredOperationResult(true, false, sequence)
                : StoredOperationResult.BusinessConflict(businessConflict, sequence);
        }
    }

    public async Task<IReadOnlyList<ConfirmedOperation>> GetAfterAsync(
        long afterSequence, int limit, Guid? excludingDeviceId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await SeedExistingOutboxAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT o.Sequence, o.OperationId, o.DeviceId, o.ActorUserId, u.UserName, u.Role,
                   o.OperationType, o.PayloadJson, o.CreatedAtUtc, o.ReceivedAtUtc
            FROM CoordinatorOperations o
            LEFT JOIN UserAccounts u ON u.UserId = o.ActorUserId
            WHERE o.Sequence > $after AND o.Status = 1 AND ($exclude IS NULL OR o.DeviceId <> $exclude)
            ORDER BY o.Sequence LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$after", afterSequence);
        command.Parameters.AddWithValue("$exclude", excludingDeviceId?.ToString("D") is { } excluded ? excluded : DBNull.Value);
        command.Parameters.AddWithValue("$limit", limit);
        var operations = new List<ConfirmedOperation>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            operations.Add(new ConfirmedOperation(reader.GetInt64(0), Guid.Parse(reader.GetString(1)),
                Guid.Parse(reader.GetString(2)), Guid.Parse(reader.GetString(3)), reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetInt32(5), reader.GetString(6), reader.GetString(7),
                new DateTime(reader.GetInt64(8), DateTimeKind.Utc), new DateTime(reader.GetInt64(9), DateTimeKind.Utc)));
        }
        return operations;
    }

    public async Task<IReadOnlyList<CoordinatorConflictItem>> GetConflictsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Sequence, OperationId, OperationType, PayloadJson, ConflictMessage FROM CoordinatorOperations WHERE Status = 2 ORDER BY Sequence;";
        var rows = new List<(Guid Id, string Type, string Payload, string Message)>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
                rows.Add((Guid.Parse(reader.GetString(1)), reader.GetString(2), reader.GetString(3),
                    reader.IsDBNull(4) ? "Requiere revisión del Administrador." : reader.GetString(4)));
        }
        var conflicts = new List<CoordinatorConflictItem>();
        foreach (var row in rows)
        {
            var summaries = await BuildSummariesAsync(connection, row.Type, row.Payload, cancellationToken);
            conflicts.Add(new CoordinatorConflictItem(row.Id, row.Type, row.Message, summaries.Current, summaries.Proposed, summaries.CanApply));
        }
        return conflicts;
    }

    private static async Task<(string Current, string Proposed, bool CanApply)> BuildSummariesAsync(
        SqliteConnection connection, string type, string payload, CancellationToken ct)
    {
        using var document = JsonDocument.Parse(payload);
        var idProperty = type.StartsWith("Product.", StringComparison.Ordinal) ? "ProductId" : "CategoryId";
        var idElement = document.RootElement.EnumerateObject().FirstOrDefault(item =>
            string.Equals(item.Name, idProperty, StringComparison.OrdinalIgnoreCase)).Value;
        if (idElement.ValueKind != JsonValueKind.String || !Guid.TryParse(idElement.GetString(), out var id))
            return ("No se pudo leer la versión central.", "Datos de la operación recibida", false);
        if (type.StartsWith("Product.", StringComparison.Ordinal))
        {
            var proposed = JsonSerializer.Deserialize<ProductChangedPayload>(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true });
            await using var query = connection.CreateCommand();
            query.CommandText = "SELECT Name, SalePriceMinorUnits, Unit, StockMilliUnits, MinimumStockMilliUnits, IsActive FROM Products WHERE ProductId = $id;";
            query.Parameters.AddWithValue("$id", id.ToString("D"));
            await using var reader = await query.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return ("Producto no encontrado en el coordinador.", proposed?.Name ?? "Propuesta de producto", true);
            var current = $"Nombre: {reader.GetString(0)}; precio: {reader.GetInt64(1) / 100m:0.00}; unidad: {reader.GetString(2)}; existencia: {reader.GetInt64(3) / 1000m:0.000}; mínimo: {reader.GetInt64(4) / 1000m:0.000}; activo: {(reader.GetBoolean(5) ? "sí" : "no")}.";
            var incoming = proposed is null ? "Propuesta de producto" : $"Nombre: {proposed.Name}; precio: {proposed.SalePriceMinorUnits / 100m:0.00}; unidad: {proposed.Unit}; mínimo: {proposed.MinimumStockMilliUnits / 1000m:0.000}; activo: {(proposed.IsActive ? "sí" : "no")}.";
            return (current, incoming, type is "Product.Updated" or "Product.Deactivated" or "Product.Reactivated");
        }
        if (type == "Category.Renamed")
        {
            var proposed = JsonSerializer.Deserialize<CategoryChangedPayload>(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true });
            await using var query = connection.CreateCommand();
            query.CommandText = "SELECT Name FROM Categories WHERE CategoryId = $id;";
            query.Parameters.AddWithValue("$id", id.ToString("D"));
            var current = await query.ExecuteScalarAsync(ct) as string ?? "Categoría no encontrada en el coordinador.";
            return ($"Nombre vigente: {current}.", $"Nombre propuesto: {proposed?.Name ?? "(vacío)"}.", proposed is not null);
        }
        return ("La operación no modificó la base central.", "Requiere conciliación específica.", false);
    }

    public async Task<string?> ResolveConflictAsync(Guid operationId, bool applyIncoming,
        Guid administratorUserId, CoordinatorBusinessOperationApplier applier, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction(deferred: false);
        string? type;
        string? payload;
        Guid actor;
        DateTime created;
        long sequence;
        await using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = "SELECT Sequence, OperationType, PayloadJson, ActorUserId, CreatedAtUtc FROM CoordinatorOperations WHERE OperationId = $id AND Status = 2 LIMIT 1;";
            select.Parameters.AddWithValue("$id", operationId.ToString("D"));
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return "El conflicto ya fue resuelto o no existe.";
            sequence = reader.GetInt64(0); type = reader.GetString(1); payload = reader.GetString(2);
            actor = Guid.Parse(reader.GetString(3)); created = new DateTime(reader.GetInt64(4), DateTimeKind.Utc);
        }
        if (applyIncoming)
        {
            if (type is not ("Product.Updated" or "Product.Deactivated" or "Product.Reactivated" or "Category.Renamed"))
                return "Esta operación requiere una conciliación de datos específica y no admite aplicar la propuesta automáticamente.";
            var error = await applier.ApplyAsync(connection, transaction, operationId, type!, payload!, created, actor,
                cancellationToken, forceCatalogSnapshot: true);
            if (error is not null) return error;
        }

        var resolution = await CreateResolutionEventAsync(connection, transaction, operationId, type!, payload!, administratorUserId, cancellationToken);
        if (resolution is not null) return resolution;

        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = applyIncoming
            ? "UPDATE CoordinatorOperations SET Status = 3, ConflictMessage = 'Resuelto: se aplicó la propuesta del dispositivo.' WHERE OperationId = $id AND Status = 2;"
            : "UPDATE CoordinatorOperations SET Status = 3, ConflictMessage = 'Resuelto: se conservó la versión central.' WHERE OperationId = $id AND Status = 2;";
        update.Parameters.AddWithValue("$id", operationId.ToString("D"));
        if (await update.ExecuteNonQueryAsync(cancellationToken) != 1) return "El conflicto cambió durante la resolución. Actualiza la lista.";
        await transaction.CommitAsync(cancellationToken);
        return null;
    }

    private static async Task<string?> CreateResolutionEventAsync(SqliteConnection connection, SqliteTransaction transaction,
        Guid conflictedOperationId, string type, string originalPayload, Guid administratorUserId, CancellationToken ct)
    {
        var supported = type.StartsWith("Product.", StringComparison.Ordinal) || type == "Category.Renamed";
        if (!supported) return null;
        using var document = JsonDocument.Parse(originalPayload);
        var idName = type.StartsWith("Product.", StringComparison.Ordinal) ? "ProductId" : "CategoryId";
        var idElement = document.RootElement.EnumerateObject().FirstOrDefault(item => string.Equals(item.Name, idName, StringComparison.OrdinalIgnoreCase)).Value;
        if (idElement.ValueKind != JsonValueKind.String || !Guid.TryParse(idElement.GetString(), out var recordId)) return "No se pudo crear el cambio de resolución.";
        string eventType;
        string payload;
        await using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.Parameters.AddWithValue("$id", recordId.ToString("D"));
            if (type.StartsWith("Product.", StringComparison.Ordinal))
            {
                read.CommandText = "SELECT Name, SalePriceMinorUnits, StockMilliUnits, Unit, CategoryId, MinimumStockMilliUnits, IsActive FROM Products WHERE ProductId=$id;";
                await using var reader = await read.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct)) return "El producto ya no existe en la base central.";
                var snapshot = new ProductChangedPayload(recordId, reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetString(3),
                    Guid.Parse(reader.GetString(4)), reader.GetInt64(5), reader.GetBoolean(6), ResolutionOfOperationId: conflictedOperationId);
                payload = JsonSerializer.Serialize(snapshot);
                eventType = "Product.Resolved";
            }
            else
            {
                read.CommandText = "SELECT Name FROM Categories WHERE CategoryId=$id;";
                var name = await read.ExecuteScalarAsync(ct) as string;
                if (name is null) return "La categoría ya no existe en la base central.";
                payload = JsonSerializer.Serialize(new CategoryChangedPayload(recordId, name, ResolutionOfOperationId: conflictedOperationId));
                eventType = "Category.Resolved";
            }
        }
        var opId = Guid.NewGuid();
        var now = DateTime.UtcNow.Ticks;
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO CoordinatorOperations
                (OperationId, DeviceId, ActorUserId, OperationType, PayloadJson, PayloadHash, CreatedAtUtc, ReceivedAtUtc, Status, ConflictMessage)
            VALUES ($id, $device, $actor, $type, $payload, $hash, $created, $received, 1, NULL);
            """;
        insert.Parameters.AddWithValue("$id", opId.ToString("D"));
        insert.Parameters.AddWithValue("$device", Guid.Empty.ToString("D"));
        insert.Parameters.AddWithValue("$actor", administratorUserId.ToString("D"));
        insert.Parameters.AddWithValue("$type", eventType);
        insert.Parameters.AddWithValue("$payload", payload);
        insert.Parameters.AddWithValue("$hash", SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        insert.Parameters.AddWithValue("$created", now);
        insert.Parameters.AddWithValue("$received", now);
        await insert.ExecuteNonQueryAsync(ct);
        return null;
    }

    private static async Task EnsureColumnAsync(SqliteConnection connection, string name, string definition, CancellationToken ct)
    {
        await using var check = connection.CreateCommand();
        check.CommandText = "PRAGMA table_info(CoordinatorOperations);";
        await using var reader = await check.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            if (string.Equals(reader.GetString(1), name, StringComparison.OrdinalIgnoreCase)) return;
        await reader.DisposeAsync();
        await using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE CoordinatorOperations ADD COLUMN {name} {definition};";
        await alter.ExecuteNonQueryAsync(ct);
    }

    private static async Task SeedExistingOutboxAsync(SqliteConnection connection, CancellationToken ct)
    {
        var seedRows = new List<(string Id, string Device, string Actor, string Type, string Payload, long Created)>();
        await using (var read = connection.CreateCommand())
        {
            read.CommandText = "SELECT OperationId, DeviceId, ActorUserId, OperationType, PayloadJson, CreatedAtUtc FROM OutboxMessages ORDER BY CreatedAtUtc, OperationId;";
            await using var reader = await read.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                seedRows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetInt64(5)));
        }

        foreach (var row in seedRows)
        {
            await using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT OR IGNORE INTO CoordinatorOperations
                    (OperationId, DeviceId, ActorUserId, OperationType, PayloadJson, PayloadHash,
                     CreatedAtUtc, ReceivedAtUtc, Status, ConflictMessage)
                VALUES ($id, $device, $actor, $type, $payload, $hash, $created, $created, 1, NULL);
                """;
            insert.Parameters.AddWithValue("$id", row.Id);
            insert.Parameters.AddWithValue("$device", row.Device);
            insert.Parameters.AddWithValue("$actor", row.Actor);
            insert.Parameters.AddWithValue("$type", row.Type);
            insert.Parameters.AddWithValue("$payload", row.Payload);
            insert.Parameters.AddWithValue("$hash", SHA256.HashData(Encoding.UTF8.GetBytes(row.Payload)));
            insert.Parameters.AddWithValue("$created", row.Created);
            await insert.ExecuteNonQueryAsync(ct);
        }

        await using var confirm = connection.CreateCommand();
        confirm.CommandText = "UPDATE OutboxMessages SET Status = 1, ConfirmedAtUtc = COALESCE(ConfirmedAtUtc, CreatedAtUtc) WHERE Status = 0;";
        await confirm.ExecuteNonQueryAsync(ct);
    }
}

internal sealed record IncomingOperation(Guid OperationId, Guid DeviceId, Guid ActorUserId,
    string OperationType, string PayloadJson, DateTime CreatedAtUtc);
internal sealed record ConfirmedOperation(long Sequence, Guid OperationId, Guid DeviceId, Guid ActorUserId,
    string? ActorUserName, int? ActorRole, string OperationType, string PayloadJson, DateTime CreatedAtUtc, DateTime ConfirmedAtUtc);
internal sealed record StoredOperationResult(bool Accepted, bool AlreadyAccepted, long? Sequence, string? ConflictMessage = null)
{
    public static StoredOperationResult Invalid => new(false, false, null);
    public static StoredOperationResult Conflict => new(false, false, null);
    public static StoredOperationResult BusinessConflict(string message, long? sequence = null) => new(false, false, sequence, message);
}
