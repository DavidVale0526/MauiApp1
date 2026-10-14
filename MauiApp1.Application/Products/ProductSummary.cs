using MauiApp1.Domain.Identifiers;
using MauiApp1.Domain.Values;
using System.Globalization;

namespace MauiApp1.Application.Products;

public sealed record ProductSummary(
    ProductId Id,
    string Name,
    MoneyAmount SalePrice,
    Quantity Stock,
    string Unit,
    CategoryId CategoryId,
    string CategoryName,
    Quantity MinimumStock,
    bool IsActive)
{
    public string StatusText => IsActive ? "Activo" : "Inactivo";

    public string ActionText => IsActive ? "Desactivar" : "Reactivar";

    public string SalePriceText => $"Precio: {SalePrice.ToDecimal().ToString("0.00", CultureInfo.CurrentCulture)}";

    public string StockText => $"Existencia: {Stock.ToDecimal().ToString("0.###", CultureInfo.CurrentCulture)} {Unit}";

    public string MinimumStockText => $"Mínimo: {MinimumStock.ToDecimal().ToString("0.###", CultureInfo.CurrentCulture)} {Unit}";
}
