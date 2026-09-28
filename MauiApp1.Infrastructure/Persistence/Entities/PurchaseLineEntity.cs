namespace MauiApp1.Infrastructure.Persistence.Entities;

internal sealed class PurchaseLineEntity
{
    public Guid OperationId { get; set; }

    public int LineNumber { get; set; }

    public Guid ProductId { get; set; }

    public long QuantityMilliUnits { get; set; }
}
