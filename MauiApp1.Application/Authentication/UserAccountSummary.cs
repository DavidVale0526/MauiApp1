using MauiApp1.Domain.Users;

namespace MauiApp1.Application.Authentication;

/// <summary>
/// Cuenta registrada en el coordinador (o en la base activa cuando el equipo
/// trabaja como host). Incluye el rol para poder administrarlo.
/// </summary>
public sealed record UserAccountSummary(
    Guid UserId,
    string UserName,
    UserRole Role,
    bool IsEnabled,
    DateTime CreatedAtUtc);

public sealed record UserAccountListResult(
    bool Succeeded,
    IReadOnlyList<UserAccountSummary> Accounts,
    string? ErrorMessage)
{
    public static UserAccountListResult Success(IReadOnlyList<UserAccountSummary> accounts) =>
        new(true, accounts, null);

    public static UserAccountListResult Failure(string errorMessage) =>
        new(false, Array.Empty<UserAccountSummary>(), errorMessage);
}

public sealed record AccountRoleChangeResult(
    bool Succeeded,
    string? ErrorMessage,
    bool SessionRevoked = false)
{
    public static AccountRoleChangeResult Success(bool sessionRevoked = false) =>
        new(true, null, sessionRevoked);

    public static AccountRoleChangeResult Failure(string errorMessage) =>
        new(false, errorMessage);
}
