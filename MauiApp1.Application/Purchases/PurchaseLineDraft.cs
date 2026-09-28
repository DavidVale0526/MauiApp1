using MauiApp1.Domain.Identifiers;
using MauiApp1.Domain.Values;

namespace MauiApp1.Application.Purchases;

public sealed record PurchaseLineDraft(ProductId ProductId, Quantity Quantity);
