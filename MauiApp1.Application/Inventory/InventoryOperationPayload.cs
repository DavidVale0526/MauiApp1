using MauiApp1.Domain.Inventory;

namespace MauiApp1.Application.Inventory;

public sealed record InventoryOperationPayload(
    Guid OperationId,
    DateTime OccurredAtUtc,
    IReadOnlyList<InventoryMovementPayloadLine> Lines);

public sealed record InventoryMovementPayloadLine(
    int LineNumber,
    Guid ProductId,
    InventoryMovementType Type,
    long QuantityMilliUnits,
    string? Reason);
