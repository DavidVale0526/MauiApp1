namespace MauiApp1.Application.Authentication;

public interface ICoordinatorCashierAccountClient
{
    Task<AuthenticationResult> CreateCashierAsync(string userName, string password, CancellationToken cancellationToken = default);

    Task<CashierAccountListResult> GetCashiersAsync(CancellationToken cancellationToken = default);
}
