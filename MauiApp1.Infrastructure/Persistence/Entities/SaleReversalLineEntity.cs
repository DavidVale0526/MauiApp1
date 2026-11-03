namespace MauiApp1.Infrastructure.Persistence.Entities;

internal sealed class SaleReversalLineEntity
{
    public Guid OperationId { get; set; }
    public int LineNumber { get; set; }
    public int SaleLineNumber { get; set; }
    public Guid SaleOperationId { get; set; }
    public Guid ProductId { get; set; }
    public string ProductNameSnapshot { get; set; } = string.Empty;
    public string UnitSnapshot { get; set; } = string.Empty;
    public long QuantityMilliUnits { get; set; }
    public long AmountMinorUnits { get; set; }
    public bool ReturnToStock { get; set; }
    public string? DisposalReason { get; set; }
}
