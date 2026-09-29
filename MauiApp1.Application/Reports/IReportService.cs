using MauiApp1.Domain.Inventory;

namespace MauiApp1.Application.Reports;

public interface IReportService
{
    Task<IReadOnlyList<SalesReportRow>> GetSalesAsync(
        DateTime fromUtcInclusive,
        DateTime toUtcExclusive,
        bool? isCredit,
        Guid? customerId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InventoryReportRow>> GetInventoryAsync(
        Guid? categoryId,
        bool? isActive,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MovementReportRow>> GetMovementsAsync(
        DateTime fromUtcInclusive,
        DateTime toUtcExclusive,
        Guid? productId,
        InventoryMovementType? type,
        CancellationToken cancellationToken = default);
}
