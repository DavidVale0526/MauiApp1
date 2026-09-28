namespace MauiApp1.Application.Products;

/// <summary>Snapshot durable para que el coordinador aplique la misma versión del producto.</summary>
public sealed record ProductChangedPayload(
    Guid ProductId,
    string Name,
    long SalePriceMinorUnits,
    long StockMilliUnits,
    string Unit,
    Guid CategoryId,
    long MinimumStockMilliUnits,
    bool IsActive);
