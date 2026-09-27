using MauiApp1.Application.Storage;
using MauiApp1.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MauiApp1.Infrastructure.Persistence;

public sealed class LocalDatabaseInitializer(IDbContextFactory<LocalDbContext> contextFactory)
    : ILocalDatabaseInitializer, ILocalDeviceIdentity
{
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private bool _initialized;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        await _initializationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }

            await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);

            var identity = await context.DeviceIdentities
                .SingleOrDefaultAsync(item => item.SingletonKey == DeviceIdentityEntity.SingletonKeyValue, cancellationToken)
                .ConfigureAwait(false);

            if (identity is null)
            {
                context.DeviceIdentities.Add(new DeviceIdentityEntity
                {
                    DeviceId = Guid.NewGuid(),
                    CreatedAtUtc = DateTime.UtcNow
                });
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            _initialized = true;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    public async Task<Guid> GetDeviceIdAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await context.DeviceIdentities
            .Where(item => item.SingletonKey == DeviceIdentityEntity.SingletonKeyValue)
            .Select(item => item.DeviceId)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
