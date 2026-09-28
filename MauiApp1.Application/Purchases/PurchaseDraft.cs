namespace MauiApp1.Application.Purchases;

public sealed record PurchaseDraft(
    Guid OperationId,
    DateTime OccurredAtUtc,
    IReadOnlyList<PurchaseLineDraft> Lines);
