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

    internal DbSet<SaleEntity> Sales => Set<SaleEntity>();

    internal DbSet<SaleLineEntity> SaleLines => Set<SaleLineEntity>();

    internal DbSet<CustomerEntity> Customers => Set<CustomerEntity>();

    internal DbSet<ReceivableEntity> Receivables => Set<ReceivableEntity>();

    internal DbSet<PaymentEntity> Payments => Set<PaymentEntity>();

    internal DbSet<InventoryDiscrepancyEntity> InventoryDiscrepancies => Set<InventoryDiscrepancyEntity>();

    internal DbSet<DeviceSyncStateEntity> DeviceSyncStates => Set<DeviceSyncStateEntity>();

    internal DbSet<DeviceSyncConflictEntity> DeviceSyncConflicts => Set<DeviceSyncConflictEntity>();

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
            entity.Property(item => item.LastError).HasMaxLength(500);
            entity.HasIndex(item => new { item.Status, item.CreatedAtUtc });
            entity.HasIndex(item => item.DeviceId);
        });

        modelBuilder.Entity<UserAccountEntity>(entity =>
        {
            entity.ToTable("UserAccounts");
            entity.HasKey(item => item.UserId);
            entity.Property(item => item.UserId).ValueGeneratedNever();
            entity.Property(item => item.CoordinatorUserId);
            entity.Property(item => item.UserName).HasMaxLength(80).IsRequired();
            entity.Property(item => item.NormalizedUserName).HasMaxLength(80).IsRequired();
            entity.HasIndex(item => item.NormalizedUserName).IsUnique();
            entity.HasIndex(item => item.CoordinatorUserId).IsUnique();
            entity.Property(item => item.PasswordSalt).IsRequired();
            entity.Property(item => item.PasswordHash).IsRequired();
            entity.Property(item => item.PasswordIterations).IsRequired();
            entity.Property(item => item.RecoveryCodeSalt);
            entity.Property(item => item.RecoveryCodeHash);
            entity.Property(item => item.RecoveryCodeIterations);
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
            entity.Property(item => item.ImageData).HasColumnType("BLOB");
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
            entity.Property(item => item.ProductId);
            entity.Property(item => item.ProductNameSnapshot).HasDefaultValue(string.Empty).IsRequired();
            entity.Property(item => item.UnitSnapshot).HasDefaultValue(string.Empty).IsRequired();
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

        modelBuilder.Entity<SaleEntity>(entity =>
        {
            entity.ToTable("Sales", table => table.HasCheckConstraint(
                "CK_Sales_Total_NonNegative", "\"TotalMinorUnits\" >= 0"));
            entity.HasKey(item => item.OperationId);
            entity.Property(item => item.OperationId).ValueGeneratedNever();
            entity.Property(item => item.OccurredAtUtc)
                .HasConversion(UtcDateTimeConverter)
                .HasColumnType("INTEGER")
                .IsRequired();
            entity.Property(item => item.ActorUserId).IsRequired();
            entity.Property(item => item.TotalMinorUnits).IsRequired();
            entity.Property(item => item.IsCredit).IsRequired();
            entity.HasOne<CustomerEntity>()
                .WithMany()
                .HasForeignKey(item => item.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserAccountEntity>()
                .WithMany()
                .HasForeignKey(item => item.ActorUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(item => item.OccurredAtUtc);
            entity.HasIndex(item => item.CustomerId);
        });

        modelBuilder.Entity<SaleLineEntity>(entity =>
        {
            entity.ToTable("SaleLines", table =>
            {
                table.HasCheckConstraint("CK_SaleLines_LineNumber_NonNegative", "\"LineNumber\" >= 0");
                table.HasCheckConstraint("CK_SaleLines_Quantity_Positive", "\"QuantityMilliUnits\" > 0");
                table.HasCheckConstraint("CK_SaleLines_Price_NonNegative", "\"SalePriceMinorUnits\" >= 0");
                table.HasCheckConstraint("CK_SaleLines_Total_NonNegative", "\"LineTotalMinorUnits\" >= 0");
            });
            entity.HasKey(item => new { item.OperationId, item.LineNumber });
            entity.Property(item => item.OperationId).IsRequired();
            entity.Property(item => item.LineNumber).IsRequired();
            entity.Property(item => item.ProductId).IsRequired();
            entity.Property(item => item.ProductNameSnapshot).IsRequired();
            entity.Property(item => item.UnitSnapshot).IsRequired();
            entity.Property(item => item.QuantityMilliUnits).IsRequired();
            entity.Property(item => item.SalePriceMinorUnits).IsRequired();
            entity.Property(item => item.LineTotalMinorUnits).IsRequired();
            entity.HasOne<SaleEntity>()
                .WithMany()
                .HasForeignKey(item => item.OperationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ProductEntity>()
                .WithMany()
                .HasForeignKey(item => item.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(item => item.ProductId);
        });

        modelBuilder.Entity<CustomerEntity>(entity =>
        {
            entity.ToTable("Customers");
            entity.HasKey(item => item.CustomerId);
            entity.Property(item => item.CustomerId).ValueGeneratedNever();
            entity.Property(item => item.Name).HasMaxLength(120).IsRequired();
            entity.Property(item => item.NormalizedName).HasMaxLength(120).IsRequired();
            entity.Property(item => item.Phone).HasMaxLength(40);
            entity.Property(item => item.CreatedAtUtc)
                .HasConversion(UtcDateTimeConverter)
                .HasColumnType("INTEGER")
                .IsRequired();
            entity.HasIndex(item => item.NormalizedName).IsUnique();
        });

        modelBuilder.Entity<ReceivableEntity>(entity =>
        {
            entity.ToTable("Receivables", table =>
            {
                table.HasCheckConstraint("CK_Receivables_Original_NonNegative", "\"OriginalAmountMinorUnits\" >= 0");
                table.HasCheckConstraint("CK_Receivables_Outstanding_NonNegative", "\"OutstandingMinorUnits\" >= 0");
            });
            entity.HasKey(item => item.OperationId);
            entity.Property(item => item.OperationId).ValueGeneratedNever();
            entity.Property(item => item.CustomerId).IsRequired();
            entity.Property(item => item.OriginalAmountMinorUnits).IsRequired();
            entity.Property(item => item.OutstandingMinorUnits).IsRequired();
            entity.Property(item => item.OutstandingMinorUnits).IsConcurrencyToken();
            entity.Property(item => item.DueAtUtc)
                .HasConversion(UtcDateTimeConverter)
                .HasColumnType("INTEGER")
                .IsRequired();
            entity.HasOne<SaleEntity>()
                .WithOne()
                .HasForeignKey<ReceivableEntity>(item => item.OperationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<CustomerEntity>()
                .WithMany()
                .HasForeignKey(item => item.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(item => new { item.CustomerId, item.DueAtUtc });
        });


        modelBuilder.Entity<PaymentEntity>(entity =>
        {
            entity.ToTable("Payments", table => table.HasCheckConstraint(
                "CK_Payments_Amount_Positive", "\"AmountMinorUnits\" > 0"));
            entity.HasKey(item => item.PaymentId);
            entity.Property(item => item.PaymentId).ValueGeneratedNever();
            entity.Property(item => item.OperationId).IsRequired();
            entity.Property(item => item.ActorUserId).IsRequired();
            entity.Property(item => item.AmountMinorUnits).IsRequired();
            entity.Property(item => item.OccurredAtUtc)
                .HasConversion(UtcDateTimeConverter)
                .HasColumnType("INTEGER")
                .IsRequired();
            entity.HasOne<ReceivableEntity>()
                .WithMany()
                .HasForeignKey(item => item.OperationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserAccountEntity>()
                .WithMany()
                .HasForeignKey(item => item.ActorUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(item => new { item.OperationId, item.OccurredAtUtc });
        });

        modelBuilder.Entity<InventoryDiscrepancyEntity>(entity =>
        {
            entity.ToTable("InventoryDiscrepancies", table => table.HasCheckConstraint(
                "CK_InventoryDiscrepancies_Shortage_Positive", "\"ShortageMilliUnits\" > 0"));
            entity.HasKey(item => item.DiscrepancyId);
            entity.Property(item => item.DiscrepancyId).ValueGeneratedNever();
            entity.Property(item => item.OperationId).IsRequired();
            entity.Property(item => item.ProductId).IsRequired();
            entity.Property(item => item.ActorUserId).IsRequired();
            entity.Property(item => item.ShortageMilliUnits).IsRequired();
            entity.Property(item => item.CreatedAtUtc)
                .HasConversion(UtcDateTimeConverter)
                .HasColumnType("INTEGER")
                .IsRequired();
            entity.Property(item => item.IsResolved).IsRequired();
            entity.HasOne<ProductEntity>()
                .WithMany()
                .HasForeignKey(item => item.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserAccountEntity>()
                .WithMany()
                .HasForeignKey(item => item.ActorUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(item => new { item.ProductId, item.IsResolved, item.CreatedAtUtc });
            entity.HasIndex(item => new { item.OperationId, item.ProductId }).IsUnique();
        });

        modelBuilder.Entity<DeviceSyncStateEntity>(entity =>
        {
            entity.ToTable("DeviceSyncStates");
            entity.HasKey(item => item.DeviceId);
            entity.Property(item => item.DeviceId).ValueGeneratedNever();
            entity.Property(item => item.LastSequence).IsRequired();
            entity.Property(item => item.LastSuccessfulSyncAtUtc).HasConversion(UtcDateTimeConverter).HasColumnType("INTEGER");
        });

        modelBuilder.Entity<DeviceSyncConflictEntity>(entity =>
        {
            entity.ToTable("DeviceSyncConflicts");
            entity.HasKey(item => item.ConflictId);
            entity.Property(item => item.ConflictId).ValueGeneratedNever();
            entity.Property(item => item.CoordinatorSequence).IsRequired();
            entity.Property(item => item.OperationId).IsRequired();
            entity.Property(item => item.OperationType).HasMaxLength(120).IsRequired();
            entity.Property(item => item.PayloadJson).IsRequired();
            entity.Property(item => item.Message).HasMaxLength(500).IsRequired();
            entity.Property(item => item.CreatedAtUtc).HasConversion(UtcDateTimeConverter).HasColumnType("INTEGER").IsRequired();
            entity.Property(item => item.IsResolved).IsRequired();
            entity.HasIndex(item => item.OperationId).IsUnique();
            entity.HasIndex(item => new { item.IsResolved, item.CoordinatorSequence });
        });
    }
}
