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
                string.IsNullOrWhiteSpace(line.ProductNameSnapshot) || string.IsNullOrWhiteSpace(line.UnitSnapshot)) ||
            sale.IsCredit != sale.CustomerId.HasValue || sale.IsCredit != sale.CreditDueAtUtc.HasValue ||
            (sale.CreditDueAtUtc.HasValue && sale.CreditDueAtUtc.Value.Kind != DateTimeKind.Utc))
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
            totalMinorUnits,
            sale.IsCredit,
            sale.CustomerId,
            sale.CreditDueAtUtc));

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (sale.IsCredit)
        {
            if (!await context.Customers.AnyAsync(customer => customer.CustomerId == sale.CustomerId, cancellationToken))
            {
                return SaleResult.Failure("El cliente seleccionado ya no existe. Actualiza la lista e inténtalo de nuevo.");
            }

            if (sale.CreditDueAtUtc != sale.OccurredAtUtc.AddDays(15))
            {
                return SaleResult.Failure("La fecha de vencimiento debe ser 15 días después de la fecha de venta.");
            }
        }

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
            var persistedReceivable = sale.IsCredit
                ? await context.Receivables.AsNoTracking().SingleOrDefaultAsync(item => item.OperationId == sale.OperationId, cancellationToken)
                : null;
            var creditMatches = sale.IsCredit
                ? persistedReceivable is not null && persistedReceivable.CustomerId == sale.CustomerId &&
                  persistedReceivable.OriginalAmountMinorUnits == totalMinorUnits &&
                  persistedReceivable.OutstandingMinorUnits == totalMinorUnits && persistedReceivable.DueAtUtc == sale.CreditDueAtUtc
                : !await context.Receivables.AnyAsync(item => item.OperationId == sale.OperationId, cancellationToken);
            return existingSale is not null && existingSale.TotalMinorUnits == totalMinorUnits &&
                   existingSale.IsCredit == sale.IsCredit && existingSale.CustomerId == sale.CustomerId &&
                   persistedLineCount == lines.Count && creditMatches
                ? SaleResult.Success(alreadyApplied: true)
                : SaleResult.Failure("La operación ya existe, pero no se encontró la venta y deuda completas asociadas. No se modificó el inventario.");
        }

        context.Sales.Add(new SaleEntity
        {
            OperationId = sale.OperationId,
            OccurredAtUtc = sale.OccurredAtUtc,
            ActorUserId = actor.Id.Value,
            TotalMinorUnits = totalMinorUnits,
            IsCredit = sale.IsCredit,
            CustomerId = sale.CustomerId
        });
        if (sale.IsCredit)
        {
            context.Receivables.Add(new ReceivableEntity
            {
                OperationId = sale.OperationId,
                CustomerId = sale.CustomerId!.Value,
                OriginalAmountMinorUnits = totalMinorUnits,
                OutstandingMinorUnits = totalMinorUnits,
                DueAtUtc = sale.CreditDueAtUtc!.Value
            });
        }
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

    public async Task<IReadOnlyList<SaleHistoryEntry>> GetHistoryAsync(CancellationToken cancellationToken = default)
    {
        if (authenticationService.CurrentUser is null)
        {
            return [];
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var sales = await (
            from sale in context.Sales.AsNoTracking()
            join actor in context.UserAccounts.AsNoTracking() on sale.ActorUserId equals actor.UserId
            orderby sale.OccurredAtUtc descending, sale.OperationId descending
            select new
            {
                sale.OperationId,
                sale.OccurredAtUtc,
                ActorUserName = actor.UserName,
                sale.IsCredit,
                sale.CustomerId,
                sale.TotalMinorUnits
            }).ToListAsync(cancellationToken);
        if (sales.Count == 0)
        {
            return [];
        }

        var operationIds = sales.Select(sale => sale.OperationId).ToArray();
        var lines = await context.SaleLines.AsNoTracking()
            .Where(line => operationIds.Contains(line.OperationId))
            .OrderBy(line => line.LineNumber)
            .Select(line => new
            {
                line.OperationId,
                line.ProductNameSnapshot,
                line.UnitSnapshot,
                line.QuantityMilliUnits,
                line.SalePriceMinorUnits,
                line.LineTotalMinorUnits
            })
            .ToListAsync(cancellationToken);
        var linesBySale = lines.GroupBy(line => line.OperationId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<SaleHistoryLine>)group.Select(line => new SaleHistoryLine(
                    line.ProductNameSnapshot,
                    line.UnitSnapshot,
                    line.QuantityMilliUnits,
                    line.SalePriceMinorUnits,
                    line.LineTotalMinorUnits)).ToArray());

        var customerIds = sales.Where(sale => sale.CustomerId.HasValue).Select(sale => sale.CustomerId!.Value).Distinct().ToArray();
        var customerNames = customerIds.Length == 0
            ? new Dictionary<Guid, string>()
            : await context.Customers.AsNoTracking()
                .Where(customer => customerIds.Contains(customer.CustomerId))
                .ToDictionaryAsync(customer => customer.CustomerId, customer => customer.Name, cancellationToken);
        var dueDates = await context.Receivables.AsNoTracking()
            .Where(item => operationIds.Contains(item.OperationId))
            .ToDictionaryAsync(item => item.OperationId, item => item.DueAtUtc, cancellationToken);

        return sales.Select(sale => new SaleHistoryEntry(
            sale.OperationId,
            sale.OccurredAtUtc,
            sale.ActorUserName,
            sale.TotalMinorUnits,
            linesBySale.GetValueOrDefault(sale.OperationId, []),
            sale.IsCredit,
            sale.CustomerId.HasValue ? customerNames.GetValueOrDefault(sale.CustomerId.Value) : null,
            dueDates.TryGetValue(sale.OperationId, out var dueAtUtc) ? dueAtUtc : null)).ToArray();
    }
}
