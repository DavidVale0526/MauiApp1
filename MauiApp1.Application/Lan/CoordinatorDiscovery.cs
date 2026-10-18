namespace MauiApp1.Application.Lan;

public interface ICoordinatorDiscovery
{
    Task<IReadOnlyList<CoordinatorCandidate>> FindAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}

public sealed record CoordinatorCandidate(
    string CoordinatorId,
    string DisplayName,
    string Address,
    int ProtocolVersion);
