namespace MauiApp1.Application.Credit;

public interface IPaymentService
{
    Task<PaymentResult> RecordAsync(PaymentDraft payment, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CreditAccountSummary>> GetAccountsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PaymentHistoryEntry>> GetHistoryAsync(
        Guid operationId, CancellationToken cancellationToken = default);
}
