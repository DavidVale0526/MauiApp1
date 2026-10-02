using MauiApp1.Application.Storage;
using MauiApp1.Application.Authentication;
using MauiApp1.Infrastructure.Authentication;
using MauiApp1.Application.Categories;
using MauiApp1.Infrastructure.Categories;
using MauiApp1.Application.Products;
using MauiApp1.Infrastructure.Products;
using MauiApp1.Application.Inventory;
using MauiApp1.Application.Purchases;
using MauiApp1.Infrastructure.Inventory;
using MauiApp1.Infrastructure.Purchases;
using MauiApp1.Application.Sales;
using MauiApp1.Infrastructure.Sales;
using MauiApp1.Application.Customers;
using MauiApp1.Infrastructure.Customers;
using MauiApp1.Application.Credit;
using MauiApp1.Infrastructure.Credit;
using MauiApp1.Application.Reports;
using MauiApp1.Infrastructure.Reports;
using MauiApp1.Infrastructure.Sync;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MauiApp1.Infrastructure.Persistence;

public static class LocalPersistenceServiceCollectionExtensions
{
    public const string DatabaseFileName = "miscelanea-ari.db";

    public static IServiceCollection AddLocalPersistence(
        this IServiceCollection services, string databasePath, bool migrationsManagedExternally = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        var connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        services.AddDbContextFactory<LocalDbContext>(options => options.UseSqlite(connectionString));
        services.AddSingleton(provider => new LocalDatabaseInitializer(
            provider.GetRequiredService<IDbContextFactory<LocalDbContext>>(), migrationsManagedExternally));
        services.AddSingleton<ILocalDatabaseInitializer>(provider => provider.GetRequiredService<LocalDatabaseInitializer>());
        services.AddSingleton<ILocalDeviceIdentity>(provider => provider.GetRequiredService<LocalDatabaseInitializer>());
        services.AddScoped<IOperationOutbox, EfOperationOutbox>();
        services.AddScoped<ILocalUnitOfWork>(provider => provider.GetRequiredService<LocalDbContext>());
        services.AddSingleton<Pbkdf2PasswordHasher>();
        services.AddSingleton<CoordinatorBusinessOperationApplier>();
        services.AddSingleton<MauiApp1.Application.Sync.ICoordinatorConflictReviewService, MauiApp1.Infrastructure.Sync.CoordinatorConflictReviewService>();
        services.AddSingleton<MauiApp1.Application.Sync.ILocalOperationSyncService, MauiApp1.Infrastructure.Lan.LocalOperationSyncService>();
        services.AddSingleton<IAuthenticationService, LocalAuthenticationService>();
        services.AddTransient<ICategoryService, EfCategoryService>();
        services.AddTransient<IProductService, EfProductService>();
        services.AddTransient<IInventoryMovementService, EfInventoryMovementService>();
        services.AddSingleton(provider => new InventoryMovementWriter(
            provider.GetRequiredService<ILocalDeviceIdentity>()));
        services.AddTransient<IPurchaseService, EfPurchaseService>();
        services.AddTransient<ISaleService, EfSaleService>();
        services.AddTransient<ICustomerService, EfCustomerService>();
        services.AddTransient<IPaymentService, EfPaymentService>();
        services.AddTransient<IReportService, EfReportService>();
        return services;
    }
}
