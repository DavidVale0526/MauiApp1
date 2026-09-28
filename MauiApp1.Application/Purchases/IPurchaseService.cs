namespace MauiApp1.Application.Purchases;

public interface IPurchaseService
{
    Task<PurchaseResult> RecordAsync(PurchaseDraft purchase, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PurchaseHistoryEntry>> GetHistoryAsync(CancellationToken cancellationToken = default);
}
