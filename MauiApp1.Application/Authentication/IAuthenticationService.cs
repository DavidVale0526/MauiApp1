using MauiApp1.Domain.Users;

namespace MauiApp1.Application.Authentication;

public interface IAuthenticationService
{
    AuthenticatedUser? CurrentUser { get; }

    event EventHandler? AuthenticationStateChanged;

    Task<bool> HasAdministratorAsync(CancellationToken cancellationToken = default);

    Task<AuthenticationResult> CreateInitialAdministratorAsync(
        string userName,
        string password,
        CancellationToken cancellationToken = default);

    Task<AuthenticationResult> CreateCashierAccountAsync(
        string userName,
        string password,
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
