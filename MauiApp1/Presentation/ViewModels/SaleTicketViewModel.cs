using System.Globalization;
using MauiApp1.Application.Sales;

namespace MauiApp1.Presentation.ViewModels;

public sealed class SaleTicketViewModel
{
    public SaleTicketViewModel(SaleHistoryEntry sale)
    {
        Sale = sale;
        Lines = sale.Lines.Select(line => new SaleTicketLine(line)).ToArray();
    }

    public SaleHistoryEntry Sale { get; }

    public IReadOnlyList<SaleTicketLine> Lines { get; }

    public string StoreName => "Miscelánea Ari";

    public string DocumentType => "TICKET INTERNO · NO FISCAL";

    public string ReferenceText => $"Folio interno: {Sale.OperationId.ToString("N")[..8].ToUpperInvariant()}";

    public string DateText => $"Fecha: {Sale.OccurredAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)}";

    public string ActorText => $"Atendió: {Sale.ActorUserName}";

    public string CurrencyText => "Córdobas nicaragüenses (C$)";

    public string CreditDetailsText => Sale.IsCredit
        ? $"Venta a crédito · Cliente: {Sale.CustomerName ?? "No disponible"} · Vence: {Sale.CreditDueAtUtc?.ToLocalTime().ToString("d", CultureInfo.CurrentCulture) ?? "No disponible"}"
        : "Venta de contado";

    public string PaymentDetailsText => Sale.IsCredit
        ? "El saldo se registra como cuenta por cobrar; los abonos se consultan por separado."
        : Sale.Payments is not { Count: > 0 }
            ? "Sin detalle de pago registrado para esta venta."
            : string.Join(Environment.NewLine, Sale.Payments.Select(payment => payment.Method switch
            {
                SalePaymentMethod.Cash => $"Efectivo aplicado: C$ {FormatMoney(payment.AmountMinorUnits)} · recibido: C$ {FormatMoney(payment.CashReceivedMinorUnits)} · cambio: C$ {FormatMoney(payment.ChangeMinorUnits)}",
                SalePaymentMethod.Card => $"Tarjeta: C$ {FormatMoney(payment.AmountMinorUnits)}",
                SalePaymentMethod.Transfer => $"Transferencia: C$ {FormatMoney(payment.AmountMinorUnits)}",
                _ => "Método de pago no disponible"
            }));

    public string TotalText => $"C$ {FormatMoney(Sale.TotalMinorUnits)}";

    internal static string FormatMoney(long minorUnits) =>
        (minorUnits / 100m).ToString("0.00", CultureInfo.CurrentCulture);
}

public sealed record SaleTicketLine(SaleHistoryLine Line)
{
    public string NameText => Line.ProductName;

    public string QuantityAndUnitText =>
        $"{(Line.QuantityMilliUnits / 1000m).ToString("0.###", CultureInfo.CurrentCulture)} {Line.Unit} × C$ {SaleTicketViewModel.FormatMoney(Line.SalePriceMinorUnits)}";

    public string LineTotalText => $"C$ {SaleTicketViewModel.FormatMoney(Line.LineTotalMinorUnits)}";
}
