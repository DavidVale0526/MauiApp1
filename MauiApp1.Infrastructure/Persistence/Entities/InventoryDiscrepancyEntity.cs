namespace MauiApp1.Infrastructure.Persistence.Entities;

internal sealed class InventoryDiscrepancyEntity
{
    public Guid DiscrepancyId { get; set; }
    public Guid OperationId { get; set; }
    public Guid ProductId { get; set; }
    public Guid ActorUserId { get; set; }
    public long ShortageMilliUnits { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public bool IsResolved { get; set; }
}
