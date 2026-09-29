namespace MauiApp1.Infrastructure.Persistence.Entities;

internal sealed class SaleEntity
{
    public Guid OperationId { get; set; }

    public DateTime OccurredAtUtc { get; set; }

    public Guid ActorUserId { get; set; }

    public long TotalMinorUnits { get; set; }

    public bool IsCredit { get; set; }

    public Guid? CustomerId { get; set; }
}
