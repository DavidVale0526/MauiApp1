using MauiApp1.Domain.Identifiers;
using MauiApp1.Domain.Values;

namespace MauiApp1.Application.Products;

/// <summary>Campos editables del catálogo. La existencia solo cambia con movimientos de inventario.</summary>
public sealed record ProductDraft(
    string Name,
    MoneyAmount SalePrice,
    string Unit,
    CategoryId CategoryId,
    Quantity MinimumStock);
