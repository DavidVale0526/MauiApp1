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

    public string TotalText => $"C$ {FormatMoney(Sale.TotalMinorUnits)}";

    internal static string FormatMoney(long minorUnits) =>
        (minorUnits / 100m).ToString("0.00", CultureInfo.CurrentCulture);
}

public sealed record SaleTicketLine(SaleHistoryLine Line)
{
    public string NameText => Line.ProductName;

    public string QuantityAndUnitText =>
        $"{(Line.QuantityMilliUnits / 1000m).ToString("0.000", CultureInfo.CurrentCulture)} {Line.Unit} × C$ {SaleTicketViewModel.FormatMoney(Line.SalePriceMinorUnits)}";

    public string LineTotalText => $"C$ {SaleTicketViewModel.FormatMoney(Line.LineTotalMinorUnits)}";
}
