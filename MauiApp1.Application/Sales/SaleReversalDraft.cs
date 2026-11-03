namespace MauiApp1.Application.Sales;

public enum SaleReversalKind
{
    Cancellation = 1,
    Return = 2
}

public enum SaleRefundMethod
{
    Cash = 1,
    Transfer = 2,
    Card = 3
}

public sealed record SaleReversalLineDraft(
    int SaleLineNumber,
    long QuantityMilliUnits,
    bool ReturnToStock,
    string? DisposalReason = null,
    long AmountMinorUnits = 0);

public sealed record SaleReversalDraft(
    Guid OperationId,
    Guid SaleOperationId,
    DateTime OccurredAtUtc,
    SaleReversalKind Kind,
    string Reason,
    IReadOnlyList<SaleReversalLineDraft> Lines,
    SaleRefundMethod? RefundMethod = null);

public sealed record SaleReversalEntry(
    Guid OperationId,
    Guid SaleOperationId,
    DateTime OccurredAtUtc,
    string ActorUserName,
    SaleReversalKind Kind,
    string Reason,
    long AmountMinorUnits,
    SaleRefundMethod? RefundMethod,
    IReadOnlyList<SaleReversalLineDraft> Lines);
