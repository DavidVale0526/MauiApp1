namespace MauiApp1.Application.Inventory;

public interface IInventoryMovementService
{
    /// <summary>Appends immutable movement lines and updates product balances in one local transaction.</summary>
    Task<InventoryOperationResult> ApplyAsync(
        InventoryOperationDraft operation,
        CancellationToken cancellationToken = default);
}
