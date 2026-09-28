namespace MauiApp1.Infrastructure.Persistence.Entities;

internal sealed class PurchaseEntity
{
    public Guid OperationId { get; set; }

    public DateTime OccurredAtUtc { get; set; }

    public Guid ActorUserId { get; set; }
}
