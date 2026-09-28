using MauiApp1.Domain.Inventory;

namespace MauiApp1.Application.Inventory;

public sealed record InventoryMovementHistoryEntry(
    Guid MovementId,
    Guid OperationId,
    InventoryMovementType Type,
    string ProductName,
    string Unit,
    long QuantityMilliUnits,
    DateTime OccurredAtUtc,
    string? Reason);
