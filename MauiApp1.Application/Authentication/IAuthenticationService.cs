using MauiApp1.Domain.Users;

namespace MauiApp1.Application.Authentication;

public interface IAuthenticationService
{
    AuthenticatedUser? CurrentUser { get; }

    event EventHandler? AuthenticationStateChanged;

    Task<bool> HasAdministratorAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Indica si el dispositivo ya tiene alguna cuenta local (Administrador o cajero).
    /// Se usa para no forzar el alta inicial cuando ya existen credenciales válidas.
    /// </summary>
    Task<bool> HasAnyAccountAsync(CancellationToken cancellationToken = default);

    Task<AccountProvisioningResult> CreateInitialAdministratorAsync(
        string userName,
        string password,
        string recoveryCode,
        CancellationToken cancellationToken = default);

    Task<AccountProvisioningResult> CreateCashierAccountAsync(
        string userName,
        string password,
        string recoveryCode,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Crea una cuenta local con el rol indicado. Solo un Administrador puede hacerlo.
    /// </summary>
    Task<AccountProvisioningResult> CreateAccountAsync(
        string userName,
        string password,
        string recoveryCode,
        UserRole role,
        CancellationToken cancellationToken = default);

    Task<PasswordRecoveryResult> ResetPasswordAsync(
        string userName,
        string recoveryCode,
        string newPassword,
        CancellationToken cancellationToken = default);

    Task<PasswordRecoveryResult> ConfigureRecoveryCodeAsync(
        string currentPassword,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserAccountSummary>> GetAccountsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cambia el rol de una cuenta local. No permite degradar al último
    /// Administrador activo ni cambiar el rol de la propia cuenta.
    /// </summary>
    Task<AccountRoleChangeResult> ChangeRoleAsync(
        Guid userId,
        UserRole role,
        CancellationToken cancellationToken = default);

    Task<AuthenticationResult> SignInAsync(
        string userName,
        string password,
        CancellationToken cancellationToken = default);

    Task<AuthenticationResult> SignInWithCoordinatorUserAsync(
        Guid coordinatorUserId,
        string userName,
        UserRole role,
        string password,
        CancellationToken cancellationToken = default);

    Task<Guid?> GetCoordinatorUserIdAsync(Guid localUserId, CancellationToken cancellationToken = default);

    void SignOut();
}
