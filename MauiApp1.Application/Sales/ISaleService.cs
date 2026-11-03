namespace MauiApp1.Application.Sales;

public interface ISaleService
{
    Task<SaleResult> RecordAsync(SaleDraft sale, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SaleHistoryEntry>> GetHistoryAsync(CancellationToken cancellationToken = default);

    Task<SaleReversalResult> RecordReversalAsync(SaleReversalDraft reversal, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SaleReversalEntry>> GetReversalsAsync(Guid saleOperationId, CancellationToken cancellationToken = default);
}
