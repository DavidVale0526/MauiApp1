using MauiApp1.Domain.Identifiers;

namespace MauiApp1.Domain.Users;

public sealed record AuthenticatedUser(UserId Id, string UserName, UserRole Role);
