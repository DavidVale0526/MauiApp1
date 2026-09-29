namespace MauiApp1.Infrastructure.Persistence.Entities;

internal sealed class ReceivableEntity
{
    public Guid OperationId { get; set; }

    public Guid CustomerId { get; set; }

    public long OriginalAmountMinorUnits { get; set; }

    public long OutstandingMinorUnits { get; set; }

    public DateTime DueAtUtc { get; set; }
}
