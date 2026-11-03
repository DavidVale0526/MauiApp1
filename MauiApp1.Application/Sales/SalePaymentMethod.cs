namespace MauiApp1.Application.Sales;

/// <summary>Payment method used to settle a retail sale.</summary>
public enum SalePaymentMethod
{
    Cash = 1,
    Card = 2,
    Transfer = 3
}

/// <param name="PaymentId">Stable idempotency key for this tender line.</param>
/// <param name="AmountMinorUnits">Amount applied to the sale.</param>
/// <param name="CashReceivedMinorUnits">Physical cash received; zero for non-cash methods.</param>
/// <param name="ChangeMinorUnits">Change returned to the customer.</param>
public sealed record SalePaymentDraft(
    Guid PaymentId,
    SalePaymentMethod Method,
    long AmountMinorUnits,
    long CashReceivedMinorUnits = 0,
    long ChangeMinorUnits = 0);
