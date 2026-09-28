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
            purchase.Lines.Any(line => line.ProductId.IsEmpty || line.Quantity.MilliUnits <= 0 ||
                                       string.IsNullOrWhiteSpace(line.ProductNameSnapshot) ||
                                       string.IsNullOrWhiteSpace(line.UnitSnapshot)))
        {
            return PurchaseResult.Failure("La compra requiere fecha UTC, productos válidos y cantidades mayores que cero.");
        }

        List<(Guid ProductId, string ProductNameSnapshot, string UnitSnapshot, long QuantityMilliUnits)> normalizedLines;
        try
        {
            normalizedLines = purchase.Lines
                .Select(line => line with
                {
                    ProductNameSnapshot = line.ProductNameSnapshot.Trim(),
                    UnitSnapshot = line.UnitSnapshot.Trim()
                })
                .GroupBy(line => line.ProductId.Value)
                .Select(group =>
                {
                    var first = group.First();
                    if (group.Any(line => line.ProductNameSnapshot != first.ProductNameSnapshot ||
                                          line.UnitSnapshot != first.UnitSnapshot))
                    {
                        throw new ArgumentException("Un mismo producto debe conservar el mismo nombre y unidad en toda la compra.");
                    }

                    return (ProductId: group.Key,
                        ProductNameSnapshot: first.ProductNameSnapshot,
                        UnitSnapshot: first.UnitSnapshot,
                        QuantityMilliUnits: group.Aggregate(0L, (total, line) => checked(total + line.Quantity.MilliUnits)));
                })
                .ToList();
        }
        catch (ArgumentException exception)
        {
            return PurchaseResult.Failure(exception.Message);
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
                index, line.ProductId, line.ProductNameSnapshot, line.UnitSnapshot, line.QuantityMilliUnits)).ToArray()));

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
            var existingPurchase = await context.Purchases.AsNoTracking().SingleOrDefaultAsync(
                existing => existing.OperationId == purchase.OperationId && existing.ActorUserId == actor.Id.Value,
                cancellationToken);
            var persistedLineCount = existingPurchase is null ? 0 : await context.PurchaseLines.AsNoTracking()
                .CountAsync(line => line.OperationId == purchase.OperationId, cancellationToken);
            return existingPurchase is not null && persistedLineCount == normalizedLines.Count
                ? PurchaseResult.Success(alreadyApplied: true)
                : PurchaseResult.Failure("La operación ya existe, pero no se encontró su compra asociada. No se modificó el inventario.");
        }

        var productIds = normalizedLines.Select(line => line.ProductId).ToArray();
        var currentProducts = await context.Products.AsNoTracking()
            .Where(product => productIds.Contains(product.ProductId))
            .ToDictionaryAsync(product => product.ProductId, cancellationToken);
        if (currentProducts.Count != normalizedLines.Count || normalizedLines.Any(line =>
                !currentProducts.TryGetValue(line.ProductId, out var product) ||
                !product.IsActive || product.Name != line.ProductNameSnapshot || product.Unit != line.UnitSnapshot))
        {
            return PurchaseResult.Failure("Un producto cambió desde que se agregó a la compra. Actualiza la pantalla y vuelve a prepararla.");
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
                ProductNameSnapshot = line.ProductNameSnapshot,
                UnitSnapshot = line.UnitSnapshot,
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

    public async Task<IReadOnlyList<PurchaseHistoryEntry>> GetHistoryAsync(CancellationToken cancellationToken = default)
    {
        if (authenticationService.CurrentUser is null)
        {
            return [];
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var purchases = await context.Purchases.AsNoTracking()
            .OrderByDescending(purchase => purchase.OccurredAtUtc)
            .ThenByDescending(purchase => purchase.OperationId)
            .Select(purchase => new { purchase.OperationId, purchase.OccurredAtUtc })
            .ToListAsync(cancellationToken);
        if (purchases.Count == 0)
        {
            return [];
        }

        var operationIds = purchases.Select(purchase => purchase.OperationId).ToArray();
        var lines = await context.PurchaseLines.AsNoTracking()
            .Where(line => operationIds.Contains(line.OperationId))
            .OrderBy(line => line.LineNumber)
            .Select(line => new
            {
                line.OperationId,
                line.ProductNameSnapshot,
                line.UnitSnapshot,
                line.QuantityMilliUnits
            })
            .ToListAsync(cancellationToken);
        var linesByOperation = lines.GroupBy(line => line.OperationId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<PurchaseHistoryLine>)group.Select(line => new PurchaseHistoryLine(
                    line.ProductNameSnapshot,
                    line.UnitSnapshot,
                    line.QuantityMilliUnits)).ToArray());

        return purchases.Select(purchase => new PurchaseHistoryEntry(
            purchase.OperationId,
            purchase.OccurredAtUtc,
            linesByOperation.GetValueOrDefault(purchase.OperationId, []))).ToArray();
    }
}
