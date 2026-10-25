using MauiApp1.Application.Products;
using MauiApp1.Presentation.Formatting;

namespace MauiApp1.Presentation.ViewModels;

public sealed record OperationalStockAlert(string Name, string AlertType, string Description, string Status, bool IsCritical)
{
    public string AccentColor => IsCritical ? "#BD2727" : "#9A5900";
    public string BadgeColor => IsCritical ? "#FFE0DE" : "#FFE8C7";

    public static IReadOnlyList<OperationalStockAlert> Create(IEnumerable<ProductSummary> products) =>
        LowStockProductQuery.Select(products)
            .OrderBy(product => product.Stock.MilliUnits > 0)
            .ThenBy(product => product.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(product => new OperationalStockAlert(
                product.Name,
                product.Stock.MilliUnits <= 0 ? "CRÍTICO" : "BAJO",
                $"Existencia: {QuantityText.Format(product.Stock.ToDecimal(), product.Unit)} · Mínimo: {QuantityText.Format(product.MinimumStock.ToDecimal(), product.Unit)}",
                product.Stock.MilliUnits <= 0 ? "Sin existencias disponibles" : "Requiere reposición",
                product.Stock.MilliUnits <= 0))
            .ToArray();
}
