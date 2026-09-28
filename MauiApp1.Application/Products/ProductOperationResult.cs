namespace MauiApp1.Application.Products;

public sealed record ProductOperationResult(bool Succeeded, string? ErrorMessage)
{
    public static ProductOperationResult Success() => new(true, null);

    public static ProductOperationResult Failure(string errorMessage) => new(false, errorMessage);
}
