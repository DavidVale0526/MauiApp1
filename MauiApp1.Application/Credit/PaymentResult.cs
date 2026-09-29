namespace MauiApp1.Application.Credit;

public sealed record PaymentResult(bool Succeeded, bool AlreadyApplied, string? ErrorMessage)
{
    public static PaymentResult Success(bool alreadyApplied = false) => new(true, alreadyApplied, null);

    public static PaymentResult Failure(string message) => new(false, false, message);
}
