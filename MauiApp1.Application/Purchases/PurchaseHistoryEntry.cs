namespace MauiApp1.Application.Purchases;

public sealed record PurchaseHistoryEntry(
    Guid OperationId,
    DateTime OccurredAtUtc,
    IReadOnlyList<PurchaseHistoryLine> Lines);

public sealed record PurchaseHistoryLine(
    string ProductName,
    string Unit,
    long QuantityMilliUnits);
