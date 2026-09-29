using MauiApp1.Domain.Inventory;

namespace MauiApp1.Application.Reports;

public sealed record MovementReportRow(
    Guid MovementId,
    Guid OperationId,
    InventoryMovementType Type,
    string ProductName,
    string Unit,
    long QuantityMilliUnits,
    DateTime OccurredAtUtc,
    string? Reason,
    string ActorUserName);
