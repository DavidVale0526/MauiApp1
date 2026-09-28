namespace MauiApp1.Infrastructure.Persistence.Entities;

internal sealed class ProductEntity
{
    public Guid ProductId { get; set; }

    public string Name { get; set; } = string.Empty;

    public long SalePriceMinorUnits { get; set; }

    public long StockMilliUnits { get; set; }

    public string Unit { get; set; } = string.Empty;

    public Guid CategoryId { get; set; }

    public long MinimumStockMilliUnits { get; set; }

    public bool IsActive { get; set; }
}
