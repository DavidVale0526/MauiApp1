namespace MauiApp1.Application.Credit;

public sealed record CreditAccountSummary(
    Guid OperationId,
    Guid CustomerId,
    string CustomerName,
    DateTime SaleOccurredAtUtc,
    DateTime DueAtUtc,
    long OriginalAmountMinorUnits,
    long OutstandingMinorUnits);
