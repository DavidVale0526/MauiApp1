namespace MauiApp1.Infrastructure.Persistence.Entities;

internal sealed class SaleReversalEntity
{
    public Guid OperationId { get; set; }
    public Guid SaleOperationId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public Guid ActorUserId { get; set; }
    public int Kind { get; set; }
    public string Reason { get; set; } = string.Empty;
    public long AmountMinorUnits { get; set; }
    public int? RefundMethod { get; set; }
}
