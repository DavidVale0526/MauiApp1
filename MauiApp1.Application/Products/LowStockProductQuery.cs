namespace MauiApp1.Application.Products;

/// <summary>Applies the approved low-stock rule to the local product catalog.</summary>
public static class LowStockProductQuery
{
    public static IReadOnlyList<ProductSummary> Select(IEnumerable<ProductSummary> products) =>
        products
            .Where(product => product.IsActive && product.Stock.MilliUnits <= product.MinimumStock.MilliUnits)
            .OrderBy(product => product.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
}
