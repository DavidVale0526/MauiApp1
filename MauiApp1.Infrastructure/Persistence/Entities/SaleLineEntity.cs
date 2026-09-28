namespace MauiApp1.Infrastructure.Persistence.Entities;

internal sealed class SaleLineEntity
{
    public Guid OperationId { get; set; }

    public int LineNumber { get; set; }

    public Guid ProductId { get; set; }

    public string ProductNameSnapshot { get; set; } = string.Empty;

    public string UnitSnapshot { get; set; } = string.Empty;

    public long QuantityMilliUnits { get; set; }

    public long SalePriceMinorUnits { get; set; }

    public long LineTotalMinorUnits { get; set; }
}
