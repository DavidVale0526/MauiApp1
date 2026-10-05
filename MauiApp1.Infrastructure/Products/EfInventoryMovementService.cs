using System.Text.Json;
using MauiApp1.Application.Authentication;
using MauiApp1.Application.Inventory;
using MauiApp1.Domain.Inventory;
using MauiApp1.Infrastructure.Inventory;
using MauiApp1.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MauiApp1.Infrastructure.Products;

internal sealed class EfInventoryMovementService(
    IDbContextFactory<LocalDbContext> contextFactory,
    InventoryMovementWriter movementWriter,
    IAuthenticationService authenticationService) : IInventoryMovementService
{
    private const string OutboxOperationType = "Inventory.Recorded";

    public async Task<InventoryOperationResult> ApplyAsync(
        InventoryOperationDraft operation,
        CancellationToken cancellationToken = default)
    {
        var actor = authenticationService.CurrentUser;
        if (actor is null)
        {
            return InventoryOperationResult.Failure("Inicia sesión para registrar movimientos de inventario.");
        }

        if (!InventoryMovementWriter.TryValidate(operation, out var lines, out var validationError))
        {
            return InventoryOperationResult.Failure(validationError);
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var productIds = lines.Where(line => !line.ProductId.IsEmpty).Select(line => line.ProductId.Value).Distinct().ToArray();
        var productSnapshots = await context.Products.AsNoTracking()
            .Where(product => productIds.Contains(product.ProductId))
            .ToDictionaryAsync(product => product.ProductId,
                product => (product.Name, product.Unit), cancellationToken);
        var payloadJson = JsonSerializer.Serialize(new InventoryOperationPayload(
            operation.OperationId,
            operation.OccurredAtUtc,
            lines.Select(line =>
            {
                productSnapshots.TryGetValue(line.ProductId.Value, out var snapshot);
                return new InventoryMovementPayloadLine(
                    line.LineNumber, line.ProductId.Value, line.Type, line.Quantity.MilliUnits, line.Reason,
                    line.ProductNameSnapshot ?? snapshot.Name ?? "Merma mixta (no atribuida)",
                    line.UnitSnapshot ?? snapshot.Unit ?? string.Empty);
            }).ToArray()));

        var operationType = lines[0].Type == InventoryMovementType.MixedWaste
            ? "Inventory.MixedWasteRecorded"
            : OutboxOperationType;
        var result = await movementWriter.StageAsync(
            context, operation, actor.Id.Value, operationType, payloadJson,
            requireActiveProducts: true, cancellationToken);
        if (!result.Succeeded || result.AlreadyApplied)
        {
            return result;
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return InventoryOperationResult.Success();
        }
        catch (DbUpdateException)
        {
            return InventoryOperationResult.Failure("No se pudo guardar el movimiento completo. Inténtalo de nuevo con el mismo identificador de operación.");
        }
    }

    public async Task<IReadOnlyList<InventoryMovementHistoryEntry>> GetHistoryAsync(
        CancellationToken cancellationToken = default)
    {
        if (authenticationService.CurrentUser is null)
        {
            return [];
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await context.InventoryMovements.AsNoTracking()
            .OrderByDescending(movement => movement.OccurredAtUtc)
            .ThenByDescending(movement => movement.MovementId)
            .Select(movement => new
            {
                movement.MovementId,
                movement.OperationId,
                movement.Type,
                movement.ProductNameSnapshot,
                movement.UnitSnapshot,
                movement.QuantityMilliUnits,
                movement.OccurredAtUtc,
                movement.Reason
            })
            .ToListAsync(cancellationToken);

        return rows.Select(row => new InventoryMovementHistoryEntry(
            row.MovementId,
            row.OperationId,
            (InventoryMovementType)row.Type,
            row.ProductNameSnapshot,
            row.UnitSnapshot,
            row.QuantityMilliUnits,
            row.OccurredAtUtc,
            row.Reason)).ToArray();
    }
}
