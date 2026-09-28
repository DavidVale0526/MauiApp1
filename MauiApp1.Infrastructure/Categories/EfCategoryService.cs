using System.Text.Json;
using MauiApp1.Application.Authentication;
using MauiApp1.Application.Categories;
using MauiApp1.Application.Storage;
using MauiApp1.Domain.Identifiers;
using MauiApp1.Infrastructure.Persistence;
using MauiApp1.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MauiApp1.Infrastructure.Categories;

internal sealed class EfCategoryService(
    IDbContextFactory<LocalDbContext> contextFactory,
    ILocalDeviceIdentity deviceIdentity,
    IAuthenticationService authenticationService) : ICategoryService
{
    public async Task<IReadOnlyList<CategorySummary>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        EnsureSignedIn();
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Categories
            .AsNoTracking()
            .OrderBy(category => category.NormalizedName)
            .Select(category => new CategorySummary(new CategoryId(category.CategoryId), category.Name))
            .ToListAsync(cancellationToken);
    }

    public async Task<CategoryOperationResult> CreateAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        var actor = authenticationService.CurrentUser;
        if (actor is null)
        {
            return CategoryOperationResult.Failure("Inicia sesión para administrar categorías.");
        }

        if (!TryNormalizeName(name, out var normalizedName, out var validationError))
        {
            return CategoryOperationResult.Failure(validationError);
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (await context.Categories.AnyAsync(category => category.NormalizedName == normalizedName, cancellationToken))
        {
            return CategoryOperationResult.Failure("Ya existe una categoría con ese nombre.");
        }

        var categoryId = CategoryId.New();
        context.Categories.Add(new CategoryEntity
        {
            CategoryId = categoryId.Value,
            Name = name.Trim(),
            NormalizedName = normalizedName
        });

        await EnqueueAsync(
            context,
            actor.Id.Value,
            "Category.Created",
            JsonSerializer.Serialize(new CategoryChangedPayload(categoryId.Value, name.Trim())),
            cancellationToken);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return CategoryOperationResult.Success();
        }
        catch (DbUpdateException)
        {
            return CategoryOperationResult.Failure("No se pudo guardar la categoría. Comprueba si ese nombre ya existe e inténtalo de nuevo.");
        }
    }

    public async Task<CategoryOperationResult> RenameAsync(
        Guid categoryId,
        string name,
        CancellationToken cancellationToken = default)
    {
        var actor = authenticationService.CurrentUser;
        if (actor is null)
        {
            return CategoryOperationResult.Failure("Inicia sesión para administrar categorías.");
        }

        if (categoryId == Guid.Empty)
        {
            return CategoryOperationResult.Failure("No se encontró la categoría que se desea editar.");
        }

        if (!TryNormalizeName(name, out var normalizedName, out var validationError))
        {
            return CategoryOperationResult.Failure(validationError);
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var category = await context.Categories.SingleOrDefaultAsync(item => item.CategoryId == categoryId, cancellationToken);
        if (category is null)
        {
            return CategoryOperationResult.Failure("La categoría ya no existe. Actualiza la lista e inténtalo de nuevo.");
        }

        if (await context.Categories.AnyAsync(
                item => item.CategoryId != categoryId && item.NormalizedName == normalizedName,
                cancellationToken))
        {
            return CategoryOperationResult.Failure("Ya existe una categoría con ese nombre.");
        }

        var cleanName = name.Trim();
        if (category.Name == cleanName)
        {
            return CategoryOperationResult.Success();
        }

        category.Name = cleanName;
        category.NormalizedName = normalizedName;
        await EnqueueAsync(
            context,
            actor.Id.Value,
            "Category.Renamed",
            JsonSerializer.Serialize(new CategoryChangedPayload(categoryId, cleanName)),
            cancellationToken);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return CategoryOperationResult.Success();
        }
        catch (DbUpdateException)
        {
            return CategoryOperationResult.Failure("No se pudo cambiar el nombre. Comprueba si ya existe otra categoría con ese nombre.");
        }
    }

    public async Task<CategoryOperationResult> DeleteAsync(
        Guid categoryId,
        CancellationToken cancellationToken = default)
    {
        var actor = authenticationService.CurrentUser;
        if (actor is null)
        {
            return CategoryOperationResult.Failure("Inicia sesión para administrar categorías.");
        }

        if (categoryId == Guid.Empty)
        {
            return CategoryOperationResult.Failure("No se encontró la categoría que se desea eliminar.");
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var category = await context.Categories.SingleOrDefaultAsync(item => item.CategoryId == categoryId, cancellationToken);
        if (category is null)
        {
            return CategoryOperationResult.Failure("La categoría ya no existe. Actualiza la lista.");
        }

        context.Categories.Remove(category);
        await EnqueueAsync(
            context,
            actor.Id.Value,
            "Category.Deleted",
            JsonSerializer.Serialize(new CategoryDeletedPayload(categoryId)),
            cancellationToken);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return CategoryOperationResult.Success();
        }
        catch (DbUpdateException)
        {
            return CategoryOperationResult.Failure("No se puede eliminar la categoría porque tiene productos asociados. Reasigna esos productos e inténtalo de nuevo.");
        }
    }

    private async Task EnqueueAsync(
        LocalDbContext context,
        Guid actorUserId,
        string operationType,
        string payload,
        CancellationToken cancellationToken)
    {
        var outbox = new EfOperationOutbox(context, deviceIdentity);
        await outbox.EnqueueAsync(
            new NewOutboxOperation(Guid.NewGuid(), actorUserId, operationType, payload), cancellationToken);
    }

    private void EnsureSignedIn()
    {
        if (authenticationService.CurrentUser is null)
        {
            throw new UnauthorizedAccessException("Inicia sesión para consultar categorías.");
        }
    }

    private static bool TryNormalizeName(string? name, out string normalizedName, out string errorMessage)
    {
        var cleanName = name?.Trim();
        if (string.IsNullOrWhiteSpace(cleanName))
        {
            normalizedName = string.Empty;
            errorMessage = "Escribe el nombre de la categoría.";
            return false;
        }

        normalizedName = cleanName.ToUpperInvariant();
        errorMessage = string.Empty;
        return true;
    }
}
