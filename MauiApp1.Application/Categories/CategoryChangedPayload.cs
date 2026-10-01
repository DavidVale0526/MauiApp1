namespace MauiApp1.Application.Categories;

public sealed record CategoryChangedPayload(Guid CategoryId, string Name, string? ExpectedName = null, Guid? ResolutionOfOperationId = null);
