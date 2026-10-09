using MauiApp1.Application.Storage;
using MauiApp1.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace MauiApp1.Infrastructure.Persistence;

internal sealed class EfOperationOutbox(LocalDbContext context, ILocalDeviceIdentity deviceIdentity) : IOperationOutbox
{
    public async Task EnqueueAsync(NewOutboxOperation operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (operation.OperationId == Guid.Empty)
        {
            throw new ArgumentException("La operación requiere un identificador estable.", nameof(operation));
        }

        if (operation.ActorUserId == Guid.Empty)
        {
            throw new ArgumentException("La operación requiere el identificador del usuario responsable.", nameof(operation));
        }

        if (string.IsNullOrWhiteSpace(operation.OperationType) || operation.OperationType.Length > 120)
        {
            throw new ArgumentException("El tipo de operación debe tener entre 1 y 120 caracteres.", nameof(operation));
        }

        if (string.IsNullOrWhiteSpace(operation.PayloadJson))
        {
            throw new ArgumentException("La operación requiere un contenido JSON.", nameof(operation));
        }

        var existingTracked = context.OutboxMessages.Local.SingleOrDefault(item => item.OperationId == operation.OperationId);
        var existing = existingTracked ?? await context.OutboxMessages
            .SingleOrDefaultAsync(item => item.OperationId == operation.OperationId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            if (existing.ActorUserId != operation.ActorUserId
                || !string.Equals(existing.OperationType, operation.OperationType, StringComparison.Ordinal)
                || !string.Equals(existing.PayloadJson, operation.PayloadJson, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("El identificador de operación ya existe con otro contenido.");
            }

            return;
        }

        context.OutboxMessages.Add(new OutboxMessageEntity
        {
            OperationId = operation.OperationId,
            DeviceId = await deviceIdentity.GetDeviceIdAsync(cancellationToken).ConfigureAwait(false),
            ActorUserId = operation.ActorUserId,
            OperationType = operation.OperationType,
            PayloadJson = operation.PayloadJson,
            CreatedAtUtc = DateTime.UtcNow,
            Status = OutboxStatus.Pending
        });
    }

    public async Task<IReadOnlyList<OutboxOperation>> GetPendingAsync(
        int maximumCount,
        CancellationToken cancellationToken = default,
        Guid? actorUserId = null)
    {
        if (maximumCount is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCount), "El lote debe contener entre 1 y 1000 operaciones.");
        }

        var conflicts = await context.OutboxMessages.AsNoTracking()
            .Where(item => item.Status == OutboxStatus.Conflict)
            .Select(item => new { item.CreatedAtUtc, item.PayloadJson })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var pending = await context.OutboxMessages
            .AsNoTracking()
            .Where(item => item.Status == OutboxStatus.Pending &&
                (actorUserId == null || item.ActorUserId == actorUserId))
            .OrderBy(item => item.CreatedAtUtc)
            .ThenBy(item => item.OperationId)
            .Select(item => new OutboxOperation(
                item.OperationId,
                item.DeviceId,
                item.ActorUserId,
                item.OperationType,
                item.PayloadJson,
                item.CreatedAtUtc,
                item.AttemptCount))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var blocked = conflicts.Select(item => (item.CreatedAtUtc, Resources: GetResources(item.PayloadJson))).ToArray();
        return pending.Where(item =>
        {
            var resources = GetResources(item.PayloadJson);
            return !blocked.Any(conflict => conflict.CreatedAtUtc <= item.CreatedAtUtc &&
                (resources is null || conflict.Resources is null || resources.Overlaps(conflict.Resources)));
        }).Take(maximumCount).ToArray();
    }

    // Preserve dependent operations, but a conflicted category must not stop an
    // unrelated category or purchase. Unknown payloads remain conservatively blocked.
    private static HashSet<Guid>? GetResources(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var resources = new HashSet<Guid>();
            Visit(document.RootElement);
            return resources.Count == 0 ? null : resources;
            void Visit(JsonElement element)
            {
                if (element.ValueKind == JsonValueKind.Array)
                    foreach (var child in element.EnumerateArray()) Visit(child);
                else if (element.ValueKind == JsonValueKind.Object)
                    foreach (var property in element.EnumerateObject())
                    {
                        if (property.Name.Equals("CategoryId", StringComparison.OrdinalIgnoreCase) ||
                            property.Name.Equals("ProductId", StringComparison.OrdinalIgnoreCase) ||
                            property.Name.Equals("CustomerId", StringComparison.OrdinalIgnoreCase) ||
                            property.Name.Equals("SaleOperationId", StringComparison.OrdinalIgnoreCase))
                        {
                            if (property.Value.ValueKind == JsonValueKind.String && property.Value.TryGetGuid(out var id) && id != Guid.Empty)
                                resources.Add(id);
                        }
                        else Visit(property.Value);
                    }
            }
        }
        catch (JsonException) { return null; }
    }

    public async Task RecordAttemptAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        var message = await context.OutboxMessages.FindAsync([operationId], cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("No existe la operación indicada en la cola local.");

        if (message.Status != OutboxStatus.Pending)
        {
            return;
        }

        message.AttemptCount = checked(message.AttemptCount + 1);
        message.LastAttemptAtUtc = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task MarkConfirmedAsync(
        Guid operationId,
        DateTime confirmedAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (confirmedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("La fecha de confirmación debe estar en UTC.", nameof(confirmedAtUtc));
        }

        var message = await context.OutboxMessages.FindAsync([operationId], cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("No existe la operación indicada en la cola local.");

        if (message.Status != OutboxStatus.Pending)
        {
            return;
        }

        message.Status = OutboxStatus.Confirmed;
        message.ConfirmedAtUtc = confirmedAtUtc;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task MarkConflictAsync(Guid operationId, string message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("El conflicto debe describir el motivo.", nameof(message));
        var outboxMessage = await context.OutboxMessages.FindAsync([operationId], cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("No existe la operación indicada en la cola local.");
        if (outboxMessage.Status != OutboxStatus.Pending) return;
        outboxMessage.Status = OutboxStatus.Conflict;
        outboxMessage.LastError = message.Length <= 500 ? message : message[..500];
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
