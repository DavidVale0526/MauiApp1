namespace MauiApp1.Infrastructure.Persistence.Entities;

internal sealed class UserAccountEntity
{
    public Guid UserId { get; set; }

    public Guid? CoordinatorUserId { get; set; }

    public string UserName { get; set; } = string.Empty;

    public string NormalizedUserName { get; set; } = string.Empty;

    public byte[] PasswordSalt { get; set; } = [];

    public byte[] PasswordHash { get; set; } = [];

    public int PasswordIterations { get; set; }

    public int Role { get; set; }

    public bool IsEnabled { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
