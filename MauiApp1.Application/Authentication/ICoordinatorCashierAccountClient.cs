namespace MauiApp1.Application.Authentication;

public interface ICoordinatorCashierAccountClient
{
    Task<AccountProvisioningResult> CreateCashierAsync(string userName, string password, string recoveryCode, CancellationToken cancellationToken = default);

    Task<CashierAccountListResult> GetCashiersAsync(CancellationToken cancellationToken = default);
}
