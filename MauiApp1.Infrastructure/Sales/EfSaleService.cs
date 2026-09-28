using System.Text.Json;
using MauiApp1.Application.Authentication;
using MauiApp1.Application.Inventory;
using MauiApp1.Application.Sales;
using MauiApp1.Domain.Identifiers;
using MauiApp1.Domain.Inventory;
using MauiApp1.Domain.Values;
using MauiApp1.Infrastructure.Inventory;
using MauiApp1.Infrastructure.Persistence;
using MauiApp1.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MauiApp1.Infrastructure.Sales;

internal sealed class EfSaleService(
    IDbContextFactory<LocalDbContext> contextFactory,
    InventoryMovementWriter movementWriter,
    IAuthenticationService authenticationService) : ISaleService
{
    private const string OutboxOperationType = "Sale.Recorded";

    public async Task<SaleResult> RecordAsync(SaleDraft sale, CancellationToken cancellationToken = default)
    {
        var actor = authenticationService.CurrentUser;
        if (actor is null)
        {
            return SaleResult.Failure("Inicia sesión para registrar una venta.");
        }

        if (sale is null || sale.OperationId == Guid.Empty || sale.OccurredAtUtc.Kind != DateTimeKind.Utc ||
            sale.Lines is null || sale.Lines.Count == 0 || sale.Lines.Any(line =>
                line.ProductId.IsEmpty || line.Quantity.MilliUnits <= 0 || line.SalePrice.MinorUnits < 0 ||
                string.IsNullOrWhiteSpace(line.ProductNameSnapshot) || string.IsNullOrWhiteSpace(line.UnitSnapshot)))
        {
            return SaleResult.Failure("La venta requiere fecha UTC, productos válidos, cantidades positivas y precios no negativos.");
        }

        List<(Guid ProductId, string Name, string Unit, long Quantity, long Price, long LineTotal)> lines;
        long totalMinorUnits;
        try
        {
            lines = sale.Lines
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
                                          line.UnitSnapshot != first.UnitSnapshot || line.SalePrice != first.SalePrice))
                    {
                        throw new ArgumentException("Un mismo producto debe conservar el mismo nombre, unidad y precio durante la venta.");
                    }

                    var quantity = group.Aggregate(0L, (sum, line) => checked(sum + line.Quantity.MilliUnits));
                    var lineTotal = SaleAmountCalculator.CalculateLineTotalMinorUnits(first.SalePrice, new Quantity(quantity));
                    return (group.Key, first.ProductNameSnapshot, first.UnitSnapshot, quantity,
                        first.SalePrice.MinorUnits, lineTotal);
                })
                .ToList();
            totalMinorUnits = lines.Aggregate(0L, (sum, line) => checked(sum + line.LineTotal));
        }
        catch (ArgumentException exception)
        {
            return SaleResult.Failure(exception.Message);
        }
        catch (OverflowException)
        {
            return SaleResult.Failure("El total de la venta supera el valor permitido.");
        }

        var movementLines = lines.Select((line, index) => new InventoryMovementLineDraft(
            index,
            new ProductId(line.ProductId),
            InventoryMovementType.SaleExit,
            new Quantity(line.Quantity),
            ProductNameSnapshot: line.Name,
            UnitSnapshot: line.Unit)).ToArray();
        var operation = new InventoryOperationDraft(
            sale.OperationId,
            sale.OccurredAtUtc,
            movementLines,
            NegativeStockPolicy.Reject);
        var payload = JsonSerializer.Serialize(new SaleRecordedPayload(
            sale.OperationId,
            sale.OccurredAtUtc,
            lines.Select((line, index) => new SaleRecordedLinePayload(
                index, line.ProductId, line.Name, line.Unit, line.Quantity, line.Price, line.LineTotal)).ToArray(),
            totalMinorUnits));

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
            return SaleResult.Failure(stageResult.ErrorMessage ?? "No se pudo preparar la venta.");
        }

        if (stageResult.AlreadyApplied)
        {
            var existingSale = await context.Sales.AsNoTracking().SingleOrDefaultAsync(
                existing => existing.OperationId == sale.OperationId && existing.ActorUserId == actor.Id.Value,
                cancellationToken);
            var persistedLineCount = existingSale is null ? 0 : await context.SaleLines.AsNoTracking()
                .CountAsync(line => line.OperationId == sale.OperationId, cancellationToken);
            return existingSale is not null && existingSale.TotalMinorUnits == totalMinorUnits && persistedLineCount == lines.Count
                ? SaleResult.Success(alreadyApplied: true)
                : SaleResult.Failure("La operación ya existe, pero no se encontró la venta completa asociada. No se modificó el inventario.");
        }

        context.Sales.Add(new SaleEntity
        {
            OperationId = sale.OperationId,
            OccurredAtUtc = sale.OccurredAtUtc,
            ActorUserId = actor.Id.Value,
            TotalMinorUnits = totalMinorUnits
        });
        foreach (var (line, index) in lines.Select((line, index) => (line, index)))
        {
            context.SaleLines.Add(new SaleLineEntity
            {
                OperationId = sale.OperationId,
                LineNumber = index,
                ProductId = line.ProductId,
                ProductNameSnapshot = line.Name,
                UnitSnapshot = line.Unit,
                QuantityMilliUnits = line.Quantity,
                SalePriceMinorUnits = line.Price,
                LineTotalMinorUnits = line.LineTotal
            });
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return SaleResult.Success();
        }
        catch (DbUpdateException)
        {
            return SaleResult.Failure("No se pudo guardar la venta completa. Reintenta con el mismo identificador de operación.");
        }
    }
}
