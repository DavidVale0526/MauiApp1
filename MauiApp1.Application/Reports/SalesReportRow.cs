namespace MauiApp1.Application.Reports;

public sealed record SalesReportRow(
    Guid OperationId,
    DateTime OccurredAtUtc,
    string ActorUserName,
    bool IsCredit,
    string? CustomerName,
    long TotalMinorUnits);
