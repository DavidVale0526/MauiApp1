namespace MauiApp1.Application.Storage;

public interface ILocalUnitOfWork
{
    /// <summary>Atomically saves all tracked local business changes and their outbox record.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
