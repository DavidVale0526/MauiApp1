using MauiApp1.Domain.Identifiers;
using MauiApp1.Domain.Inventory;
using MauiApp1.Domain.Values;

namespace MauiApp1.Application.Inventory;

public sealed record InventoryMovementLineDraft(
    int LineNumber,
    ProductId ProductId,
    InventoryMovementType Type,
    Quantity Quantity,
    string? Reason = null);
