using MauiApp1.Application.Dashboard;
using MauiApp1.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MauiApp1.Infrastructure.Dashboard;

internal sealed class EfDashboardService(IDbContextFactory<LocalDbContext> contextFactory) : IDashboardService
{
    public async Task<DashboardSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var localToday = DateTime.Today;
        var localTomorrow = localToday.AddDays(1);
        var localMonthStart = new DateTime(localToday.Year, localToday.Month, 1);
        var localWeekStart = localToday.AddDays(-6);
        var todayUtc = LocalBoundaryToUtc(localToday);
        var tomorrowUtc = LocalBoundaryToUtc(localTomorrow);
        var monthStartUtc = LocalBoundaryToUtc(localMonthStart);
        var weekStartUtc = LocalBoundaryToUtc(localWeekStart);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var todaySales = context.Sales.AsNoTracking()
            .Where(sale => sale.OccurredAtUtc >= todayUtc && sale.OccurredAtUtc < tomorrowUtc);
        var salesTodayMinorUnits = await todaySales
            .SumAsync(sale => (long?)sale.TotalMinorUnits, cancellationToken) ?? 0L;
        var salesTodayCount = await todaySales.CountAsync(cancellationToken);

        var salesMonthMinorUnits = await context.Sales.AsNoTracking()
            .Where(sale => sale.OccurredAtUtc >= monthStartUtc && sale.OccurredAtUtc < tomorrowUtc)
            .SumAsync(sale => (long?)sale.TotalMinorUnits, cancellationToken) ?? 0L;

        var weeklySales = await context.Sales.AsNoTracking()
            .Where(sale => sale.OccurredAtUtc >= weekStartUtc && sale.OccurredAtUtc < tomorrowUtc)
            .Select(sale => new { sale.OccurredAtUtc, sale.TotalMinorUnits })
            .ToListAsync(cancellationToken);
        var weeklyTotals = weeklySales
            .GroupBy(sale => sale.OccurredAtUtc.ToLocalTime().Date)
            .ToDictionary(group => group.Key, group => group.Sum(sale => sale.TotalMinorUnits));
        var salesLastSevenDays = Enumerable.Range(0, 7)
            .Select(offset =>
            {
                var date = localWeekStart.AddDays(offset);
                return new DashboardSalesPoint(date, weeklyTotals.GetValueOrDefault(date.Date));
            })
            .ToArray();

        var topProductsQuery =
            from line in context.SaleLines.AsNoTracking()
            join sale in context.Sales.AsNoTracking() on line.OperationId equals sale.OperationId
            where sale.OccurredAtUtc >= monthStartUtc && sale.OccurredAtUtc < tomorrowUtc
            group line by new { line.ProductNameSnapshot, line.UnitSnapshot } into productSales
            select new
            {
                productSales.Key.ProductNameSnapshot,
                productSales.Key.UnitSnapshot,
                QuantityMilliUnits = productSales.Sum(line => line.QuantityMilliUnits)
            };
        var topProducts = await topProductsQuery
            .OrderByDescending(product => product.QuantityMilliUnits)
            .ThenBy(product => product.ProductNameSnapshot)
            .Take(5)
            .Select(product => new DashboardTopProduct(
                product.ProductNameSnapshot,
                product.UnitSnapshot,
                product.QuantityMilliUnits))
            .ToListAsync(cancellationToken);

        var recentSalesQuery =
            from sale in context.Sales.AsNoTracking()
            join actor in context.UserAccounts.AsNoTracking() on sale.ActorUserId equals actor.UserId
            join customerEntry in context.Customers.AsNoTracking()
                on sale.CustomerId equals (Guid?)customerEntry.CustomerId into customerGroup
            from customer in customerGroup.DefaultIfEmpty()
            orderby sale.OccurredAtUtc descending, sale.OperationId descending
            select new DashboardRecentSale(
                sale.OperationId,
                sale.OccurredAtUtc,
                actor.UserName,
                sale.IsCredit,
                customer == null ? null : customer.Name,
                context.SaleLines.Count(line => line.OperationId == sale.OperationId),
                sale.TotalMinorUnits);
        var recentSales = await recentSalesQuery.Take(5).ToListAsync(cancellationToken);

        return new DashboardSnapshot(
            salesTodayMinorUnits,
            salesTodayCount,
            salesMonthMinorUnits,
            salesLastSevenDays,
            topProducts,
            recentSales);
    }

    private static DateTime LocalBoundaryToUtc(DateTime localBoundary) =>
        DateTime.SpecifyKind(localBoundary, DateTimeKind.Local).ToUniversalTime();
}
