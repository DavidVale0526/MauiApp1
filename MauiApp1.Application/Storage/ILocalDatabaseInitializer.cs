namespace MauiApp1.Application.Storage;

public interface ILocalDatabaseInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
