namespace MauiApp1.Infrastructure.Persistence.Entities;

internal sealed class SalePaymentEntity
{
    public Guid PaymentId { get; set; }

    public Guid OperationId { get; set; }

    public int Method { get; set; }

    public long AmountMinorUnits { get; set; }

    public long CashReceivedMinorUnits { get; set; }

    public long ChangeMinorUnits { get; set; }
}
