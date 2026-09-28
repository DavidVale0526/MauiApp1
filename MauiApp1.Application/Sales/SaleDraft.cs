using MauiApp1.Domain.Identifiers;
using MauiApp1.Domain.Values;

namespace MauiApp1.Application.Sales;

public sealed record SaleDraft(
    Guid OperationId,
    DateTime OccurredAtUtc,
    IReadOnlyList<SaleLineDraft> Lines);

public sealed record SaleLineDraft(
    ProductId ProductId,
    string ProductNameSnapshot,
    string UnitSnapshot,
    Quantity Quantity,
    MoneyAmount SalePrice);
