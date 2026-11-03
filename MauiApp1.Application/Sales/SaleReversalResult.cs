namespace MauiApp1.Application.Sales;

public sealed record SaleReversalResult(bool Succeeded, string? ErrorMessage = null, bool AlreadyApplied = false)
{
    public static SaleReversalResult Success(bool alreadyApplied = false) => new(true, null, alreadyApplied);

    public static SaleReversalResult Failure(string message) => new(false, message);
}
