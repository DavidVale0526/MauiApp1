using MauiApp1.Domain.Users;

namespace MauiApp1.Application.Authentication;

public sealed record AuthenticationResult(bool Succeeded, AuthenticatedUser? User, string? ErrorMessage)
{
    public static AuthenticationResult Success(AuthenticatedUser user) => new(true, user, null);

    public static AuthenticationResult Failure(string errorMessage) => new(false, null, errorMessage);
}
