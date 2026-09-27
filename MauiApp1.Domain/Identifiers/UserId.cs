namespace MauiApp1.Domain.Identifiers;

public readonly record struct UserId(Guid Value)
{
    public static UserId New() => new(Guid.NewGuid());
}
