namespace MauiApp1.Application.Authentication;

public sealed record CashierAccountSummary(
    string UserName,
    bool IsEnabled,
    DateTime CreatedAtUtc);

public sealed record CashierAccountListResult(
    bool Succeeded,
    IReadOnlyList<CashierAccountSummary> Accounts,
    string? ErrorMessage)
{
    public static CashierAccountListResult Success(IReadOnlyList<CashierAccountSummary> accounts) =>
        new(true, accounts, null);

    public static CashierAccountListResult Failure(string errorMessage) =>
        new(false, Array.Empty<CashierAccountSummary>(), errorMessage);
}
