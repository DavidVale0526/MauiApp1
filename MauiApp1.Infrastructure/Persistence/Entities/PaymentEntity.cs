namespace MauiApp1.Infrastructure.Persistence.Entities;

internal sealed class PaymentEntity
{
    public Guid PaymentId { get; set; }

    public Guid OperationId { get; set; }

    public Guid ActorUserId { get; set; }

    public long AmountMinorUnits { get; set; }

    public DateTime OccurredAtUtc { get; set; }
}
