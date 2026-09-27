using MauiApp1.Application.Storage;
using MauiApp1.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

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
        CancellationToken cancellationToken = default)
    {
        if (maximumCount is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCount), "El lote debe contener entre 1 y 1000 operaciones.");
        }

        return await context.OutboxMessages
            .AsNoTracking()
            .Where(item => item.Status == OutboxStatus.Pending)
            .OrderBy(item => item.CreatedAtUtc)
            .ThenBy(item => item.OperationId)
            .Take(maximumCount)
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
    }

    public async Task RecordAttemptAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        var message = await context.OutboxMessages.FindAsync([operationId], cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("No existe la operación indicada en la cola local.");

        if (message.Status == OutboxStatus.Confirmed)
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

        if (message.Status == OutboxStatus.Confirmed)
        {
            return;
        }

        message.Status = OutboxStatus.Confirmed;
        message.ConfirmedAtUtc = confirmedAtUtc;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
