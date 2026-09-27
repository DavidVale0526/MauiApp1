using MauiApp1.Infrastructure.Persistence.Entities;
using MauiApp1.Application.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MauiApp1.Infrastructure.Persistence;

public sealed class LocalDbContext(DbContextOptions<LocalDbContext> options) : DbContext(options), ILocalUnitOfWork
{
    private static readonly ValueConverter<DateTime, long> UtcDateTimeConverter = new(
        value => value.Ticks,
        ticks => new DateTime(ticks, DateTimeKind.Utc));

    internal DbSet<DeviceIdentityEntity> DeviceIdentities => Set<DeviceIdentityEntity>();

    internal DbSet<OutboxMessageEntity> OutboxMessages => Set<OutboxMessageEntity>();

    internal DbSet<UserAccountEntity> UserAccounts => Set<UserAccountEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DeviceIdentityEntity>(entity =>
        {
            entity.ToTable("DeviceIdentity", table => table.HasCheckConstraint(
                "CK_DeviceIdentity_SingletonKey", "\"SingletonKey\" = 1"));
            entity.HasKey(item => item.SingletonKey);
            entity.Property(item => item.SingletonKey).ValueGeneratedNever();
            entity.Property(item => item.DeviceId).IsRequired();
            entity.HasIndex(item => item.DeviceId).IsUnique();
            entity.Property(item => item.CreatedAtUtc)
                .HasConversion(UtcDateTimeConverter)
                .HasColumnType("INTEGER")
                .IsRequired();
        });

        modelBuilder.Entity<OutboxMessageEntity>(entity =>
        {
            entity.ToTable("OutboxMessages");
            entity.HasKey(item => item.OperationId);
            entity.Property(item => item.OperationId).ValueGeneratedNever();
            entity.Property(item => item.DeviceId).IsRequired();
            entity.Property(item => item.ActorUserId).IsRequired();
            entity.Property(item => item.OperationType).HasMaxLength(120).IsRequired();
            entity.Property(item => item.PayloadJson).IsRequired();
            entity.Property(item => item.CreatedAtUtc)
                .HasConversion(UtcDateTimeConverter)
                .HasColumnType("INTEGER")
                .IsRequired();
            entity.Property(item => item.Status).HasConversion<int>().IsRequired();
            entity.Property(item => item.AttemptCount).IsRequired();
            entity.Property(item => item.LastAttemptAtUtc)
                .HasConversion(UtcDateTimeConverter)
                .HasColumnType("INTEGER");
            entity.Property(item => item.ConfirmedAtUtc)
                .HasConversion(UtcDateTimeConverter)
                .HasColumnType("INTEGER");
            entity.HasIndex(item => new { item.Status, item.CreatedAtUtc });
            entity.HasIndex(item => item.DeviceId);
        });

        modelBuilder.Entity<UserAccountEntity>(entity =>
        {
            entity.ToTable("UserAccounts");
            entity.HasKey(item => item.UserId);
            entity.Property(item => item.UserId).ValueGeneratedNever();
            entity.Property(item => item.UserName).HasMaxLength(80).IsRequired();
            entity.Property(item => item.NormalizedUserName).HasMaxLength(80).IsRequired();
            entity.HasIndex(item => item.NormalizedUserName).IsUnique();
            entity.Property(item => item.PasswordSalt).IsRequired();
            entity.Property(item => item.PasswordHash).IsRequired();
            entity.Property(item => item.PasswordIterations).IsRequired();
            entity.Property(item => item.Role).IsRequired();
            entity.Property(item => item.IsEnabled).IsRequired();
            entity.Property(item => item.CreatedAtUtc)
                .HasConversion(UtcDateTimeConverter)
                .HasColumnType("INTEGER")
                .IsRequired();
        });
    }
}
