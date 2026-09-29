namespace MauiApp1.Application.Credit;

public sealed record PaymentHistoryEntry(
    Guid PaymentId,
    Guid OperationId,
    DateTime OccurredAtUtc,
    string ActorUserName,
    long AmountMinorUnits);
