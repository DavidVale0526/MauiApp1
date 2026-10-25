using System.Globalization;
using MauiApp1.Application.Credit;
using MauiApp1.Application.Inventory;
using MauiApp1.Application.Purchases;
using MauiApp1.Application.Sales;
using MauiApp1.Domain.Inventory;
using MauiApp1.Presentation.Formatting;

namespace MauiApp1.Presentation.ViewModels;

public enum RecentOperationHistory { Sales, Purchases, Inventory, Payments }

public sealed record RecentOperationItem(Guid Id, DateTime OccurredAtUtc, string Type,
    string Description, string ValueText, string? Actor, RecentOperationHistory History)
{
    private DateTime LocalDate => DateTime.SpecifyKind(OccurredAtUtc, DateTimeKind.Utc).ToLocalTime();
    public string DateText => LocalDate.ToString("dd/MM/yyyy", CultureInfo.CurrentCulture);
    public string TimeText => LocalDate.ToString("HH:mm", CultureInfo.CurrentCulture);
    public bool HasActor => !string.IsNullOrWhiteSpace(Actor);
    public string AccentColor => History switch
    {
        RecentOperationHistory.Sales or RecentOperationHistory.Payments => "#007A56",
        RecentOperationHistory.Purchases => "#193459", _ => "#BD2727"
    };

    public static IReadOnlyList<RecentOperationItem> SelectLatest(
        IReadOnlyList<SaleHistoryEntry> sales, IReadOnlyList<PurchaseHistoryEntry> purchases,
        IReadOnlyList<InventoryMovementHistoryEntry> movements, IReadOnlyList<PaymentHistoryEntry> payments)
    {
        var items = new List<RecentOperationItem>();
        var represented = sales.Select(sale => sale.OperationId).Concat(purchases.Select(purchase => purchase.OperationId)).ToHashSet();
        items.AddRange(sales.Select(sale => new RecentOperationItem(sale.OperationId, sale.OccurredAtUtc,
            sale.IsCredit ? "Venta a crédito" : "Venta", Describe(sale.Lines.Select(line => line.ProductName)),
            WeeklySalesSummary.FormatAmount(sale.TotalMinorUnits / 100m), sale.ActorUserName, RecentOperationHistory.Sales)));
        items.AddRange(purchases.Select(purchase => new RecentOperationItem(purchase.OperationId, purchase.OccurredAtUtc,
            "Compra / entrada", Describe(purchase.Lines.Select(line => line.ProductName)),
            Quantities(purchase.Lines.Select(line => (line.Unit, line.QuantityMilliUnits))), null, RecentOperationHistory.Purchases)));
        foreach (var group in movements.Where(movement => !represented.Contains(movement.OperationId)).GroupBy(movement => movement.OperationId))
        {
            var first = group.First();
            var types = group.Select(movement => movement.Type).Distinct().ToArray();
            var type = types.Length != 1 ? "Movimiento" : first.Type switch
            {
                InventoryMovementType.OtherExit => "Salida", InventoryMovementType.MixedWaste => "Merma mixta",
                InventoryMovementType.PurchaseEntry => "Entrada", InventoryMovementType.SaleExit => "Salida por venta", _ => "Movimiento"
            };
            var descriptions = group.Select(movement => string.IsNullOrWhiteSpace(movement.Reason)
                ? movement.ProductName : $"{movement.ProductName}: {movement.Reason}");
            items.Add(new(first.OperationId, group.Max(movement => movement.OccurredAtUtc), type,
                Describe(descriptions), Quantities(group.Select(movement => (movement.Unit, movement.QuantityMilliUnits))),
                null, RecentOperationHistory.Inventory));
        }
        items.AddRange(payments.DistinctBy(payment => payment.PaymentId).Select(payment => new RecentOperationItem(
            payment.PaymentId, payment.OccurredAtUtc, "Abono de crédito", $"Venta {payment.OperationId:N}",
            WeeklySalesSummary.FormatAmount(payment.AmountMinorUnits / 100m), payment.ActorUserName, RecentOperationHistory.Payments)));
        return items.OrderByDescending(item => item.OccurredAtUtc).ThenByDescending(item => item.Id).Take(5).ToArray();
    }

    private static string Describe(IEnumerable<string> names)
    {
        var values = names.ToArray();
        return string.Join(", ", values.Take(3)) + (values.Length > 3 ? $" y {values.Length - 3} más" : string.Empty);
    }

    private static string Quantities(IEnumerable<(string Unit, long MilliUnits)> lines) =>
        string.Join(" · ", lines.GroupBy(line => line.Unit).Select(group =>
            QuantityText.Format(group.Sum(line => line.MilliUnits / 1000m), group.Key)));
}
