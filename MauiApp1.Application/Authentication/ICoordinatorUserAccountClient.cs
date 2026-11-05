using MauiApp1.Domain.Users;

namespace MauiApp1.Application.Authentication;

/// <summary>
/// Administración de cuentas y roles en el coordinador central.
/// </summary>
public interface ICoordinatorUserAccountClient
{
    Task<AccountProvisioningResult> CreateAccountAsync(
        string userName,
        string password,
        string recoveryCode,
        UserRole role,
        CancellationToken cancellationToken = default);

    Task<UserAccountListResult> GetAccountsAsync(CancellationToken cancellationToken = default);

    Task<AccountRoleChangeResult> ChangeRoleAsync(
        Guid userId,
        UserRole role,
        CancellationToken cancellationToken = default);
}
