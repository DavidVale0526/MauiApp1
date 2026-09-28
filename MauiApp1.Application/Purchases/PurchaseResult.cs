namespace MauiApp1.Application.Purchases;

public sealed record PurchaseResult(bool Succeeded, bool AlreadyApplied, string? ErrorMessage)
{
    public static PurchaseResult Success(bool alreadyApplied = false) => new(true, alreadyApplied, null);

    public static PurchaseResult Failure(string errorMessage) => new(false, false, errorMessage);
}
