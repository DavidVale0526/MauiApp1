namespace MauiApp1.Application.Sales;

public sealed record SaleRecordedPayload(
    Guid OperationId,
    DateTime OccurredAtUtc,
    IReadOnlyList<SaleRecordedLinePayload> Lines,
    long TotalMinorUnits);

public sealed record SaleRecordedLinePayload(
    int LineNumber,
    Guid ProductId,
    string ProductNameSnapshot,
    string UnitSnapshot,
    long QuantityMilliUnits,
    long SalePriceMinorUnits,
    long LineTotalMinorUnits);
