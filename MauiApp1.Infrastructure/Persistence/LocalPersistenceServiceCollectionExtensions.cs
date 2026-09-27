using MauiApp1.Application.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MauiApp1.Infrastructure.Persistence;

public static class LocalPersistenceServiceCollectionExtensions
{
    public const string DatabaseFileName = "miscelanea-ari.db";

    public static IServiceCollection AddLocalPersistence(this IServiceCollection services, string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        var connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        services.AddDbContextFactory<LocalDbContext>(options => options.UseSqlite(connectionString));
        services.AddSingleton<LocalDatabaseInitializer>();
        services.AddSingleton<ILocalDatabaseInitializer>(provider => provider.GetRequiredService<LocalDatabaseInitializer>());
        services.AddSingleton<ILocalDeviceIdentity>(provider => provider.GetRequiredService<LocalDatabaseInitializer>());
        services.AddScoped<IOperationOutbox, EfOperationOutbox>();
        services.AddScoped<ILocalUnitOfWork>(provider => provider.GetRequiredService<LocalDbContext>());
        return services;
    }
}
