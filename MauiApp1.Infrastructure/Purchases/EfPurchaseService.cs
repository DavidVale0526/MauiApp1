using System.Text.Json;
using MauiApp1.Application.Authentication;
using MauiApp1.Application.Inventory;
using MauiApp1.Application.Purchases;
using MauiApp1.Domain.Identifiers;
using MauiApp1.Domain.Inventory;
using MauiApp1.Domain.Values;
using MauiApp1.Infrastructure.Inventory;
using MauiApp1.Infrastructure.Persistence;
using MauiApp1.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MauiApp1.Infrastructure.Purchases;

internal sealed class EfPurchaseService(
    IDbContextFactory<LocalDbContext> contextFactory,
    InventoryMovementWriter movementWriter,
    IAuthenticationService authenticationService) : IPurchaseService
{
    private const string OutboxOperationType = "Purchase.Recorded";

    public async Task<PurchaseResult> RecordAsync(PurchaseDraft purchase, CancellationToken cancellationToken = default)
    {
        var actor = authenticationService.CurrentUser;
        if (actor is null)
        {
            return PurchaseResult.Failure("Inicia sesión para registrar una compra.");
        }

        if (purchase is null || purchase.OperationId == Guid.Empty || purchase.OccurredAtUtc.Kind != DateTimeKind.Utc ||
            purchase.Lines is null || purchase.Lines.Count == 0 ||
            purchase.Lines.Any(line => line.ProductId.IsEmpty || line.Quantity.MilliUnits <= 0))
        {
            return PurchaseResult.Failure("La compra requiere fecha UTC, productos válidos y cantidades mayores que cero.");
        }

        List<(Guid ProductId, long QuantityMilliUnits)> normalizedLines;
        try
        {
            normalizedLines = purchase.Lines
                .GroupBy(line => line.ProductId.Value)
                .Select(group => (ProductId: group.Key,
                    QuantityMilliUnits: group.Aggregate(0L, (total, line) => checked(total + line.Quantity.MilliUnits))))
                .ToList();
        }
        catch (OverflowException)
        {
            return PurchaseResult.Failure("La cantidad total de un producto supera el valor permitido.");
        }

        var movementLines = normalizedLines.Select((line, index) => new InventoryMovementLineDraft(
            index,
            new ProductId(line.ProductId),
            InventoryMovementType.PurchaseEntry,
            new Quantity(line.QuantityMilliUnits))).ToArray();
        var operation = new InventoryOperationDraft(
            purchase.OperationId,
            purchase.OccurredAtUtc,
            movementLines,
            NegativeStockPolicy.Reject);
        var payload = JsonSerializer.Serialize(new PurchaseRecordedPayload(
            purchase.OperationId,
            purchase.OccurredAtUtc,
            normalizedLines.Select((line, index) => new PurchaseRecordedLinePayload(
                index, line.ProductId, line.QuantityMilliUnits)).ToArray()));

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var stageResult = await movementWriter.StageAsync(
            context,
            operation,
            actor.Id.Value,
            OutboxOperationType,
            payload,
            requireActiveProducts: true,
            cancellationToken);
        if (!stageResult.Succeeded)
        {
            return PurchaseResult.Failure(stageResult.ErrorMessage ?? "No se pudo preparar la compra.");
        }

        if (stageResult.AlreadyApplied)
        {
            var hasPurchase = await context.Purchases.AnyAsync(
                existing => existing.OperationId == purchase.OperationId && existing.ActorUserId == actor.Id.Value,
                cancellationToken);
            return hasPurchase
                ? PurchaseResult.Success(alreadyApplied: true)
                : PurchaseResult.Failure("La operación ya existe, pero no se encontró su compra asociada. No se modificó el inventario.");
        }

        context.Purchases.Add(new PurchaseEntity
        {
            OperationId = purchase.OperationId,
            OccurredAtUtc = purchase.OccurredAtUtc,
            ActorUserId = actor.Id.Value
        });
        foreach (var (line, index) in normalizedLines.Select((line, index) => (line, index)))
        {
            context.PurchaseLines.Add(new PurchaseLineEntity
            {
                OperationId = purchase.OperationId,
                LineNumber = index,
                ProductId = line.ProductId,
                QuantityMilliUnits = line.QuantityMilliUnits
            });
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return PurchaseResult.Success();
        }
        catch (DbUpdateException)
        {
            return PurchaseResult.Failure("No se pudo guardar la compra completa. Inténtalo de nuevo con el mismo identificador de operación.");
        }
    }
}
