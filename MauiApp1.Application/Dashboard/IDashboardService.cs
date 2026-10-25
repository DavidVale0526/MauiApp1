namespace MauiApp1.Application.Dashboard;

public interface IDashboardService
{
    Task<DashboardSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
}

public sealed record DashboardSnapshot(
    long SalesTodayMinorUnits,
    int SalesTodayCount,
    long SalesMonthMinorUnits,
    IReadOnlyList<DashboardSalesPoint> SalesLastSevenDays,
    IReadOnlyList<DashboardTopProduct> TopProducts,
    IReadOnlyList<DashboardRecentSale> RecentSales);

public sealed record DashboardSalesPoint(DateTime LocalDate, long TotalMinorUnits);

public sealed record DashboardTopProduct(string ProductName, string Unit, long QuantityMilliUnits);

public sealed record DashboardRecentSale(
    Guid OperationId,
    DateTime OccurredAtUtc,
    string ActorUserName,
    bool IsCredit,
    string? CustomerName,
    int LineCount,
    long TotalMinorUnits);
