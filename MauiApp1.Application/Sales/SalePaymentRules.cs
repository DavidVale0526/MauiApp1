namespace MauiApp1.Application.Sales;

public static class SalePaymentRules
{
    public static string? Validate(long totalMinorUnits, bool isCredit, IReadOnlyList<SalePaymentDraft>? payments)
    {
        if (totalMinorUnits < 0)
            return "El total de la venta no puede ser negativo.";

        if (isCredit)
            return payments is { Count: > 0 }
                ? "Las ventas a crédito no pueden registrar un pago inicial en este flujo."
                : null;

        if (payments is null || payments.Count == 0)
            return totalMinorUnits == 0 ? null : "Registra cómo se pagó la venta de contado.";
        if (payments.Any(payment => payment is null || payment.PaymentId == Guid.Empty ||
                                    !Enum.IsDefined(payment.Method) || payment.AmountMinorUnits <= 0 ||
                                    payment.CashReceivedMinorUnits < 0 || payment.ChangeMinorUnits < 0))
            return "El detalle de pago contiene un método o importe inválido.";
        if (payments.Select(payment => payment.PaymentId).Distinct().Count() != payments.Count ||
            payments.Select(payment => payment.Method).Distinct().Count() != payments.Count)
            return "Cada método de pago solo puede aparecer una vez por venta.";

        try
        {
            if (payments.Aggregate(0L, (sum, payment) => checked(sum + payment.AmountMinorUnits)) != totalMinorUnits)
                return "La suma de los pagos debe coincidir con el total de la venta.";
        }
        catch (OverflowException)
        {
            return "El total de los pagos supera el valor permitido.";
        }

        foreach (var payment in payments)
        {
            if (payment.Method == SalePaymentMethod.Cash)
            {
                if (payment.CashReceivedMinorUnits < payment.AmountMinorUnits ||
                    payment.ChangeMinorUnits != payment.CashReceivedMinorUnits - payment.AmountMinorUnits)
                    return "El efectivo recibido debe cubrir el importe en efectivo y el cambio debe ser correcto.";
            }
            else if (payment.CashReceivedMinorUnits != 0 || payment.ChangeMinorUnits != 0)
            {
                return "Tarjeta y transferencia no pueden registrar efectivo recibido ni cambio.";
            }
        }

        return null;
    }
}
