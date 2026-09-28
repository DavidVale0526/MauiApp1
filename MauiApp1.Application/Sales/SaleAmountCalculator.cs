using MauiApp1.Domain.Values;

namespace MauiApp1.Application.Sales;

public static class SaleAmountCalculator
{
    /// <summary>Calculates a line subtotal in cents, rounding halfway values away from zero.</summary>
    public static long CalculateLineTotalMinorUnits(MoneyAmount unitPrice, Quantity quantity)
    {
        if (unitPrice.MinorUnits < 0 || quantity.MilliUnits <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "El precio no puede ser negativo y la cantidad debe ser positiva.");
        }

        var exactMinorUnits = checked((decimal)unitPrice.MinorUnits * quantity.MilliUnits / 1000m);
        return checked((long)decimal.Round(exactMinorUnits, 0, MidpointRounding.AwayFromZero));
    }
}
