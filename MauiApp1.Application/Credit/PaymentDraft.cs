namespace MauiApp1.Application.Credit;

public sealed record PaymentDraft(
    Guid PaymentId,
    Guid OperationId,
    long AmountMinorUnits,
    DateTime OccurredAtUtc);
