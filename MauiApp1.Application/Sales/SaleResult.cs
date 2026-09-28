namespace MauiApp1.Application.Sales;

public sealed record SaleResult(bool Succeeded, bool AlreadyApplied, string? ErrorMessage)
{
    public static SaleResult Success(bool alreadyApplied = false) => new(true, alreadyApplied, null);

    public static SaleResult Failure(string errorMessage) => new(false, false, errorMessage);
}
