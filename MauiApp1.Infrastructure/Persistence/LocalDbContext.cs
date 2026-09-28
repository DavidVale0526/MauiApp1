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

    internal DbSet<CategoryEntity> Categories => Set<CategoryEntity>();

    internal DbSet<ProductEntity> Products => Set<ProductEntity>();

    internal DbSet<InventoryMovementEntity> InventoryMovements => Set<InventoryMovementEntity>();

    internal DbSet<PurchaseEntity> Purchases => Set<PurchaseEntity>();

    internal DbSet<PurchaseLineEntity> PurchaseLines => Set<PurchaseLineEntity>();

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

        modelBuilder.Entity<CategoryEntity>(entity =>
        {
            entity.ToTable("Categories");
            entity.HasKey(item => item.CategoryId);
            entity.Property(item => item.CategoryId).ValueGeneratedNever();
            entity.Property(item => item.Name).IsRequired();
            entity.Property(item => item.NormalizedName).IsRequired();
            entity.HasIndex(item => item.NormalizedName).IsUnique();
        });

        modelBuilder.Entity<ProductEntity>(entity =>
        {
            entity.ToTable("Products");
            entity.HasKey(item => item.ProductId);
            entity.Property(item => item.ProductId).ValueGeneratedNever();
            entity.Property(item => item.Name).IsRequired();
            entity.Property(item => item.SalePriceMinorUnits).IsRequired();
            entity.Property(item => item.StockMilliUnits).IsRequired();
            entity.Property(item => item.Unit).IsRequired();
            entity.Property(item => item.CategoryId).IsRequired();
            entity.Property(item => item.MinimumStockMilliUnits).IsRequired();
            entity.Property(item => item.IsActive).IsRequired();
            entity.HasOne<CategoryEntity>()
                .WithMany()
                .HasForeignKey(item => item.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(item => item.CategoryId);
            entity.HasIndex(item => item.IsActive);
        });

        modelBuilder.Entity<InventoryMovementEntity>(entity =>
        {
            entity.ToTable("InventoryMovements", table =>
            {
                table.HasCheckConstraint("CK_InventoryMovements_LineNumber_NonNegative", "\"LineNumber\" >= 0");
                table.HasCheckConstraint("CK_InventoryMovements_Quantity_Positive", "\"QuantityMilliUnits\" > 0");
            });
            entity.HasKey(item => item.MovementId);
            entity.Property(item => item.MovementId).ValueGeneratedNever();
            entity.Property(item => item.OperationId).IsRequired();
            entity.Property(item => item.LineNumber).IsRequired();
            entity.Property(item => item.ProductId).IsRequired();
            entity.Property(item => item.Type).IsRequired();
            entity.Property(item => item.QuantityMilliUnits).IsRequired();
            entity.Property(item => item.Reason);
            entity.Property(item => item.OccurredAtUtc)
                .HasConversion(UtcDateTimeConverter)
                .HasColumnType("INTEGER")
                .IsRequired();
            entity.Property(item => item.ActorUserId).IsRequired();
            entity.HasOne<ProductEntity>()
                .WithMany()
                .HasForeignKey(item => item.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserAccountEntity>()
                .WithMany()
                .HasForeignKey(item => item.ActorUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(item => new { item.OperationId, item.LineNumber }).IsUnique();
            entity.HasIndex(item => new { item.ProductId, item.OccurredAtUtc });
            entity.HasIndex(item => item.OperationId);
        });

        modelBuilder.Entity<PurchaseEntity>(entity =>
        {
            entity.ToTable("Purchases");
            entity.HasKey(item => item.OperationId);
            entity.Property(item => item.OperationId).ValueGeneratedNever();
            entity.Property(item => item.OccurredAtUtc)
                .HasConversion(UtcDateTimeConverter)
                .HasColumnType("INTEGER")
                .IsRequired();
            entity.Property(item => item.ActorUserId).IsRequired();
            entity.HasOne<UserAccountEntity>()
                .WithMany()
                .HasForeignKey(item => item.ActorUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(item => item.OccurredAtUtc);
        });

        modelBuilder.Entity<PurchaseLineEntity>(entity =>
        {
            entity.ToTable("PurchaseLines", table =>
            {
                table.HasCheckConstraint("CK_PurchaseLines_LineNumber_NonNegative", "\"LineNumber\" >= 0");
                table.HasCheckConstraint("CK_PurchaseLines_Quantity_Positive", "\"QuantityMilliUnits\" > 0");
            });
            entity.HasKey(item => new { item.OperationId, item.LineNumber });
            entity.Property(item => item.OperationId).IsRequired();
            entity.Property(item => item.LineNumber).IsRequired();
            entity.Property(item => item.ProductId).IsRequired();
            entity.Property(item => item.ProductNameSnapshot).IsRequired();
            entity.Property(item => item.UnitSnapshot).IsRequired();
            entity.Property(item => item.QuantityMilliUnits).IsRequired();
            entity.HasOne<PurchaseEntity>()
                .WithMany()
                .HasForeignKey(item => item.OperationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ProductEntity>()
                .WithMany()
                .HasForeignKey(item => item.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(item => item.ProductId);
        });
    }
}
