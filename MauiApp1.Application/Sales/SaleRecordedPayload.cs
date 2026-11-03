namespace MauiApp1.Application.Sales;

public sealed record SaleRecordedPayload(
    Guid OperationId,
    DateTime OccurredAtUtc,
    IReadOnlyList<SaleRecordedLinePayload> Lines,
    long TotalMinorUnits,
    bool IsCredit = false,
    Guid? CustomerId = null,
    DateTime? CreditDueAtUtc = null,
    IReadOnlyList<SaleRecordedPaymentPayload>? Payments = null);

public sealed record SaleRecordedLinePayload(
    int LineNumber,
    Guid ProductId,
    string ProductNameSnapshot,
    string UnitSnapshot,
    long QuantityMilliUnits,
    long SalePriceMinorUnits,
    long LineTotalMinorUnits);

public sealed record SaleRecordedPaymentPayload(
    Guid PaymentId,
    int Method,
    long AmountMinorUnits,
    long CashReceivedMinorUnits,
    long ChangeMinorUnits);
