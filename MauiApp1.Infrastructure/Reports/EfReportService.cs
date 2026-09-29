using MauiApp1.Application.Authentication;
using MauiApp1.Application.Reports;
using MauiApp1.Domain.Inventory;
using MauiApp1.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MauiApp1.Infrastructure.Reports;

internal sealed class EfReportService(
    IDbContextFactory<LocalDbContext> contextFactory,
    IAuthenticationService authenticationService) : IReportService
{
    public async Task<IReadOnlyList<SalesReportRow>> GetSalesAsync(
        DateTime fromUtcInclusive,
        DateTime toUtcExclusive,
        bool? isCredit,
        Guid? customerId,
        CancellationToken cancellationToken = default)
    {
        EnsureSignedIn();
        if (!IsValidRange(fromUtcInclusive, toUtcExclusive)) return [];

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query =
            from sale in context.Sales.AsNoTracking()
            join actor in context.UserAccounts.AsNoTracking() on sale.ActorUserId equals actor.UserId
            join customerEntry in context.Customers.AsNoTracking()
                on sale.CustomerId equals (Guid?)customerEntry.CustomerId into customerGroup
            from customer in customerGroup.DefaultIfEmpty()
            where sale.OccurredAtUtc >= fromUtcInclusive && sale.OccurredAtUtc < toUtcExclusive
            select new { Sale = sale, ActorName = actor.UserName, CustomerName = customer == null ? null : customer.Name };

        if (isCredit.HasValue)
            query = query.Where(item => item.Sale.IsCredit == isCredit.Value);
        if (customerId.HasValue)
            query = query.Where(item => item.Sale.CustomerId == customerId.Value);

        return await query
            .OrderByDescending(item => item.Sale.OccurredAtUtc)
            .ThenByDescending(item => item.Sale.OperationId)
            .Select(item => new SalesReportRow(
                item.Sale.OperationId,
                item.Sale.OccurredAtUtc,
                item.ActorName,
                item.Sale.IsCredit,
                item.CustomerName,
                item.Sale.TotalMinorUnits))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<InventoryReportRow>> GetInventoryAsync(
        Guid? categoryId,
        bool? isActive,
        CancellationToken cancellationToken = default)
    {
        EnsureSignedIn();
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query =
            from product in context.Products.AsNoTracking()
            join category in context.Categories.AsNoTracking() on product.CategoryId equals category.CategoryId
            select new { Product = product, CategoryName = category.Name };

        if (categoryId.HasValue)
            query = query.Where(item => item.Product.CategoryId == categoryId.Value);
        if (isActive.HasValue)
            query = query.Where(item => item.Product.IsActive == isActive.Value);

        return await query
            .OrderBy(item => item.CategoryName)
            .ThenBy(item => item.Product.Name)
            .Select(item => new InventoryReportRow(
                item.Product.ProductId,
                item.Product.Name,
                item.Product.CategoryId,
                item.CategoryName,
                item.Product.IsActive,
                item.Product.Unit,
                item.Product.StockMilliUnits,
                item.Product.MinimumStockMilliUnits))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MovementReportRow>> GetMovementsAsync(
        DateTime fromUtcInclusive,
        DateTime toUtcExclusive,
        Guid? productId,
        InventoryMovementType? type,
        CancellationToken cancellationToken = default)
    {
        EnsureSignedIn();
        if (!IsValidRange(fromUtcInclusive, toUtcExclusive)) return [];

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query =
            from movement in context.InventoryMovements.AsNoTracking()
            join actor in context.UserAccounts.AsNoTracking() on movement.ActorUserId equals actor.UserId
            where movement.OccurredAtUtc >= fromUtcInclusive && movement.OccurredAtUtc < toUtcExclusive
            select new { Movement = movement, ActorName = actor.UserName };

        if (productId.HasValue)
            query = query.Where(item => item.Movement.ProductId == productId.Value);
        if (type.HasValue)
            query = query.Where(item => item.Movement.Type == (int)type.Value);

        return await query
            .OrderByDescending(item => item.Movement.OccurredAtUtc)
            .ThenByDescending(item => item.Movement.MovementId)
            .Select(item => new MovementReportRow(
                item.Movement.MovementId,
                item.Movement.OperationId,
                (InventoryMovementType)item.Movement.Type,
                item.Movement.ProductNameSnapshot,
                item.Movement.UnitSnapshot,
                item.Movement.QuantityMilliUnits,
                item.Movement.OccurredAtUtc,
                item.Movement.Reason,
                item.ActorName))
            .ToListAsync(cancellationToken);
    }

    private void EnsureSignedIn()
    {
        if (authenticationService.CurrentUser is null)
            throw new UnauthorizedAccessException("Inicia sesión para consultar los reportes.");
    }

    private static bool IsValidRange(DateTime fromUtcInclusive, DateTime toUtcExclusive) =>
        fromUtcInclusive.Kind == DateTimeKind.Utc && toUtcExclusive.Kind == DateTimeKind.Utc &&
        fromUtcInclusive < toUtcExclusive;
}
