namespace MauiApp1.Application.Products;

public sealed record ProductCatalogPage(IReadOnlyList<ProductSummary> Products, int TotalCount);
