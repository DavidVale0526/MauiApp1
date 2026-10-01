using MauiApp1.Domain.Users;

namespace MauiApp1.Application.Authentication;

public interface ICoordinatedSignInService
{
    Task<CoordinatedSignInResult> SignInAsync(string userName, string password, CancellationToken cancellationToken = default);
}

public sealed record CoordinatedSignInResult(AuthenticationResult Authentication, string? Notice = null);

public sealed record CoordinatorSignInResult(
    CoordinatorSignInStatus Status,
    Guid UserId = default,
    string UserName = "",
    UserRole Role = UserRole.Cashier,
    string SessionToken = "",
    DateTime ExpiresAtUtc = default);

public enum CoordinatorSignInStatus
{
    NotLinked,
    Unavailable,
    Rejected,
    Authenticated
}

public interface ICoordinatorAuthenticationClient
{
    Task<CoordinatorSignInResult> SignInAsync(string userName, string password, CancellationToken cancellationToken = default);
}

public sealed record CoordinatorUserSession(
    Guid LocalUserId,
    Guid CoordinatorUserId,
    string UserName,
    UserRole Role,
    string SessionToken,
    DateTime ExpiresAtUtc);

public interface ICoordinatorUserSessionStorage
{
    Task<CoordinatorUserSession?> ReadAsync();
    Task SaveAsync(CoordinatorUserSession session);
    Task RemoveAsync();
}
