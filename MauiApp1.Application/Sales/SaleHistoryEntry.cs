namespace MauiApp1.Application.Sales;

public sealed record SaleHistoryEntry(
    Guid OperationId,
    DateTime OccurredAtUtc,
    string ActorUserName,
    long TotalMinorUnits,
    IReadOnlyList<SaleHistoryLine> Lines);

public sealed record SaleHistoryLine(
    string ProductName,
    string Unit,
    long QuantityMilliUnits,
    long SalePriceMinorUnits,
    long LineTotalMinorUnits);
