namespace MauiApp1.Application.Sales;

public interface ISaleService
{
    Task<SaleResult> RecordAsync(SaleDraft sale, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SaleHistoryEntry>> GetHistoryAsync(CancellationToken cancellationToken = default);
}
