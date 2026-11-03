namespace MauiApp1.Application.Sales;

public sealed record SaleReversalRecordedPayload(
    Guid OperationId,
    Guid SaleOperationId,
    DateTime OccurredAtUtc,
    int Kind,
    string Reason,
    IReadOnlyList<SaleReversalRecordedLinePayload> Lines,
    int? RefundMethod);

public sealed record SaleReversalRecordedLinePayload(
    int LineNumber,
    int SaleLineNumber,
    long QuantityMilliUnits,
    bool ReturnToStock,
    string? DisposalReason);
