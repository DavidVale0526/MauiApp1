using MauiApp1.Domain.Inventory;

namespace MauiApp1.Application.Inventory;

/// <summary>Una operación lógica con una o más líneas que se guardan juntas.</summary>
public sealed record InventoryOperationDraft(
    Guid OperationId,
    DateTime OccurredAtUtc,
    IReadOnlyList<InventoryMovementLineDraft> Lines,
    NegativeStockPolicy NegativeStockPolicy);
