namespace MauiApp1.Application.Categories;

public sealed record CategoryOperationResult(bool Succeeded, string? ErrorMessage)
{
    public static CategoryOperationResult Success() => new(true, null);

    public static CategoryOperationResult Failure(string errorMessage) => new(false, errorMessage);
}
