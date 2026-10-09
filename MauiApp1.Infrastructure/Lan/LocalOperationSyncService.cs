using MauiApp1.Application.Authentication;
using MauiApp1.Application.Sync;
using MauiApp1.Application.Storage;
using MauiApp1.Infrastructure.Persistence;
using MauiApp1.Infrastructure.Persistence.Entities;
using MauiApp1.Infrastructure.Sync;
using MauiApp1.Infrastructure.Authentication;
using MauiApp1.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text.Json;

namespace MauiApp1.Infrastructure.Lan;

internal sealed class LocalOperationSyncService(
    IDbContextFactory<LocalDbContext> contextFactory,
    ICoordinatorOperationSender sender,
    IAuthenticationService authenticationService,
    ILocalDeviceIdentity deviceIdentity,
    ICoordinatorChangeClient changeClient,
    CoordinatorBusinessOperationApplier businessApplier,
    Pbkdf2PasswordHasher passwordHasher,
    ILocalDataChangeNotifier changeNotifier) : ILocalOperationSyncService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _lastDownloadError;

    public async Task<LocalOperationSyncStatus> SynchronizeAsync(CancellationToken cancellationToken = default)
    {
        if (authenticationService.CurrentUser is null) return await GetStatusAsync(cancellationToken);
        // Wait for an active pass so a SignalR notice received during a send is
        // followed by a download instead of silently dropping the notice.
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
            var localConnection = (SqliteConnection)context.Database.GetDbConnection();
            await localConnection.OpenAsync(cancellationToken);
            await using (var journalCheck = localConnection.CreateCommand())
            {
                journalCheck.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name='CoordinatorOperations' LIMIT 1;";
                if (await journalCheck.ExecuteScalarAsync(cancellationToken) is not null)
                {
                    await PublishLocalCoordinatorOperationsAsync(context, localConnection, cancellationToken);
                    return await GetStatusAsync(cancellationToken);
                }
            }
            await localConnection.CloseAsync();
            var outbox = new EfOperationOutbox(context, deviceIdentity);
            var pending = await outbox.GetPendingAsync(100, cancellationToken, authenticationService.CurrentUser?.Id.Value);
            foreach (var operation in pending)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await outbox.RecordAttemptAsync(operation.OperationId, cancellationToken);
                var result = await sender.SendAsync(operation, cancellationToken);
                if (result.Status is CoordinatorOperationSendStatus.Accepted or CoordinatorOperationSendStatus.AlreadyAccepted)
                {
                    await outbox.MarkConfirmedAsync(operation.OperationId, DateTime.UtcNow, cancellationToken);
                    continue;
                }
                if (result.Status == CoordinatorOperationSendStatus.Conflict)
                    await outbox.MarkConflictAsync(operation.OperationId,
                        result.Message ?? "El coordinador detectó un cambio simultáneo. Se requiere revisión del Administrador.", cancellationToken);
                else
                {
                    var pendingOperation = await context.OutboxMessages.FindAsync([operation.OperationId], cancellationToken);
                    if (pendingOperation is not null)
                    {
                        pendingOperation.LastError = result.Status switch
                        {
                            CoordinatorOperationSendStatus.NotLinked => "Este dispositivo ya no está vinculado al coordinador.",
                            CoordinatorOperationSendStatus.SessionUnavailable => result.Message ?? "La sesión compartida no está disponible o venció. Cierra sesión e inicia sesión de nuevo con el coordinador disponible.",
                            CoordinatorOperationSendStatus.IdentityMismatch => "La cuenta o identidad del dispositivo no coincide con la vinculación actual.",
                            CoordinatorOperationSendStatus.Unauthorized => result.Message ?? "El coordinador rechazó la autorización. Vuelve a iniciar sesión en el dispositivo; si persiste, revisa la vinculación del teléfono.",
                            CoordinatorOperationSendStatus.Rejected => "El coordinador rechazó la operación. Actualiza el coordinador y vuelve a intentar.",
                            CoordinatorOperationSendStatus.Unavailable => "El coordinador no pudo confirmar la operación. Revisa su actualización y conexión LAN.",
                            _ => "La operación sigue pendiente y se volverá a intentar."
                        };
                        await context.SaveChangesAsync(cancellationToken);
                    }
                }
                break;
            }
            var changed = await ApplyIncomingChangesAsync(cancellationToken);
            changed |= await RetryDeferredPurchasesAsync(cancellationToken);
            if (changed)
                changeNotifier.NotifyDataChanged();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _lastDownloadError = exception is InvalidOperationException
                ? exception.Message
                : "No se pudieron aplicar los cambios recibidos. Los datos locales se conservan; vuelve a sincronizar.";
            throw;
        }
        finally
        {
            _gate.Release();
        }
        return await GetStatusAsync(cancellationToken);
    }

    public async Task<LocalOperationSyncStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var pending = await context.OutboxMessages.CountAsync(item => item.Status == OutboxStatus.Pending, cancellationToken);
        var conflicts = await context.OutboxMessages.CountAsync(item => item.Status == OutboxStatus.Conflict, cancellationToken) +
            await context.DeviceSyncConflicts.CountAsync(item => !item.IsResolved, cancellationToken);
        var lastConfirmed = await context.OutboxMessages.Where(item => item.ConfirmedAtUtc != null)
            .MaxAsync(item => (DateTime?)item.ConfirmedAtUtc, cancellationToken);
        var lastInbound = await context.DeviceSyncStates.Where(item => item.LastSuccessfulSyncAtUtc != null)
            .MaxAsync(item => item.LastSuccessfulSyncAtUtc, cancellationToken);
        if (lastInbound > lastConfirmed) lastConfirmed = lastInbound;
        var outboxMessage = await context.OutboxMessages.AsNoTracking().Where(item => item.Status == OutboxStatus.Conflict)
            .OrderByDescending(item => item.CreatedAtUtc).Select(item => item.LastError).FirstOrDefaultAsync(cancellationToken);
        var pendingMessage = await context.OutboxMessages.AsNoTracking()
            .Where(item => item.Status == OutboxStatus.Pending && item.LastError != null)
            .OrderByDescending(item => item.LastAttemptAtUtc).Select(item => item.LastError).FirstOrDefaultAsync(cancellationToken);
        var inboundMessage = await context.DeviceSyncConflicts.AsNoTracking().Where(item => !item.IsResolved)
            .OrderByDescending(item => item.CoordinatorSequence).Select(item => item.Message).FirstOrDefaultAsync(cancellationToken);
        return new LocalOperationSyncStatus(pending, conflicts, lastConfirmed,
            inboundMessage ?? outboxMessage ?? _lastDownloadError ?? pendingMessage);
    }

    private async Task<bool> ApplyIncomingChangesAsync(CancellationToken cancellationToken)
    {
        var deviceId = await deviceIdentity.GetDeviceIdAsync(cancellationToken);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var state = await context.DeviceSyncStates.SingleOrDefaultAsync(item => item.DeviceId == deviceId, cancellationToken);
        var afterSequence = state?.LastSequence ?? 0;
        var appliedChanges = false;
        state ??= new DeviceSyncStateEntity { DeviceId = deviceId, LastSequence = afterSequence };
        while (true)
        {
            var page = await changeClient.GetChangesAsync(afterSequence, 100, cancellationToken);
            _lastDownloadError = page is null
                ? changeClient.LastChangeError ?? "No se pudieron descargar los cambios del coordinador. Comprueba la sesión central y la conexión LAN."
                : null;
            if (page is null || page.Changes.Count == 0) return appliedChanges;
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            if (context.Entry(state).State == EntityState.Detached) context.DeviceSyncStates.Add(state);
            foreach (var change in page.Changes.OrderBy(item => item.Sequence))
            {
                if (change.Sequence <= afterSequence)
                    throw new InvalidOperationException("El coordinador devolvió una secuencia de cambios que no avanza.");
                var actorId = await EnsureLocalActorAsync(context, change, cancellationToken);
                await context.SaveChangesAsync(cancellationToken);
                var error = await businessApplier.ApplyAsync(
                    (SqliteConnection)context.Database.GetDbConnection(),
                    (SqliteTransaction)transaction.GetDbTransaction(),
                    change.OperationId, change.OperationType, change.PayloadJson, change.CreatedAtUtc, actorId, cancellationToken);
                if (error is not null)
                {
                    if (!await context.DeviceSyncConflicts.AnyAsync(item => item.OperationId == change.OperationId, cancellationToken))
                        context.DeviceSyncConflicts.Add(new DeviceSyncConflictEntity { ConflictId = Guid.NewGuid(), CoordinatorSequence = change.Sequence,
                            OperationId = change.OperationId, OperationType = change.OperationType, PayloadJson = change.PayloadJson,
                            Message = error.Length <= 500 ? error : error[..500], CreatedAtUtc = DateTime.UtcNow, IsResolved = false });
                    await context.SaveChangesAsync(cancellationToken);
                    // The payload is retained for review. Advance the durable cursor
                    // so its resolution and unrelated changes can still be received.
                    state.LastSequence = change.Sequence;
                    await context.SaveChangesAsync(cancellationToken);
                    continue;
                }
                if (change.OperationType is "Product.Resolved" or "Category.Resolved")
                    await ApplyResolutionMetadataAsync(context, change.PayloadJson, cancellationToken);
                var previousConflict = await context.DeviceSyncConflicts.SingleOrDefaultAsync(
                    item => item.OperationId == change.OperationId && !item.IsResolved, cancellationToken);
                if (previousConflict is not null) previousConflict.IsResolved = true;
                state.LastSequence = change.Sequence;
                state.LastSuccessfulSyncAtUtc = DateTime.UtcNow;
                await context.SaveChangesAsync(cancellationToken);
                appliedChanges = true;
            }
            await transaction.CommitAsync(cancellationToken);
            afterSequence = state.LastSequence;
        }
    }

    private async Task<bool> RetryDeferredPurchasesAsync(CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var conflicts = await context.DeviceSyncConflicts.Where(item => !item.IsResolved && item.OperationType == "Purchase.Recorded")
            .OrderBy(item => item.CoordinatorSequence).Take(100).ToListAsync(ct);
        var changed = false;
        foreach (var conflict in conflicts)
        {
            // Recover the original authenticated author from the durable journal.
            var page = await changeClient.GetChangesAsync(conflict.CoordinatorSequence - 1, 1, ct);
            var change = page?.Changes.SingleOrDefault();
            if (change is null || change.OperationId != conflict.OperationId || change.Sequence != conflict.CoordinatorSequence) continue;
            await using var transaction = await context.Database.BeginTransactionAsync(ct);
            var actorId = await EnsureLocalActorAsync(context, change, ct);
            await context.SaveChangesAsync(ct);
            var error = await businessApplier.ApplyAsync((SqliteConnection)context.Database.GetDbConnection(),
                (SqliteTransaction)transaction.GetDbTransaction(), change.OperationId, change.OperationType,
                change.PayloadJson, change.CreatedAtUtc, actorId, ct);
            if (error is null)
            {
                conflict.IsResolved = true;
                await context.SaveChangesAsync(ct);
                changed = true;
            }
            await transaction.CommitAsync(ct);
        }
        return changed;
    }

    private static async Task PublishLocalCoordinatorOperationsAsync(
        LocalDbContext context, SqliteConnection connection, CancellationToken cancellationToken)
    {
        // On Windows the MAUI app writes directly to the coordinator database. Its
        // business change is already durable, so publish the matching Outbox entry
        // into the shared journal instead of sending it back through HTTP.
        var pending = await context.OutboxMessages.AsNoTracking()
            .Where(item => item.Status == OutboxStatus.Pending)
            .OrderBy(item => item.CreatedAtUtc).ThenBy(item => item.OperationId)
            .ToListAsync(cancellationToken);
        if (pending.Count == 0) return;

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var operation in pending)
        {
            var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(operation.PayloadJson));
            await using var insert = connection.CreateCommand();
            insert.Transaction = (SqliteTransaction)transaction;
            insert.CommandText = """
                INSERT OR IGNORE INTO CoordinatorOperations
                    (OperationId, DeviceId, ActorUserId, OperationType, PayloadJson, PayloadHash,
                     CreatedAtUtc, ReceivedAtUtc, Status, ConflictMessage)
                SELECT $id, $device, $actor, $type, $payload, $hash, $created, $received, 1, NULL
                WHERE NOT EXISTS (SELECT 1 FROM CoordinatorOperations WHERE OperationId = $id COLLATE NOCASE);
                """;
            insert.Parameters.AddWithValue("$id", operation.OperationId.ToString("D"));
            insert.Parameters.AddWithValue("$device", operation.DeviceId.ToString("D"));
            insert.Parameters.AddWithValue("$actor", operation.ActorUserId.ToString("D"));
            insert.Parameters.AddWithValue("$type", operation.OperationType);
            insert.Parameters.AddWithValue("$payload", operation.PayloadJson);
            insert.Parameters.AddWithValue("$hash", hash);
            insert.Parameters.AddWithValue("$created", operation.CreatedAtUtc.Ticks);
            insert.Parameters.AddWithValue("$received", DateTime.UtcNow.Ticks);
            await insert.ExecuteNonQueryAsync(cancellationToken);

            await using var verify = connection.CreateCommand();
            verify.Transaction = (SqliteTransaction)transaction;
            verify.CommandText = "SELECT DeviceId, ActorUserId, OperationType, PayloadHash, Status FROM CoordinatorOperations WHERE OperationId=$id COLLATE NOCASE ORDER BY Sequence LIMIT 1;";
            verify.Parameters.AddWithValue("$id", operation.OperationId.ToString("D"));
            await using var reader = await verify.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken) ||
                Guid.Parse(reader.GetString(0)) != operation.DeviceId || Guid.Parse(reader.GetString(1)) != operation.ActorUserId ||
                !string.Equals(reader.GetString(2), operation.OperationType, StringComparison.Ordinal) ||
                !CryptographicOperations.FixedTimeEquals((byte[])reader[3], hash) || reader.GetInt32(4) != 1)
                throw new InvalidOperationException("La operación local ya existe en el diario central con contenido o estado diferente.");
            await reader.DisposeAsync();

            await using var confirm = connection.CreateCommand();
            confirm.Transaction = (SqliteTransaction)transaction;
            confirm.CommandText = "UPDATE OutboxMessages SET Status=1, ConfirmedAtUtc=COALESCE(ConfirmedAtUtc, CreatedAtUtc) WHERE OperationId=$id COLLATE NOCASE AND Status=0;";
            confirm.Parameters.AddWithValue("$id", operation.OperationId.ToString("D"));
            await confirm.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task ApplyResolutionMetadataAsync(LocalDbContext context, string payloadJson, CancellationToken ct)
    {
        using var document = JsonDocument.Parse(payloadJson);
        var operationIdProperty = document.RootElement.EnumerateObject()
            .FirstOrDefault(item => string.Equals(item.Name, "ResolutionOfOperationId", StringComparison.OrdinalIgnoreCase)).Value;
        if (operationIdProperty.ValueKind != JsonValueKind.String || !Guid.TryParse(operationIdProperty.GetString(), out var resolvedOperationId)) return;
        var outbox = await context.OutboxMessages.FindAsync([resolvedOperationId], ct);
        if (outbox?.Status == OutboxStatus.Conflict)
        {
            outbox.Status = OutboxStatus.Confirmed;
            outbox.ConfirmedAtUtc = DateTime.UtcNow;
            outbox.LastError = null;
        }
        var localConflict = await context.DeviceSyncConflicts.SingleOrDefaultAsync(item => item.OperationId == resolvedOperationId && !item.IsResolved, ct);
        if (localConflict is not null) localConflict.IsResolved = true;
    }

    private async Task<Guid> EnsureLocalActorAsync(LocalDbContext context, CoordinatorChange change, CancellationToken cancellationToken)
    {
        var mapped = await context.UserAccounts.SingleOrDefaultAsync(item => item.CoordinatorUserId == change.ActorUserId, cancellationToken);
        if (mapped is not null) return mapped.UserId;
        if (change.ActorRole != (int)UserRole.Administrator && change.ActorRole != (int)UserRole.Cashier)
            throw new InvalidOperationException("El coordinador no incluyó un usuario válido para uno de los cambios. Actualiza el servicio coordinador de Windows y vuelve a sincronizar.");
        var userId = Guid.NewGuid();
        var (salt, hash) = await Task.Run(() => passwordHasher.Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))), cancellationToken);
        var userName = string.IsNullOrWhiteSpace(change.ActorUserName) ? "Usuario sincronizado" : change.ActorUserName.Trim();
        context.UserAccounts.Add(new UserAccountEntity { UserId = userId, CoordinatorUserId = change.ActorUserId,
            UserName = userName, NormalizedUserName = $"__REMOTE_{change.ActorUserId:N}", PasswordSalt = salt, PasswordHash = hash,
            PasswordIterations = Pbkdf2PasswordHasher.Iterations, Role = change.ActorRole.Value, IsEnabled = false, CreatedAtUtc = change.CreatedAtUtc });
        return userId;
    }
}
