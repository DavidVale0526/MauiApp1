namespace MauiApp1.Application.Purchases;

public sealed record PurchaseRecordedPayload(
    Guid OperationId,
    DateTime OccurredAtUtc,
    IReadOnlyList<PurchaseRecordedLinePayload> Lines);

public sealed record PurchaseRecordedLinePayload(
    int LineNumber,
    Guid ProductId,
    string ProductNameSnapshot,
    string UnitSnapshot,
    long QuantityMilliUnits);
