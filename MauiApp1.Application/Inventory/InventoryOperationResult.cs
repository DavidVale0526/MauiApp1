namespace MauiApp1.Application.Inventory;

public sealed record InventoryOperationResult(bool Succeeded, bool AlreadyApplied, string? ErrorMessage)
{
    public static InventoryOperationResult Success(bool alreadyApplied = false) => new(true, alreadyApplied, null);

    public static InventoryOperationResult Failure(string errorMessage) => new(false, false, errorMessage);
}
