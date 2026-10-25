using MauiApp1.Application.Authentication;
using MauiApp1.Application.Sync;
using MauiApp1.Application.Storage;
using MauiApp1.Domain.Users;
using MauiApp1.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using MauiApp1.Application.Categories;
using MauiApp1.Application.Products;
using MauiApp1.Infrastructure.Persistence.Entities;
using MauiApp1.Infrastructure.Authentication;

namespace MauiApp1.Infrastructure.Sync;

internal sealed class CoordinatorConflictReviewService(
    IDbContextFactory<LocalDbContext> contextFactory,
    ICoordinatorChangeClient remoteClient,
    IAuthenticationService authenticationService,
    CoordinatorBusinessOperationApplier applier,
    ILocalDeviceIdentity deviceIdentity,
    Pbkdf2PasswordHasher passwordHasher) : ICoordinatorConflictReviewService
{
    public async Task<IReadOnlyList<CoordinatorConflictItem>> GetOpenAsync(CancellationToken ct = default)
    {
        if (authenticationService.CurrentUser?.Role != UserRole.Administrator) return [];
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var connection = (SqliteConnection)context.Database.GetDbConnection();
        await connection.OpenAsync(ct);
        if (!await HasJournalAsync(connection, ct))
        {
            var remoteConflicts = await remoteClient.GetConflictsAsync(ct);
            if (remoteConflicts is null)
                throw new InvalidOperationException("No se pudieron consultar los conflictos del coordinador. Comprueba la vinculación, la sesión del usuario y la conexión LAN.");
            var remote = remoteConflicts.ToList();
            var localRows = await context.DeviceSyncConflicts.AsNoTracking().Where(item => !item.IsResolved)
                .OrderBy(item => item.CoordinatorSequence).ToListAsync(ct);
            foreach (var row in localRows)
            {
                var summary = await SummarizeAsync(connection, row.OperationType, row.PayloadJson, ct);
                remote.Add(new CoordinatorConflictItem(row.OperationId, row.OperationType, row.Message,
                    summary.Current, summary.Proposed, summary.CanApply, IsLocalConflict: true));
            }
            return remote;
        }
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT OperationId, OperationType, PayloadJson, ConflictMessage FROM CoordinatorOperations WHERE Status = 2 ORDER BY Sequence;";
        var rows = new List<(Guid Id, string Type, string Payload, string Message)>();
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) rows.Add((Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? "Requiere revisión." : reader.GetString(3)));
        var result = new List<CoordinatorConflictItem>();
        foreach (var row in rows)
        {
            var summary = await SummarizeAsync(connection, row.Type, row.Payload, ct);
            result.Add(new CoordinatorConflictItem(row.Id, row.Type, row.Message, summary.Current, summary.Proposed, summary.CanApply));
        }
        return result;
    }

    public async Task<string?> ResolveAsync(Guid operationId, bool applyIncoming, CancellationToken ct = default)
    {
        if (authenticationService.CurrentUser?.Role != UserRole.Administrator) return "Solo el Administrador puede resolver conflictos.";
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var connection = (SqliteConnection)context.Database.GetDbConnection();
        await connection.OpenAsync(ct);
        var localConflict = await context.DeviceSyncConflicts.SingleOrDefaultAsync(item => item.OperationId == operationId && !item.IsResolved, ct);
        if (localConflict is not null)
        {
            var localDeviceId = await deviceIdentity.GetDeviceIdAsync(ct);
            var state = await context.DeviceSyncStates.SingleOrDefaultAsync(item => item.DeviceId == localDeviceId, ct);
            await using var localTransaction = await context.Database.BeginTransactionAsync(ct);
            if (applyIncoming)
            {
                var page = await remoteClient.GetChangesAsync(localConflict.CoordinatorSequence - 1, 100, ct);
                var change = page?.Changes.SingleOrDefault(item => item.OperationId == operationId);
                if (change is null) return "El coordinador ya no devolvió el cambio que se debe aplicar. Actualiza y revisa la lista.";
                var actorId = await EnsureActorAsync(context, change, ct);
                await context.SaveChangesAsync(ct);
                var error = await applier.ApplyAsync(connection, (SqliteTransaction)localTransaction.GetDbTransaction(), change.OperationId,
                    change.OperationType, change.PayloadJson, change.CreatedAtUtc, actorId, ct, forceCatalogSnapshot: true);
                if (error is not null) return error;
            }
            localConflict.IsResolved = true;
            if (state is not null) state.LastSequence = Math.Max(state.LastSequence, localConflict.CoordinatorSequence);
            await context.SaveChangesAsync(ct);
            await localTransaction.CommitAsync(ct);
            return null;
        }
        if (!await HasJournalAsync(connection, ct))
        {
            var remoteError = await remoteClient.ResolveConflictAsync(operationId, applyIncoming, ct);
            if (remoteError is null)
            {
                var localOperation = await context.OutboxMessages.FindAsync([operationId], ct);
                if (localOperation is not null && localOperation.Status == OutboxStatus.Conflict)
                {
                    localOperation.Status = OutboxStatus.Confirmed;
                    localOperation.ConfirmedAtUtc = DateTime.UtcNow;
                    localOperation.LastError = null;
                    await context.SaveChangesAsync(ct);
                }
            }
            return remoteError;
        }
        using var transaction = connection.BeginTransaction(deferred: false);
        string type, payload; Guid actor; DateTime created;
        await using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = "SELECT OperationType, PayloadJson, ActorUserId, CreatedAtUtc FROM CoordinatorOperations WHERE OperationId = $id AND Status = 2 LIMIT 1;";
            select.Parameters.AddWithValue("$id", operationId.ToString("D"));
            await using var reader = await select.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return "El conflicto ya fue resuelto o no existe.";
            type = reader.GetString(0); payload = reader.GetString(1); actor = Guid.Parse(reader.GetString(2)); created = new DateTime(reader.GetInt64(3), DateTimeKind.Utc);
        }
        if (applyIncoming)
        {
            if (type is not ("Product.Updated" or "Product.Deactivated" or "Product.Reactivated" or "Category.Renamed"))
                return "Esta operación requiere una conciliación específica y no puede aplicarse desde esta pantalla.";
            var error = await applier.ApplyAsync(connection, transaction, operationId, type, payload, created, actor, ct, forceCatalogSnapshot: true);
            if (error is not null) return error;
        }
        var adminId = authenticationService.CurrentUser!.Id.Value;
        var publishError = await PublishResolvedValueAsync(connection, transaction, operationId, type, payload, adminId, ct);
        if (publishError is not null) return publishError;
        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = applyIncoming
            ? "UPDATE CoordinatorOperations SET Status = 3, ConflictMessage = 'Resuelto: se aplicó la propuesta del dispositivo.' WHERE OperationId = $id AND Status = 2;"
            : "UPDATE CoordinatorOperations SET Status = 3, ConflictMessage = 'Resuelto: se conservó la versión central.' WHERE OperationId = $id AND Status = 2;";
        update.Parameters.AddWithValue("$id", operationId.ToString("D"));
        if (await update.ExecuteNonQueryAsync(ct) != 1) return "El conflicto cambió durante la resolución. Actualiza la lista.";
        await transaction.CommitAsync(ct);
        return null;
    }

    private async Task<Guid> EnsureActorAsync(LocalDbContext context, CoordinatorChange change, CancellationToken ct)
    {
        var account = await context.UserAccounts.SingleOrDefaultAsync(item => item.CoordinatorUserId == change.ActorUserId, ct);
        if (account is not null) return account.UserId;
        if (change.ActorRole != (int)UserRole.Administrator && change.ActorRole != (int)UserRole.Cashier)
            throw new InvalidOperationException("El coordinador devolvió una cuenta sin rol válido.");
        var id = Guid.NewGuid();
        var (salt, hash) = await Task.Run(() => passwordHasher.Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))), ct);
        context.UserAccounts.Add(new UserAccountEntity { UserId = id, CoordinatorUserId = change.ActorUserId,
            UserName = string.IsNullOrWhiteSpace(change.ActorUserName) ? "Usuario sincronizado" : change.ActorUserName,
            NormalizedUserName = $"__REMOTE_{change.ActorUserId:N}", PasswordSalt = salt, PasswordHash = hash,
            PasswordIterations = Pbkdf2PasswordHasher.Iterations, Role = change.ActorRole.Value, IsEnabled = false, CreatedAtUtc = change.CreatedAtUtc });
        return id;
    }

    private static async Task<string?> PublishResolvedValueAsync(SqliteConnection connection, SqliteTransaction transaction,
        Guid conflictedId, string type, string originalPayload, Guid administratorId, CancellationToken ct)
    {
        if (!type.StartsWith("Product.", StringComparison.Ordinal) && type != "Category.Renamed") return null;
        using var document = JsonDocument.Parse(originalPayload);
        var idName = type.StartsWith("Product.", StringComparison.Ordinal) ? "ProductId" : "CategoryId";
        var element = Find(document.RootElement, idName);
        if (element.ValueKind != JsonValueKind.String || !Guid.TryParse(element.GetString(), out var entityId)) return "No se pudo crear el aviso de resolución.";
        string eventType;
        string resolvedPayload;
        await using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.Parameters.AddWithValue("$id", entityId.ToString("D"));
            if (type.StartsWith("Product.", StringComparison.Ordinal))
            {
                read.CommandText = "SELECT Name, SalePriceMinorUnits, StockMilliUnits, Unit, CategoryId, MinimumStockMilliUnits, IsActive, ImageData FROM Products WHERE ProductId=$id;";
                await using var reader = await read.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct)) return "El producto ya no existe en la base central.";
                resolvedPayload = JsonSerializer.Serialize(new ProductChangedPayload(entityId, reader.GetString(0), reader.GetInt64(1),
                    reader.GetInt64(2), reader.GetString(3), Guid.Parse(reader.GetString(4)), reader.GetInt64(5), reader.GetBoolean(6),
                    ResolutionOfOperationId: conflictedId,
                    ImageData: reader.IsDBNull(7) ? null : reader.GetFieldValue<byte[]>(7), ImageDataSpecified: true));
                eventType = "Product.Resolved";
            }
            else
            {
                read.CommandText = "SELECT Name FROM Categories WHERE CategoryId=$id;";
                var name = await read.ExecuteScalarAsync(ct) as string;
                if (name is null) return "La categoría ya no existe en la base central.";
                resolvedPayload = JsonSerializer.Serialize(new CategoryChangedPayload(entityId, name, ResolutionOfOperationId: conflictedId));
                eventType = "Category.Resolved";
            }
        }
        var newOperationId = Guid.NewGuid(); var now = DateTime.UtcNow.Ticks;
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO CoordinatorOperations
                (OperationId, DeviceId, ActorUserId, OperationType, PayloadJson, PayloadHash, CreatedAtUtc, ReceivedAtUtc, Status, ConflictMessage)
            VALUES ($id, $device, $actor, $type, $payload, $hash, $created, $received, 1, NULL);
            """;
        insert.Parameters.AddWithValue("$id", newOperationId.ToString("D"));
        insert.Parameters.AddWithValue("$device", Guid.Empty.ToString("D"));
        insert.Parameters.AddWithValue("$actor", administratorId.ToString("D"));
        insert.Parameters.AddWithValue("$type", eventType);
        insert.Parameters.AddWithValue("$payload", resolvedPayload);
        insert.Parameters.AddWithValue("$hash", SHA256.HashData(Encoding.UTF8.GetBytes(resolvedPayload)));
        insert.Parameters.AddWithValue("$created", now); insert.Parameters.AddWithValue("$received", now);
        await insert.ExecuteNonQueryAsync(ct);
        return null;
    }

    private static async Task<bool> HasJournalAsync(SqliteConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name='CoordinatorOperations' LIMIT 1;";
        return await command.ExecuteScalarAsync(ct) is not null;
    }

    private static async Task<(string Current, string Proposed, bool CanApply)> SummarizeAsync(SqliteConnection connection, string type, string payload, CancellationToken ct)
    {
        using var document = JsonDocument.Parse(payload);
        var property = type.StartsWith("Product.", StringComparison.Ordinal) ? "ProductId" : "CategoryId";
        var element = Find(document.RootElement, property);
        if (element.ValueKind != JsonValueKind.String || !Guid.TryParse(element.GetString(), out var id))
            return ("No se pudo leer la versión vigente.", "Datos recibidos", false);
        if (type.StartsWith("Product.", StringComparison.Ordinal))
        {
            var root = document.RootElement;
            string S(string key) => Find(root, key).GetString() ?? "";
            long L(string key) => Find(root, key).GetInt64();
            bool B(string key) => Find(root, key).GetBoolean();
            var imageSpecified = Find(root, "ImageDataSpecified").ValueKind == JsonValueKind.True;
            var incomingImage = imageSpecified
                ? Find(root, "ImageData").ValueKind == JsonValueKind.Null ? "no" : "sí"
                : "sin cambio";
            await using var query = connection.CreateCommand();
            query.CommandText = "SELECT Name, SalePriceMinorUnits, Unit, StockMilliUnits, MinimumStockMilliUnits, IsActive, ImageData FROM Products WHERE ProductId=$id;";
            query.Parameters.AddWithValue("$id", id.ToString("D"));
            await using var reader = await query.ExecuteReaderAsync(ct);
            var proposed = $"Nombre: {S("Name")}; precio: {L("SalePriceMinorUnits") / 100m:0.00}; unidad: {S("Unit")}; mínimo: {L("MinimumStockMilliUnits") / 1000m:0.###}; activo: {(B("IsActive") ? "sí" : "no")}; imagen: {incomingImage}.";
            if (!await reader.ReadAsync(ct)) return ("Producto no encontrado en el coordinador.", proposed, true);
            var current = $"Nombre: {reader.GetString(0)}; precio: {reader.GetInt64(1) / 100m:0.00}; unidad: {reader.GetString(2)}; existencia: {reader.GetInt64(3) / 1000m:0.###}; mínimo: {reader.GetInt64(4) / 1000m:0.###}; activo: {(reader.GetBoolean(5) ? "sí" : "no")}; imagen: {(reader.IsDBNull(6) ? "no" : "sí")}.";
            return (current, proposed, type is "Product.Updated" or "Product.Deactivated" or "Product.Reactivated" or "Product.Resolved");
        }
        if (type == "Category.Renamed")
        {
            var proposed = Find(document.RootElement, "Name").GetString() ?? "";
            await using var query = connection.CreateCommand(); query.CommandText = "SELECT Name FROM Categories WHERE CategoryId=$id;";
            query.Parameters.AddWithValue("$id", id.ToString("D"));
            var current = await query.ExecuteScalarAsync(ct) as string ?? "Categoría no encontrada en el coordinador.";
            return ($"Nombre vigente: {current}.", $"Nombre propuesto: {proposed}.", true);
        }
        return ("La operación no modificó la base central.", "Requiere conciliación específica.", false);
    }

    private static JsonElement Find(JsonElement root, string name) => root.EnumerateObject().FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)).Value;
}
