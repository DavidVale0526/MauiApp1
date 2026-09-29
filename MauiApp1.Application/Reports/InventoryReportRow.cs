namespace MauiApp1.Application.Reports;

public sealed record InventoryReportRow(
    Guid ProductId,
    string ProductName,
    Guid CategoryId,
    string CategoryName,
    bool IsActive,
    string Unit,
    long StockMilliUnits,
    long MinimumStockMilliUnits);
